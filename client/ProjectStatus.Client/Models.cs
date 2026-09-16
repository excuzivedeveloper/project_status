using System.Text.Json.Serialization;

namespace ProjectStatus.Client;

internal sealed class StateSnapshot
{
    [JsonPropertyName("projects")]
    public List<ProjectDto> Projects { get; set; } = [];

    [JsonPropertyName("statuses")]
    public List<StatusDto> Statuses { get; set; } = [];

    [JsonPropertyName("devices")]
    public List<DeviceDto> Devices { get; set; } = [];
}

internal sealed class ProjectDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("status_id")]
    public int? StatusId { get; set; }

    [JsonPropertyName("status_name")]
    public string? StatusName { get; set; }

    [JsonPropertyName("status_color")]
    public string? StatusColor { get; set; }

    [JsonPropertyName("device_id")]
    public int? DeviceId { get; set; }

    [JsonPropertyName("device_name")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = string.Empty;
}

internal sealed class StatusDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = "#808080";

    public override string ToString() => Name;
}

internal sealed class DeviceDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    public override string ToString() => Name;
}

internal sealed class ProjectPayload
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("status_id")]
    public int? StatusId { get; init; }

    [JsonPropertyName("device_id")]
    public int? DeviceId { get; init; }

    [JsonPropertyName("note")]
    public string Note { get; init; } = string.Empty;
}

internal sealed class StatusPayload
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; init; } = "#808080";
}

internal sealed class DevicePayload
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
