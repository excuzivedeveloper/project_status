using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// The Projects tab used to show "ProjectStatus.Client.ProjectDto" because the list fell back to the
// type name of the item. The check reads the same text the control renders for the selected row.
public class ProjectListDisplayTests
{
    [Fact]
    public void The_projects_list_shows_the_project_name_instead_of_the_type_name()
    {
        var project = new ProjectDto { Id = 3, Name = "Status tracker", Note = "one line" };

        var list = new ListBox { DisplayMember = ProjectDto.DisplayMemberProperty };
        list.Items.Add(project);
        list.SelectedIndex = 0;

        Assert.Equal("Status tracker", list.Text);
        Assert.DoesNotContain(nameof(ProjectDto), list.Text);
    }

    [Fact]
    public void The_list_still_hands_back_the_project_itself_for_add_rename_and_delete()
    {
        var project = new ProjectDto { Id = 7, Name = "Status tracker" };

        var list = new ListBox { DisplayMember = ProjectDto.DisplayMemberProperty };
        list.Items.Add(project);
        list.SelectedIndex = 0;

        Assert.Same(project, list.SelectedItem);
        Assert.IsType<ProjectDto>(list.SelectedItem);
    }

    [Fact]
    public void Without_the_display_member_the_type_name_is_what_a_list_shows()
    {
        // Documents the defect that was fixed: this is the text the Projects tab showed before.
        var project = new ProjectDto { Id = 1, Name = "Status tracker" };

        var list = new ListBox();
        list.Items.Add(project);
        list.SelectedIndex = 0;

        Assert.Contains(nameof(ProjectDto), list.Text);
    }
}
