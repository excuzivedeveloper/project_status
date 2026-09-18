using System.Drawing;
using ProjectStatus.Client;
using Xunit;

namespace ProjectStatus.Client.Tests;

// Appearance rules and the window placement model. Both are pure logic; no window is created.
public class AppearanceSettingsTests
{
    [Theory]
    [InlineData(0, 70)]
    [InlineData(69, 70)]
    [InlineData(70, 70)]
    [InlineData(85, 85)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public void NormalizeOpacityPercent_keeps_the_supported_range(int input, int expected)
    {
        Assert.Equal(expected, AppearanceSettings.NormalizeOpacityPercent(input));
    }

    [Theory]
    [InlineData(70, 0.70)]
    [InlineData(85, 0.85)]
    [InlineData(100, 1.00)]
    [InlineData(10, 0.70)]
    public void ToOpacity_converts_percent_to_the_window_opacity(int percent, double expected)
    {
        Assert.Equal(expected, AppearanceSettings.ToOpacity(percent), 3);
    }

    [Fact]
    public void ParseBackgroundColor_treats_empty_as_the_default_appearance()
    {
        Assert.Equal(Color.Empty, AppearanceSettings.ParseBackgroundColor(string.Empty));
        Assert.Equal(Color.Empty, AppearanceSettings.ParseBackgroundColor(null));
        Assert.Equal(Color.Empty, AppearanceSettings.ParseBackgroundColor("   "));
    }

    [Fact]
    public void ParseBackgroundColor_reads_the_stored_hex_format()
    {
        Assert.Equal(Color.FromArgb(0x12, 0x34, 0x56), AppearanceSettings.ParseBackgroundColor("#123456"));
    }

    [Theory]
    [InlineData("not-a-colour")]
    [InlineData("#GGGGGG")]
    [InlineData("rgb(1,2,3)")]
    public void ParseBackgroundColor_falls_back_to_the_default_for_malformed_values(string value)
    {
        Assert.Equal(Color.Empty, AppearanceSettings.ParseBackgroundColor(value));
    }

    [Fact]
    public void ToHex_round_trips_through_ParseBackgroundColor()
    {
        var original = Color.FromArgb(0x0A, 0x1B, 0x2C);

        Assert.Equal(original, AppearanceSettings.ParseBackgroundColor(AppearanceSettings.ToHex(original)));
    }
}

// The compact layout has its own geometry, and after a monitor change the old coordinates have to be
// replaced by a spot the user can actually reach.
public class WindowBoundsTests
{
    private static readonly Rectangle Primary = new(0, 0, 1920, 1040);
    private static readonly Size Minimum = new(200, 120);
    private static readonly IReadOnlyList<Rectangle> Screens = new List<Rectangle> { Primary };

    [Fact]
    public void Saved_bounds_are_used_when_still_on_a_screen()
    {
        var bounds = WindowBounds.Resolve(120, 240, 260, 300, Minimum, Primary, Screens);

        Assert.Equal(new Rectangle(120, 240, 260, 300), bounds);
    }

    [Fact]
    public void Compact_and_full_geometry_stay_independent()
    {
        var compact = WindowBounds.Resolve(40, 50, 240, 300, Minimum, Primary, Screens);
        var full = WindowBounds.Resolve(700, 120, 760, 360, new Size(620, 260), Primary, Screens);

        Assert.Equal(new Rectangle(40, 50, 240, 300), compact);
        Assert.Equal(new Rectangle(700, 120, 760, 360), full);
        Assert.NotEqual(compact.Location, full.Location);
    }

    [Fact]
    public void Sizes_below_the_minimum_are_grown_to_it()
    {
        var bounds = WindowBounds.Resolve(null, null, 100, 40, Minimum, Primary, Screens);

        Assert.True(bounds.Width >= Minimum.Width);
        Assert.True(bounds.Height >= Minimum.Height);
    }

    [Fact]
    public void A_window_larger_than_the_screen_is_clamped_to_it()
    {
        var bounds = WindowBounds.Resolve(null, null, 4000, 3000, Minimum, Primary, Screens);

        Assert.Equal(Primary.Width, bounds.Width);
        Assert.Equal(Primary.Height, bounds.Height);
        Assert.True(Primary.Contains(bounds));
    }

    [Fact]
    public void Off_screen_coordinates_fall_back_to_the_top_right_corner()
    {
        var bounds = WindowBounds.Resolve(9000, 9000, 260, 300, Minimum, Primary, Screens);

        Assert.Equal(Primary.Right - 260 - 24, bounds.X);
        Assert.Equal(Primary.Top + 24, bounds.Y);
        Assert.True(Primary.Contains(bounds));
    }

    [Fact]
    public void Missing_coordinates_fall_back_to_the_top_right_corner()
    {
        var noX = WindowBounds.Resolve(null, 40, 260, 300, Minimum, Primary, Screens);
        var noY = WindowBounds.Resolve(40, null, 260, 300, Minimum, Primary, Screens);

        Assert.Equal(Primary.Right - 260 - 24, noX.X);
        Assert.Equal(Primary.Right - 260 - 24, noY.X);
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(1915)]
    [InlineData(1919)]
    public void A_window_that_only_peeks_onto_a_screen_is_pulled_fully_inside(int savedX)
    {
        var bounds = WindowBounds.Resolve(savedX, 100, 260, 300, Minimum, Primary, Screens);

        Assert.Equal(260, bounds.Width);
        Assert.Equal(300, bounds.Height);
        Assert.True(Primary.Contains(bounds), $"{bounds} is not inside {Primary}");
    }

    [Fact]
    public void A_few_intersecting_pixels_are_not_enough_to_keep_the_saved_position()
    {
        // Ten pixels of the window overlap the primary working area; keeping the saved rectangle
        // would leave nearly all of it off-screen.
        var bounds = WindowBounds.Resolve(-250, 100, 260, 300, Minimum, Primary, Screens);

        Assert.True(Primary.Contains(bounds), $"{bounds} is not inside {Primary}");
    }

    [Fact]
    public void A_saved_size_larger_than_the_screen_is_clamped_to_it()
    {
        var bounds = WindowBounds.Resolve(100, 100, 4000, 3000, Minimum, Primary, Screens);

        Assert.Equal(Primary.Width, bounds.Width);
        Assert.Equal(Primary.Height, bounds.Height);
        Assert.True(Primary.Contains(bounds), $"{bounds} is not inside {Primary}");
    }

    [Fact]
    public void A_window_saved_on_a_removed_monitor_falls_back_to_the_primary_corner()
    {
        var bounds = WindowBounds.Resolve(2200, 300, 260, 300, Minimum, Primary, Screens);

        Assert.Equal(Primary.Right - 260 - 24, bounds.X);
        Assert.Equal(Primary.Top + 24, bounds.Y);
    }

    [Fact]
    public void A_left_hand_monitor_with_negative_coordinates_is_supported()
    {
        var leftHand = new Rectangle(-1920, 0, 1920, 1040);
        var screens = new List<Rectangle> { Primary, leftHand };

        var onScreen = WindowBounds.Resolve(-1700, 200, 260, 300, Minimum, Primary, screens);
        Assert.Equal(new Rectangle(-1700, 200, 260, 300), onScreen);

        var partlyOffScreen = WindowBounds.Resolve(-1900, -50, 260, 300, Minimum, Primary, screens);
        Assert.Equal(-1900, partlyOffScreen.X);
        Assert.Equal(leftHand.Top, partlyOffScreen.Y);
        Assert.True(leftHand.Contains(partlyOffScreen), $"{partlyOffScreen} is not inside {leftHand}");
    }

    [Fact]
    public void Bounds_on_a_secondary_screen_are_kept()
    {
        var secondary = new Rectangle(1920, 0, 1920, 1040);
        var screens = new List<Rectangle> { Primary, secondary };

        var bounds = WindowBounds.Resolve(2200, 300, 260, 300, Minimum, Primary, screens);

        Assert.Equal(new Rectangle(2200, 300, 260, 300), bounds);
    }
}
