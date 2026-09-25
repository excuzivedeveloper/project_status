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
    // Used as ListBox.DisplayMember wherever projects are listed, so a list shows the project name
    // instead of the type name of the item.
    public const string DisplayMemberProperty = nameof(Name);

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

    // Shared server state, not a local client setting. Missing in payloads from
    // older servers means visible.
    [JsonPropertyName("is_hidden")]
    public bool IsHidden { get; set; }

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

    // Null means "preserve the current flag on the server", so a rename built from
    // name/status/device/note cannot accidentally unhide the project. Omitted from
    // the JSON when null; the server also treats an explicit null as preserve.
    [JsonPropertyName("is_hidden")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsHidden { get; init; }
}

// Pure rules for project visibility, so they can be covered without creating a window:
// the main grid shows visible projects only, Settings shows hidden ones on demand.
internal static class ProjectVisibility
{
    public static IEnumerable<ProjectDto> VisibleOnly(IEnumerable<ProjectDto> projects)
    {
        return projects.Where(project => !project.IsHidden);
    }

    public static IEnumerable<ProjectDto> ForSettings(IEnumerable<ProjectDto> projects, bool showHidden)
    {
        return showHidden ? projects : VisibleOnly(projects);
    }

    public static string DisplayName(ProjectDto project)
    {
        return project.IsHidden ? Strings.ProjectHiddenSuffixFormat(project.Name) : project.Name;
    }

    public static ProjectPayload WithName(ProjectDto current, string name)
    {
        return new ProjectPayload
        {
            Name = name,
            StatusId = current.StatusId,
            DeviceId = current.DeviceId,
            Note = current.Note,
            IsHidden = current.IsHidden
        };
    }
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
