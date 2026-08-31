using System.IO.Compression;

namespace HemodinksAPI.Infrastructure.Storage;

internal static class PatientFileValidation
{
    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation"
        };

    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static async Task<ValidatedPatientFile> ValidateAsync(
        UploadedFile file,
        Stream stream,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Arquivo vazio");
        }

        if (file.Length > maxBytes)
        {
            throw FileTooLarge(maxBytes);
        }

        var originalName = Path.GetFileName(file.FileName.Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(originalName)
            || originalName.Length > 255
            || originalName.Any(char.IsControl))
        {
            throw new InvalidOperationException("Nome do arquivo inválido.");
        }

        var extension = Path.GetExtension(originalName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.TryGetValue(extension, out var contentType))
        {
            throw new InvalidOperationException("Use arquivo PDF, DOC, DOCX, JPG, JPEG, PNG, XLS, XLSX, TXT, CSV, PPT ou PPTX");
        }

        if (!stream.CanSeek)
        {
            throw new InvalidOperationException("O fluxo do arquivo precisa permitir validacao antes do armazenamento.");
        }

        var actualLength = await MeasureAndInspectAsync(stream, extension, maxBytes, cancellationToken);
        if (actualLength <= 0)
        {
            throw new InvalidOperationException("Arquivo vazio");
        }

        stream.Position = 0;
        ValidateSignature(stream, extension);
        stream.Position = 0;

        if (IsOpenXml(extension))
        {
            ValidateOpenXmlPackage(stream, extension, maxBytes);
            stream.Position = 0;
        }

        return new ValidatedPatientFile(originalName, extension.ToLowerInvariant(), contentType, actualLength);
    }

    private static async Task<long> MeasureAndInspectAsync(
        Stream stream,
        string extension,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        stream.Position = 0;
        var buffer = new byte[81920];
        long total = 0;
        var textFile = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csv", StringComparison.OrdinalIgnoreCase);

        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > maxBytes)
            {
                throw FileTooLarge(maxBytes);
            }

            if (textFile && buffer.AsSpan(0, read).Contains((byte)0))
            {
                throw new InvalidOperationException("O conteúdo do arquivo não corresponde ao tipo informado.");
            }
        }

        return total;
    }

    private static void ValidateSignature(Stream stream, string extension)
    {
        Span<byte> header = stackalloc byte[8];
        var read = stream.Read(header);
        var valid = extension.ToLowerInvariant() switch
        {
            ".pdf" => read >= 5 && header[..5].SequenceEqual("%PDF-"u8),
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            ".png" => read >= PngSignature.Length && header[..PngSignature.Length].SequenceEqual(PngSignature),
            ".doc" or ".xls" or ".ppt" => read >= OleSignature.Length && header[..OleSignature.Length].SequenceEqual(OleSignature),
            ".docx" or ".xlsx" or ".pptx" => read >= 4
                && header[0] == 0x50 && header[1] == 0x4B
                && ((header[2] == 0x03 && header[3] == 0x04)
                    || (header[2] == 0x05 && header[3] == 0x06)
                    || (header[2] == 0x07 && header[3] == 0x08)),
            ".txt" or ".csv" => true,
            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException("O conteúdo do arquivo não corresponde à extensão informada.");
        }
    }

    private static void ValidateOpenXmlPackage(Stream stream, string extension, long maxBytes)
    {
        try
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count == 0 || archive.Entries.Count > 1000)
            {
                throw new InvalidOperationException("Pacote Office inválido.");
            }

            var requiredFolder = extension.ToLowerInvariant() switch
            {
                ".docx" => "word/",
                ".xlsx" => "xl/",
                ".pptx" => "ppt/",
                _ => throw new InvalidOperationException("Pacote Office inválido.")
            };
            var hasContentTypes = false;
            var hasApplicationFolder = false;
            long expandedLength = 0;

            foreach (var entry in archive.Entries)
            {
                expandedLength += entry.Length;
                if (expandedLength > maxBytes * 10)
                {
                    throw new InvalidOperationException("Pacote Office excede o limite de conteúdo descompactado.");
                }

                hasContentTypes |= entry.FullName.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase);
                hasApplicationFolder |= entry.FullName.StartsWith(requiredFolder, StringComparison.OrdinalIgnoreCase);
            }

            if (!hasContentTypes || !hasApplicationFolder)
            {
                throw new InvalidOperationException("O conteúdo do arquivo não corresponde à extensão informada.");
            }
        }
        catch (InvalidDataException)
        {
            throw new InvalidOperationException("Pacote Office inválido.");
        }
    }

    private static bool IsOpenXml(string extension) =>
        extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
        || extension.Equals(".pptx", StringComparison.OrdinalIgnoreCase);

    private static InvalidOperationException FileTooLarge(long maxBytes) =>
        new($"O arquivo deve ter no máximo {maxBytes / 1024 / 1024} MB");
}

internal sealed record ValidatedPatientFile(
    string OriginalName,
    string Extension,
    string ContentType,
    long SizeBytes);
