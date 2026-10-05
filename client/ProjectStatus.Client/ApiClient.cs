using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Text.Json;

namespace ProjectStatus.Client;

internal sealed class ApiClient : IDisposable
{
    private readonly HttpClient _http;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private string _serverAddress;
    private string _apiToken = string.Empty;

    public ApiClient(string serverAddress, string apiToken, HttpMessageHandler? handler = null)
    {
        _serverAddress = AppSettings.NormalizeServerAddress(serverAddress);

        _http = new HttpClient(handler ?? new HttpClientHandler())
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
        SetApiToken(apiToken);
    }

    public void SetApiToken(string apiToken)
    {
        Volatile.Write(ref _apiToken, apiToken);
    }

    public void SetServerAddress(string serverAddress)
    {
        _serverAddress = AppSettings.NormalizeServerAddress(serverAddress);
    }

    public async Task<StateSnapshot> GetStateAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/state");
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<StateSnapshot>(JsonOptions)
            ?? new StateSnapshot();
    }

    public async Task<ProjectDto> CreateProjectAsync(ProjectPayload payload)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/projects", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task<ProjectDto> UpdateProjectAsync(int projectId, ProjectPayload payload)
    {
        using var response = await SendAsync(HttpMethod.Put, $"/api/projects/{projectId}", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task DeleteProjectAsync(int projectId)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/api/projects/{projectId}");
        await EnsureSuccessAsync(response);
    }

    public async Task<ProjectDto> SetProjectHiddenAsync(int projectId, bool hidden)
    {
        using var response = await SendAsync(HttpMethod.Post,
            hidden ? $"/api/projects/{projectId}/hide" : $"/api/projects/{projectId}/unhide");
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task<StatusDto> CreateStatusAsync(StatusPayload payload)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/statuses", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<StatusDto>(response);
    }

    public async Task<StatusDto> UpdateStatusAsync(int statusId, StatusPayload payload)
    {
        using var response = await SendAsync(HttpMethod.Put, $"/api/statuses/{statusId}", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<StatusDto>(response);
    }

    public async Task DeleteStatusAsync(int statusId)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/api/statuses/{statusId}");
        await EnsureSuccessAsync(response);
    }

    public async Task<DeviceDto> CreateDeviceAsync(DevicePayload payload)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/devices", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<DeviceDto>(response);
    }

    public async Task<DeviceDto> UpdateDeviceAsync(int deviceId, DevicePayload payload)
    {
        using var response = await SendAsync(HttpMethod.Put, $"/api/devices/{deviceId}", JsonContent.Create(payload, options: JsonOptions));
        await EnsureSuccessAsync(response);
        return await ReadAsync<DeviceDto>(response);
    }

    public async Task DeleteDeviceAsync(int deviceId)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"/api/devices/{deviceId}");
        await EnsureSuccessAsync(response);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, HttpContent? content = null)
    {
        var token = Volatile.Read(ref _apiToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            content?.Dispose();
            throw new ApiException(Strings.ErrorApiTokenRequired);
        }

        using var request = new HttpRequestMessage(method,
            new Uri(new Uri(_serverAddress + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = content;
        return await _http.SendAsync(request);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<T>(JsonOptions)
            ?? throw new ApiException("Server returned an empty response.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new ApiException(Strings.ErrorAuthenticationFailed);
        }

        var body = await response.Content.ReadAsStringAsync();
        var detail = TryReadDetail(body);
        throw new ApiException(
            string.IsNullOrWhiteSpace(detail)
                ? $"Server returned {(int)response.StatusCode} {response.ReasonPhrase}."
                : detail);
    }

    private static string? TryReadDetail(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
            {
                return detail.ValueKind == JsonValueKind.String
                    ? detail.GetString()
                    : detail.ToString();
            }
        }
        catch (JsonException)
        {
            // Fall through to a bounded plain-text message.
        }

        return body.Length <= 300 ? body : body[..300];
    }

    public void Dispose() => _http.Dispose();
}

internal sealed class ApiException(string message) : Exception(message);
