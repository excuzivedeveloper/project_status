using System.Net.Http.Json;
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

    public ApiClient(string serverAddress)
    {
        _serverAddress = AppSettings.NormalizeServerAddress(serverAddress);

        _http = new HttpClient(CreateServerHandler())
        {
            Timeout = TimeSpan.FromSeconds(4)
        };
    }

    // Server traffic bypasses the Windows system proxy: a VPN client that sets a
    // system proxy can intercept and stall requests to the private server while
    // direct access works. This handler is used only here; update/internet traffic
    // keeps the normal Windows networking behavior.
    internal static HttpClientHandler CreateServerHandler()
    {
        return new HttpClientHandler
        {
            UseProxy = false
        };
    }

    public void SetServerAddress(string serverAddress)
    {
        _serverAddress = AppSettings.NormalizeServerAddress(serverAddress);
    }

    public async Task<StateSnapshot> GetStateAsync()
    {
        using var response = await _http.GetAsync(BuildUri("/api/state"));
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<StateSnapshot>(JsonOptions)
            ?? new StateSnapshot();
    }

    public async Task<ProjectDto> CreateProjectAsync(ProjectPayload payload)
    {
        using var response = await _http.PostAsJsonAsync(BuildUri("/api/projects"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task<ProjectDto> UpdateProjectAsync(int projectId, ProjectPayload payload)
    {
        using var response = await _http.PutAsJsonAsync(BuildUri($"/api/projects/{projectId}"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task DeleteProjectAsync(int projectId)
    {
        using var response = await _http.DeleteAsync(BuildUri($"/api/projects/{projectId}"));
        await EnsureSuccessAsync(response);
    }

    public async Task<ProjectDto> SetProjectHiddenAsync(int projectId, bool hidden)
    {
        using var response = await _http.PostAsync(
            BuildUri(hidden ? $"/api/projects/{projectId}/hide" : $"/api/projects/{projectId}/unhide"),
            content: null);
        await EnsureSuccessAsync(response);
        return await ReadAsync<ProjectDto>(response);
    }

    public async Task<StatusDto> CreateStatusAsync(StatusPayload payload)
    {
        using var response = await _http.PostAsJsonAsync(BuildUri("/api/statuses"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<StatusDto>(response);
    }

    public async Task<StatusDto> UpdateStatusAsync(int statusId, StatusPayload payload)
    {
        using var response = await _http.PutAsJsonAsync(BuildUri($"/api/statuses/{statusId}"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<StatusDto>(response);
    }

    public async Task DeleteStatusAsync(int statusId)
    {
        using var response = await _http.DeleteAsync(BuildUri($"/api/statuses/{statusId}"));
        await EnsureSuccessAsync(response);
    }

    public async Task<DeviceDto> CreateDeviceAsync(DevicePayload payload)
    {
        using var response = await _http.PostAsJsonAsync(BuildUri("/api/devices"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<DeviceDto>(response);
    }

    public async Task<DeviceDto> UpdateDeviceAsync(int deviceId, DevicePayload payload)
    {
        using var response = await _http.PutAsJsonAsync(BuildUri($"/api/devices/{deviceId}"), payload, JsonOptions);
        await EnsureSuccessAsync(response);
        return await ReadAsync<DeviceDto>(response);
    }

    public async Task DeleteDeviceAsync(int deviceId)
    {
        using var response = await _http.DeleteAsync(BuildUri($"/api/devices/{deviceId}"));
        await EnsureSuccessAsync(response);
    }

    private Uri BuildUri(string path) => new(new Uri(_serverAddress + "/"), path.TrimStart('/'));

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
