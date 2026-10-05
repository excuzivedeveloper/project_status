using System.Net;
using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

public class ApiAuthenticationTests
{
    [Fact]
    public async Task Api_requests_send_bearer_token_and_report_auth_failures()
    {
        var handler = new RecordingHandler();
        using var api = new ApiClient("https://example.invalid", "test-token", handler);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
        Assert.Equal("Bearer", handler.Scheme);
        Assert.Equal("test-token", handler.Token);
        Assert.Equal("/api/state", handler.Path);
        Assert.Equal(Strings.ErrorAuthenticationFailed, error.Message);
    }

    [Fact]
    public async Task Changed_token_is_used_only_by_subsequent_requests()
    {
        var handler = new RecordingHandler();
        using var api = new ApiClient("https://example.invalid", "first-token", handler);
        await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
        api.SetApiToken("second-token");
        await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
        Assert.Equal(new[] { "first-token", "second-token" }, handler.Tokens);
    }

    [Fact]
    public async Task Missing_token_never_sends_a_request()
    {
        var handler = new RecordingHandler();
        using var api = new ApiClient("https://example.invalid", "", handler);
        var error = await Assert.ThrowsAsync<ApiException>(() => api.GetStateAsync());
        Assert.Equal(Strings.ErrorApiTokenRequired, error.Message);
        Assert.False(handler.WasCalled);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }
        public string? Scheme { get; private set; }
        public string? Token { get; private set; }
        public string? Path { get; private set; }
        public List<string?> Tokens { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            Scheme = request.Headers.Authorization?.Scheme;
            Token = request.Headers.Authorization?.Parameter;
            Path = request.RequestUri?.AbsolutePath;
            Tokens.Add(Token);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }
    }
}
