using Lotse.Infrastructure.Services;
using Microsoft.JSInterop;

namespace Lotse.Web.Components.Shared;

/// <summary>
/// Speech for the UI (see wwwroot/js/speech.js). Scoped per circuit; the JS module is imported lazily on first use
/// and disposed with the circuit.
///
/// Output has two sources: the local Piper engine when it is installed (neural, near-native German) and the
/// browser's own voices otherwise. Input (recognition) is browser-only.
/// </summary>
public sealed class SpeechService(IJSRuntime js, ITextToSpeech tts) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<SpeechService>? _self;

    public event Action<string, bool>? Transcript;
    public event Action<string>? RecognitionError;
    public event Action? RecognitionEnded;

    private async ValueTask<IJSObjectReference> ModuleAsync()
        => _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/speech.js");

    public async ValueTask<bool> TtsSupportedAsync() => await (await ModuleAsync()).InvokeAsync<bool>("ttsSupported");
    public async ValueTask<bool> SttSupportedAsync() => await (await ModuleAsync()).InvokeAsync<bool>("sttSupported");

    /// <summary>
    /// Speaks German text and completes when playback finished. Prefers the locally rendered Piper voice and falls
    /// back to the browser voices whenever rendering or playback did not work out - every failure path here is a
    /// fallback, never an exception, because a hiccup in the audio must not interrupt an exercise.
    /// </summary>
    public async ValueTask<bool> SpeakAsync(string text, double rate = 0.95)
    {
        if (tts.Available)
        {
            string? key = null;
            try { key = await tts.SynthesizeAsync(text, rate); }
            catch (OperationCanceledException) { /* circuit or request gone; fall back */ }

            if (key is not null && await (await ModuleAsync()).InvokeAsync<bool>("playAudio", $"/api/tts/{key}.wav"))
                return true;
        }

        return await (await ModuleAsync()).InvokeAsync<bool>("speak", text, rate);
    }

    public async ValueTask StopSpeakingAsync() => await (await ModuleAsync()).InvokeVoidAsync("stopSpeaking");

    public async ValueTask<bool> StartRecognitionAsync()
    {
        _self ??= DotNetObjectReference.Create(this);
        return await (await ModuleAsync()).InvokeAsync<bool>("startRecognition", _self);
    }

    public async ValueTask StopRecognitionAsync() => await (await ModuleAsync()).InvokeVoidAsync("stopRecognition");

    public async ValueTask FocusAsync(string elementId) => await (await ModuleAsync()).InvokeVoidAsync("focusElement", elementId);

    public async ValueTask<bool> PrefersDarkAsync() => await (await ModuleAsync()).InvokeAsync<bool>("prefersDark");

    public event Action<string>? GlobalKey;

    /// <summary>Document-level shortcuts (Enter, digits) outside of text fields.</summary>
    public async ValueTask RegisterKeysAsync()
    {
        _self ??= DotNetObjectReference.Create(this);
        await (await ModuleAsync()).InvokeVoidAsync("registerKeys", _self);
    }

    public async ValueTask UnregisterKeysAsync()
    {
        if (_module is not null) await _module.InvokeVoidAsync("unregisterKeys");
    }

    [JSInvokable] public void OnGlobalKey(string key) => GlobalKey?.Invoke(key);

    [JSInvokable] public void OnTranscript(string text, bool isFinal) => Transcript?.Invoke(text, isFinal);
    [JSInvokable] public void OnRecognitionError(string error) => RecognitionError?.Invoke(error);
    [JSInvokable] public void OnRecognitionEnded() => RecognitionEnded?.Invoke();

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("stopRecognition"); await _module.InvokeVoidAsync("unregisterKeys"); await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { /* circuit gone */ }
        }
    }
}
