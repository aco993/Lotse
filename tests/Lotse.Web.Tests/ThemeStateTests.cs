using Lotse.Web.Services;

namespace Lotse.Web.Tests;

/// <summary>
/// The light/dark choice: remembered in the browser, "system" follows the device live, the bar's button flips what
/// is shown and stores it. JS is bUnit's fake; theme.js itself is a few lines of cookie code.
/// </summary>
public class ThemeStateTests : BunitContext
{
    private readonly BunitJSModuleInterop _module;

    public ThemeStateTests()
    {
        _module = JSInterop.SetupModule("./js/theme.js");
        _module.SetupVoid("watchSystem", _ => true).SetVoidResult();
        _module.SetupVoid("setChoice", _ => true).SetVoidResult();
    }

    private ThemeState Create(string stored, bool deviceDark)
    {
        _module.Setup<string>("getChoice").SetResult(stored);
        _module.Setup<bool>("prefersDark").SetResult(deviceDark);
        return new ThemeState(JSInterop.JSRuntime);
    }

    [Fact]
    public async Task A_stored_choice_wins_over_the_device()
    {
        var theme = Create("light", deviceDark: true);
        await theme.InitializeAsync();
        Assert.Equal(ThemeChoice.Light, theme.Choice);
        Assert.False(theme.IsDark);
    }

    [Fact]
    public async Task Without_a_choice_the_device_decides_and_is_followed_live()
    {
        var theme = Create("system", deviceDark: false);
        await theme.InitializeAsync();
        Assert.False(theme.IsDark);

        var changes = 0;
        theme.Changed += () => changes++;
        theme.OnSystemThemeChanged(true);
        Assert.True(theme.IsDark);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task The_button_stores_the_opposite_of_what_is_shown_and_system_clears_it()
    {
        var theme = Create("system", deviceDark: true);
        await theme.InitializeAsync();
        Assert.True(theme.IsDark);

        await theme.ToggleAsync();
        Assert.Equal(ThemeChoice.Light, theme.Choice);
        Assert.False(theme.IsDark);
        // An explicit choice ignores the device until the learner goes back to "system".
        theme.OnSystemThemeChanged(true);
        Assert.False(theme.IsDark);

        await theme.SetAsync(ThemeChoice.System);
        Assert.True(theme.IsDark);
        Assert.Equal(["light", "system"], _module.Invocations.Where(i => i.Identifier == "setChoice").Select(i => (string)i.Arguments[0]!));
    }

    [Theory]
    [InlineData("dark", ThemeChoice.Dark)]
    [InlineData("light", ThemeChoice.Light)]
    [InlineData("", ThemeChoice.System)]
    [InlineData("purple", ThemeChoice.System)]
    [InlineData(null, ThemeChoice.System)]
    public void Anything_but_light_or_dark_means_follow_the_device(string? stored, ThemeChoice expected)
        => Assert.Equal(expected, ThemeState.Parse(stored));
}
