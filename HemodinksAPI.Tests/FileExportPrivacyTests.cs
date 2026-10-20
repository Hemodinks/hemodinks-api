extern alias workers;

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HemodinksAPI.Application.Async;
using FileExportFunction = workers::HemodinksAPI.Workers.Functions.FileExportFunction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HemodinksAPI.Tests;

public sealed class FileExportPrivacyTests
{
    [Theory]
    [InlineData("pdf")]
    [InlineData("xlsx")]
    public async Task ExportContainerCreation_DoesNotGrantAnonymousBlobAccess(string format)
    {
        // Exercise the Azure SDK requests against a local HTTP endpoint, without Azure credentials.
        var portProbe = new TcpListener(IPAddress.Loopback, 0);
        portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
        portProbe.Stop();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var job = new FileExportQueueMessage(Guid.NewGuid(), "pacientes", format, 42, 1, 2,
            DateTime.UtcNow, new Dictionary<string, string?> { ["search"] = "private-filter" });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureWebJobsStorage"] = $"DefaultEndpointsProtocol=http;AccountName=audittest;AccountKey={Convert.ToBase64String(new byte[32])};BlobEndpoint=http://127.0.0.1:{port}/audittest;",
            ["ExportsContainerName"] = "exports"
        }).Build();
        var worker = new FileExportFunction(configuration, NullLogger<FileExportFunction>.Instance);
        var serve = Task.Run(async () =>
        {
            var requests = new List<(string Path, string? PublicAccess)>();
            for (var i = 0; i < 2; i++)
            {
                var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
                requests.Add((context.Request.RawUrl!, context.Request.Headers["x-ms-blob-public-access"]));
                await context.Request.InputStream.CopyToAsync(Stream.Null, timeout.Token);
                context.Response.StatusCode = 201;
                context.Response.Headers["ETag"] = "\"audit-etag\"";
                context.Response.Headers["Last-Modified"] = DateTime.UtcNow.ToString("R");
                context.Response.Close();
            }
            return requests;
        }, timeout.Token);
        await worker.GenerateFileExport(JsonSerializer.Serialize(job, new JsonSerializerOptions(JsonSerializerDefaults.Web)), timeout.Token);
        var requests = await serve;
        Assert.Contains("restype=container", requests[0].Path);
        Assert.Null(requests[0].PublicAccess);
        Assert.Contains($"/exports/clinica-2/pacientes/{job.JobId:N}.{format}", requests[1].Path);
    }
}
