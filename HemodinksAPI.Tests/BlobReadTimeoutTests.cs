using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using HemodinksAPI.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

/// <summary>
/// Simula um Azure Storage que aceita a conexão e nunca responde (como no throttling de 04/11/2026).
/// A leitura precisa falhar em segundos, não em minutos.
/// </summary>
public sealed class BlobReadTimeoutTests : IAsyncLifetime
{
    private static readonly TimeSpan MaxExpectedDuration = TimeSpan.FromSeconds(15);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<TcpClient> _connections = [];
    private Task? _acceptLoop;

    public ValueTask InitializeAsync()
    {
        _listener.Start();
        _acceptLoop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    lock (_connections)
                    {
                        _connections.Add(client);
                    }
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    return;
                }
            }
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        lock (_connections)
        {
            foreach (var connection in _connections)
            {
                connection.Dispose();
            }
        }

        if (_acceptLoop != null)
        {
            await _acceptLoop;
        }
    }

    [Fact]
    public async Task ProfilePhotoGetAsync_ReturnsNullQuicklyWhenStorageDoesNotRespond()
    {
        var storage = new AzureBlobProfilePhotoStorage(
            Options.Create(new ProfilePhotoStorageOptions
            {
                ConnectionString = HangingStorageConnectionString(),
                ContainerName = "profile-photos",
                ReadTimeoutSeconds = 1
            }),
            NullLogger<AzureBlobProfilePhotoStorage>.Instance);

        var stopwatch = Stopwatch.StartNew();
        var photo = await storage.GetAsync("users/2026/08/avatar.jpg", CancellationToken.None);
        stopwatch.Stop();

        Assert.Null(photo);
        Assert.True(stopwatch.Elapsed < MaxExpectedDuration, $"Leitura levou {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task PatientFileGetAsync_FailsQuicklyWhenStorageDoesNotRespond()
    {
        var storage = new AzureBlobPatientFileStorage(
            Options.Create(new PatientFileStorageOptions
            {
                ConnectionString = HangingStorageConnectionString(),
                ContainerName = "patient-files",
                ReadTimeoutSeconds = 1
            }),
            NullLogger<AzureBlobPatientFileStorage>.Instance);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsAsync<TimeoutException>(
            async () => await storage.GetAsync("pacientes/2026/08/exame.pdf", CancellationToken.None));
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < MaxExpectedDuration, $"Leitura levou {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task ProfilePhotoGetAsync_PropagatesCallerCancellation()
    {
        var storage = new AzureBlobProfilePhotoStorage(
            Options.Create(new ProfilePhotoStorageOptions
            {
                ConnectionString = HangingStorageConnectionString(),
                ContainerName = "profile-photos",
                ReadTimeoutSeconds = 30
            }),
            NullLogger<AzureBlobProfilePhotoStorage>.Instance);
        using var caller = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await storage.GetAsync("users/2026/08/avatar.jpg", caller.Token));
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-5, 8)]
    [InlineData(3, 3)]
    public void ResolveTimeout_UsesDefaultForInvalidValues(int configured, int expectedSeconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), BlobReadResilience.ResolveTimeout(configured));
    }

    private string HangingStorageConnectionString()
    {
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        // Chave fictícia gerada em tempo de execução; o servidor de teste nunca a valida.
        var fakeKey = Convert.ToBase64String(new byte[64]);
        return $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={fakeKey};BlobEndpoint=http://127.0.0.1:{port}/devstoreaccount1;";
    }
}
