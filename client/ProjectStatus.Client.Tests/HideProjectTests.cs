using System.Text.Json;
using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// Hide is not delete: the flag is shared server state, the main list filters it out,
// Settings keeps it reachable, and a rename must never flip it back.
public class HideProjectTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void A_project_without_the_flag_deserializes_as_visible()
    {
        // Payloads stored before the flag existed have no "is_hidden" member.
        const string legacyJson = """{"id":1,"name":"Legacy","note":"","updated_at":"2026-01-01T00:00:00+00:00"}""";

        var project = JsonSerializer.Deserialize<ProjectDto>(legacyJson, JsonOptions);

        Assert.NotNull(project);
        Assert.False(project.IsHidden);
    }

    [Fact]
    public void The_main_list_keeps_hidden_projects_out()
    {
        var projects = new List<ProjectDto>
        {
            new() { Id = 1, Name = "Visible" },
            new() { Id = 2, Name = "Hidden", IsHidden = true }
        };

        var visible = ProjectVisibility.VisibleOnly(projects).ToList();

        Assert.Single(visible);
        Assert.Equal("Visible", visible[0].Name);
    }

    [Fact]
    public void Settings_hides_hidden_projects_until_requested()
    {
        var projects = new List<ProjectDto>
        {
            new() { Id = 1, Name = "Visible" },
            new() { Id = 2, Name = "Hidden", IsHidden = true }
        };

        Assert.Single(ProjectVisibility.ForSettings(projects, showHidden: false));

        var shown = ProjectVisibility.ForSettings(projects, showHidden: true).ToList();
        Assert.Equal(2, shown.Count);
        Assert.Contains(shown, project => project.IsHidden);
    }

    [Fact]
    public void A_rename_payload_omits_the_flag_so_the_server_preserves_it()
    {
        // A stale snapshot must never flip visibility: even when the locally known
        // project is hidden, the rename carries no flag and the server keeps its own.
        var hidden = new ProjectDto
        {
            Id = 5,
            Name = "Old",
            Note = "kept",
            StatusId = 2,
            DeviceId = 3,
            IsHidden = true
        };
        var visible = new ProjectDto
        {
            Id = 6,
            Name = "Old",
            Note = "kept",
            StatusId = 2,
            DeviceId = 3,
            IsHidden = false
        };

        foreach (var current in new[] { hidden, visible })
        {
            var payload = ProjectVisibility.WithName(current, "New");

            Assert.Equal("New", payload.Name);
            Assert.Equal(2, payload.StatusId);
            Assert.Equal(3, payload.DeviceId);
            Assert.Equal("kept", payload.Note);
            Assert.Null(payload.IsHidden);
            Assert.DoesNotContain(
                "is_hidden",
                JsonSerializer.Serialize(payload, JsonOptions),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void An_ordinary_update_payload_omits_the_flag()
    {
        // Status/device/note edits from the main grid never touch visibility; only the
        // dedicated hide/unhide endpoints change it.
        var payload = new ProjectPayload
        {
            Name = "Example",
            StatusId = 1,
            DeviceId = 2,
            Note = "note"
        };

        Assert.Null(payload.IsHidden);
        Assert.DoesNotContain(
            "is_hidden",
            JsonSerializer.Serialize(payload, JsonOptions),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Hidden_projects_are_marked_in_both_languages()
    {
        var hidden = new ProjectDto { Id = 1, Name = "Demo", IsHidden = true };
        var visible = new ProjectDto { Id = 2, Name = "Demo" };

        try
        {
            Localization.Apply("en");
            Assert.Equal("Demo (hidden)", ProjectVisibility.DisplayName(hidden));
            Assert.Equal("Demo", ProjectVisibility.DisplayName(visible));

            Localization.Apply("ru");
            Assert.Equal("Demo (скрыт)", ProjectVisibility.DisplayName(hidden));
            Assert.Equal("Demo", ProjectVisibility.DisplayName(visible));
        }
        finally
        {
            Localization.Apply(null);
        }
    }

    [Fact]
    public void Unhide_restores_the_project_to_the_main_list()
    {
        var projects = new List<ProjectDto>
        {
            new() { Id = 1, Name = "Back", IsHidden = false }
        };

        Assert.Single(ProjectVisibility.VisibleOnly(projects));
        Assert.Single(ProjectVisibility.ForSettings(projects, showHidden: false));
    }
}
