using Microsoft.JSInterop;

namespace Lotse.Web.Services;

/// <summary>Light, dark, or whatever the device says.</summary>
public enum ThemeChoice { System, Light, Dark }

/// <summary>
/// The learner's light/dark choice for this circuit (see wwwroot/js/theme.js).
///
/// Before this, the moon button in the bar flipped a field in <c>MainLayout</c>: the next reload forgot it, the account
/// pages never heard of it, and once flipped there was no way back to "follow the device". The choice is now a cookie
/// the browser keeps, the account pages read it on the server (<c>App.razor</c>), and "system" is a real option that
/// follows the operating system live, also when it switches to dark mode in the evening.
/// </summary>
public sealed class ThemeState(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<ThemeState>? _self;
    private bool _systemDark;

    public ThemeChoice Choice { get; private set; }

    /// <summary>What the app shows now.</summary>
    public bool IsDark => Choice == ThemeChoice.Dark || (Choice == ThemeChoice.System && _systemDark);

    /// <summary>Raised when <see cref="IsDark"/> or <see cref="Choice"/> may have changed; subscribers re-render.</summary>
    public event Action? Changed;

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/theme.js");

    /// <summary>Reads the stored choice and the device's preference. Safe to call more than once.</summary>
    public async Task InitializeAsync()
    {
        var module = await ModuleAsync();
        Choice = Parse(await module.InvokeAsync<string>("getChoice"));
        _systemDark = await module.InvokeAsync<bool>("prefersDark");
        _self ??= DotNetObjectReference.Create(this);
        await module.InvokeVoidAsync("watchSystem", _self);
    }

    public async Task SetAsync(ThemeChoice choice)
    {
        Choice = choice;
        await (await ModuleAsync()).InvokeVoidAsync("setChoice", choice.ToString().ToLowerInvariant());
        Changed?.Invoke();
    }

    /// <summary>The bar's button: the opposite of what is shown, remembered as an explicit choice.</summary>
    public Task ToggleAsync() => SetAsync(IsDark ? ThemeChoice.Light : ThemeChoice.Dark);

    [JSInvokable]
    public void OnSystemThemeChanged(bool dark)
    {
        _systemDark = dark;
        if (Choice == ThemeChoice.System) Changed?.Invoke();
    }

    public static ThemeChoice Parse(string? value) => value switch
    {
        "light" => ThemeChoice.Light,
        "dark" => ThemeChoice.Dark,
        _ => ThemeChoice.System,
    };

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("unwatchSystem"); await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* circuit gone */ }
        }
        _self?.Dispose();
    }
}
