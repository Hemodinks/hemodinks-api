using System.IO.Compression;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using HemodinksAPI.Application.Async;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HemodinksAPI.Workers.Functions;

public class FileExportFunction
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IConfiguration _configuration;
    private readonly ILogger<FileExportFunction> _logger;

    public FileExportFunction(
        IConfiguration configuration,
        ILogger<FileExportFunction> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    [Function(nameof(GenerateFileExport))]
    public async Task GenerateFileExport(
        [QueueTrigger("%FileExportQueueName%", Connection = "AzureWebJobsStorage")] string queueMessage,
        CancellationToken cancellationToken)
    {
        var job = JsonSerializer.Deserialize<FileExportQueueMessage>(queueMessage, JsonOptions)
            ?? throw new InvalidOperationException("Mensagem de exportacao invalida");

        var bytes = job.Format.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
            ? CreateXlsx(job)
            : CreatePdf(job);
        var contentType = job.Format.Equals("xlsx", StringComparison.OrdinalIgnoreCase)
            ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
            : "application/pdf";

        var containerName = _configuration["ExportsContainerName"] ?? "exports";
        var blobName = $"clinica-{job.ClinicaId}/{job.Resource}/{job.JobId:N}.{job.Format.ToLowerInvariant()}";
        var containerClient = new BlobContainerClient(GetStorageConnectionString(), containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken);

        var blobClient = containerClient.GetBlobClient(blobName);
        await using var stream = new MemoryStream(bytes);
        await blobClient.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = contentType
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Exportacao {JobId} gerada em {BlobUri}",
            job.JobId,
            blobClient.Uri);
    }

    private string GetStorageConnectionString()
    {
        return _configuration["AzureWebJobsStorage"]
            ?? throw new InvalidOperationException("AzureWebJobsStorage deve ser configurado no Function App.");
    }

    private static byte[] CreatePdf(FileExportQueueMessage job)
    {
        var lines = CreateDocumentRows(job).SelectMany(FormatPdfRow).ToArray();
        // A3 landscape keeps the complete clinical table legible without dropping columns.
        var pages = lines.Chunk(68).ToArray();
        var pageReferences = new List<string>();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            string.Empty,
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"
        };
        for (var page = 0; page < pages.Length; page++)
        {
            var pageObject = objects.Count + 1;
            pageReferences.Add($"{pageObject} 0 R");
            var text = new[] { $"HemoDinks | {job.Resource} | {job.ReportType} | pagina {page + 1}/{pages.Length}" }
                .Concat(pages[page]);
            var content = "BT /F1 8 Tf 11 TL 35 807 Td\n" + string.Join("\nT*\n", text.Select(line => $"({EscapePdfText(line)}) Tj")) + "\nET";
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 1191 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {pageObject + 1} 0 R >>");
            objects.Add($"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream");
        }
        objects[1] = $"<< /Type /Pages /Kids [{string.Join(' ', pageReferences)}] /Count {pages.Length} >>";

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.Latin1, leaveOpen: true);
        writer.WriteLine("%PDF-1.4");
        var offsets = new List<long> { 0 };

        for (var index = 0; index < objects.Count; index++)
        {
            writer.Flush();
            offsets.Add(stream.Position);
            writer.WriteLine($"{index + 1} 0 obj");
            writer.WriteLine(objects[index]);
            writer.WriteLine("endobj");
        }

        writer.Flush();
        var xrefOffset = stream.Position;
        writer.WriteLine("xref");
        writer.WriteLine($"0 {objects.Count + 1}");
        writer.WriteLine("0000000000 65535 f ");

        foreach (var offset in offsets.Skip(1))
        {
            writer.WriteLine($"{offset:0000000000} 00000 n ");
        }

        writer.WriteLine("trailer");
        writer.WriteLine($"<< /Size {objects.Count + 1} /Root 1 0 R >>");
        writer.WriteLine("startxref");
        writer.WriteLine(xrefOffset);
        writer.WriteLine("%%EOF");
        writer.Flush();

        return stream.ToArray();
    }

    private static byte[] CreateXlsx(FileExportQueueMessage job)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="Exportacao" sheetId="1" r:id="rId1"/>
                  </sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/worksheets/sheet1.xml", CreateWorksheetXml(job));
        }

        return stream.ToArray();
    }

    private static string CreateWorksheetXml(FileExportQueueMessage job)
    {
        var rows = CreateDocumentRows(job)
            .Select((values, index) => $"""
                <row r="{index + 1}">
                  {string.Join(string.Empty, values.Select((value, column) => $"<c r=\"{ColumnName(column)}{index + 1}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{WebUtility.HtmlEncode(XmlText(value ?? string.Empty))}</t></is></c>"))}
                </row>
                """);

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <sheetData>
                {string.Join(Environment.NewLine, rows)}
              </sheetData>
            </worksheet>
            """;
    }

    private static IReadOnlyList<string> CreateExportLines(FileExportQueueMessage job)
    {
        var filters = job.Filters.Count == 0
            ? "sem filtros"
            : JsonSerializer.Serialize(job.Filters, JsonOptions);

        return
        [
            "Hemodinks exportacao",
            "Solicitacao legada: somente metadados; este arquivo nao contem registros clinicos.",
            $"Job: {job.JobId}",
            $"Recurso: {job.Resource}",
            $"Formato: {job.Format}",
            $"Solicitante: {job.RequestedByUserId}",
            $"Perfil: {job.RequestedByPerfilId}",
            $"Clinica: {job.ClinicaId}",
            $"Solicitado em UTC: {job.RequestedAt:O}",
            $"Filtros: {filters}"
        ];
    }

    private static IEnumerable<string?[]> CreateDocumentRows(FileExportQueueMessage job)
    {
        if (job.Snapshot == null)
        {
            foreach (var line in CreateExportLines(job)) yield return [line];
            yield break;
        }
        yield return ["Hemodinks exportacao", job.Resource, job.ReportType];
        yield return ["Registros", job.Snapshot.TotalRows.ToString(CultureInfo.InvariantCulture)];
        yield return ["Solicitado em UTC", job.RequestedAt.ToString("O", CultureInfo.InvariantCulture)];
        if (job.ReportType == "completo")
        {
            yield return job.Snapshot.Columns.Select(column => (string?)column).ToArray();
            foreach (var row in job.Snapshot.Rows) yield return row;
        }
        yield return ["Totais por tipo de cirurgia"];
        yield return ["Tipo cirurgia", "Quantidade", "Honorarios cirurgiao", "Honorarios auxiliares", "Repasse medico", "Valor glosa"];
        foreach (var total in job.Snapshot.Totals)
            yield return [total.TipoCirurgia, total.Count.ToString(CultureInfo.InvariantCulture),
                Money(total.HonorariosCirurgiao), Money(total.HonorariosAuxiliares), Money(total.RepasseMedico), Money(total.ValorGlosa)];
        yield return ["Total", job.Snapshot.TotalRows.ToString(CultureInfo.InvariantCulture),
            Money(Sum(job.Snapshot.Totals.Select(total => total.HonorariosCirurgiao))),
            Money(Sum(job.Snapshot.Totals.Select(total => total.HonorariosAuxiliares))),
            Money(Sum(job.Snapshot.Totals.Select(total => total.RepasseMedico))),
            Money(Sum(job.Snapshot.Totals.Select(total => total.ValorGlosa)))];
    }

    private static string? Money(decimal? value) => value?.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var known = values.Where(value => value.HasValue).ToArray();
        return known.Length == 0 ? null : known.Sum();
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
            name = (char)('A' + (value - 1) % 26) + name;
        return name;
    }

    private static string XmlText(string value) => string.Concat(value.Where(character =>
        character is '\t' or '\r' or '\n' || character >= ' ' && character is not '\uFFFE' and not '\uFFFF'));

    private static IEnumerable<string> FormatPdfRow(string?[] row)
    {
        int[] widths = row.Length switch
        {
            14 => [7, 22, 10, 8, 16, 16, 16, 14, 27, 10, 10, 10, 10, 10],
            6 => [24, 12, 24, 24, 24, 24],
            _ => Enumerable.Repeat(Math.Max(15, 180 / Math.Max(1, row.Length)), row.Length).ToArray()
        };
        var cells = row.Select((value, index) => WrapPdfText(value ?? string.Empty, widths[index]).ToArray()).ToArray();
        var height = cells.Length == 0 ? 1 : cells.Max(cell => cell.Length);
        for (var line = 0; line < height; line++)
            yield return string.Join(" | ", cells.Select((cell, index) => (line < cell.Length ? cell[line] : string.Empty).PadRight(widths[index])));
    }

    private static IEnumerable<string> WrapPdfText(string value, int width)
    {
        // Built-in PDF fonts cover Portuguese Latin text. Preserve other characters as explicit
        // Unicode escapes instead of silently replacing or truncating clinical information.
        var safe = string.Concat(value.Select(character => character is '\r' or '\n' or '\t' ? " "
            : character < ' ' || character > '\u00FF' ? $"\\u{(int)character:X4}" : character.ToString()));
        if (safe.Length == 0) { yield return string.Empty; yield break; }
        for (var offset = 0; offset < safe.Length; offset += width)
            yield return safe.Substring(offset, Math.Min(width, safe.Length - offset));
    }

    private static string EscapePdfText(string text)
    {
        return text
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }
}
