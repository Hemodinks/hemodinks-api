using System.Net;
using System.Net.Http.Json;
using HemodinksAPI.Application.Services;
using HemodinksAPI.Infrastructure.Services;
using Microsoft.Extensions.Options;

namespace HemodinksAPI.Tests;

public sealed class EventEmailTransportTests
{
    [Fact]
    public async Task FunctionUsesProtectedIndividualPayloadWithoutExposingKeyInUrl()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var sender = new FunctionEventEmailSender(new ClientFactory(handler), Options.Create(new EventEmailFunctionOptions
        { BaseUrl = "https://worker.example.test", FunctionKey = "fake-function-key" }));
        await sender.SendAsync(new("recipient@example.test", "Event details"), default);
        Assert.Equal("https://worker.example.test/api/event-email/send", handler.Url);
        Assert.Equal("fake-function-key", handler.Key);
        Assert.Equal(new EventEmailMessage("recipient@example.test", "Event details"), handler.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task FunctionFailureIsNotReportedAsSentOrRetriedByTransport(HttpStatusCode status)
    {
        var handler = new RecordingHandler(status);
        var sender = new FunctionEventEmailSender(new ClientFactory(handler), Options.Create(new EventEmailFunctionOptions
        { BaseUrl = "https://worker.example.test/api/", FunctionKey = "fake-key" }));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new("recipient@example.test", "Event"), default));
        Assert.DoesNotContain("PRIVATE", error.Message);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("http://worker.example.test", "key")]
    [InlineData("https://worker.example.test?code=secret", "key")]
    [InlineData("https://user:pass@worker.example.test", "key")]
    [InlineData("https://worker.example.test", "")]
    public async Task InvalidTransportConfigurationFailsBeforeNetwork(string url, string key)
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        var sender = new FunctionEventEmailSender(new ClientFactory(handler), Options.Create(new EventEmailFunctionOptions { BaseUrl = url, FunctionKey = key }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(new("recipient@example.test", "Event"), default));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("invalid", "Event")]
    [InlineData("a@example.test\r\nb@example.test", "Event")]
    [InlineData("Name <a@example.test>", "Event")]
    [InlineData("a@example.test", "")]
    [InlineData("a@example.test", "\0")]
    public void MessageValidationRejectsInvalidRecipientsAndBodies(string email, string body)
        => Assert.False(new EventEmailMessage(email, body).IsValid());

    [Fact]
    public void MessageValidationLimitsPayloadSize()
        => Assert.False(new EventEmailMessage("a@example.test", new string('x', 5001)).IsValid());

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Url { get; private set; }
        public string? Key { get; private set; }
        public EventEmailMessage? Message { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++; Url = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.GetValues("x-functions-key").Single();
            Message = await request.Content!.ReadFromJsonAsync<EventEmailMessage>(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent("PRIVATE PROVIDER CONTENT") };
        }
    }
}
