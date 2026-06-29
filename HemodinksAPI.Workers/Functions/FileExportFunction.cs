using System.IO.Compression;
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
        [QueueTrigger(AsyncQueueNames.FileExportJobs, Connection = "AzureWebJobsStorage")] string queueMessage,
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
        var blobName = $"{job.Resource}/{job.JobId:N}.{job.Format.ToLowerInvariant()}";
        var containerClient = new BlobContainerClient(GetStorageConnectionString(), containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);

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
        var lines = CreateExportLines(job);
        var text = string.Join("\\n", lines);
        var content = $"BT /F1 11 Tf 50 760 Td ({EscapePdfText(text)}) Tj ET";
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream"
        };

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true);
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
        var rows = CreateExportLines(job)
            .Select((value, index) => $"""
                <row r="{index + 1}">
                  <c r="A{index + 1}" t="inlineStr"><is><t>{WebUtility.HtmlEncode(value)}</t></is></c>
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
            $"Job: {job.JobId}",
            $"Recurso: {job.Resource}",
            $"Formato: {job.Format}",
            $"Solicitante: {job.RequestedByUserId}",
            $"Perfil: {job.RequestedByPerfilId}",
            $"Solicitado em UTC: {job.RequestedAt:O}",
            $"Filtros: {filters}"
        ];
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
