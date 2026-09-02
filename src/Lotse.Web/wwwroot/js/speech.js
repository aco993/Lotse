// Browser speech: TTS for listening tasks, STT for speaking tasks. No keys, no server round-trips.
// Imported as an ES module from Blazor (SpeechService.cs).

let recognition = null;

export function ttsSupported() {
    return typeof window.speechSynthesis !== "undefined";
}

export function sttSupported() {
    return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
}

/** Speaks German text. Resolves when finished (or immediately when TTS is unavailable). */
export function speak(text, rate) {
    return new Promise((resolve) => {
        if (!ttsSupported()) { resolve(false); return; }
        const synth = window.speechSynthesis;
        synth.cancel();
        const utter = new SpeechSynthesisUtterance(text);
        utter.lang = "de-DE";
        utter.rate = rate || 0.95;
        const voices = synth.getVoices().filter(v => v.lang && v.lang.toLowerCase().startsWith("de"));
        // Prefer a natural/online voice when the browser offers one.
        const preferred = voices.find(v => /natural|online|premium|neural/i.test(v.name)) || voices[0];
        if (preferred) utter.voice = preferred;
        utter.onend = () => resolve(true);
        utter.onerror = () => resolve(false);
        synth.speak(utter);
    });
}

export function stopSpeaking() {
    if (ttsSupported()) window.speechSynthesis.cancel();
}

/**
 * Starts continuous German recognition. Interim and final results are pushed to .NET via
 * dotnetRef.invokeMethodAsync("OnTranscript", text, isFinal); the end event via "OnRecognitionEnded".
 */
export function startRecognition(dotnetRef) {
    const Ctor = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!Ctor) return false;
    stopRecognition();
    recognition = new Ctor();
    recognition.lang = "de-DE";
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.maxAlternatives = 1;

    let finalText = "";
    recognition.onresult = (event) => {
        let interim = "";
        for (let i = event.resultIndex; i < event.results.length; i++) {
            const r = event.results[i];
            if (r.isFinal) finalText += r[0].transcript + " ";
            else interim += r[0].transcript;
        }
        dotnetRef.invokeMethodAsync("OnTranscript", (finalText + interim).trim(), false);
    };
    recognition.onerror = (event) => {
        dotnetRef.invokeMethodAsync("OnRecognitionError", event.error || "unknown");
    };
    recognition.onend = () => {
        dotnetRef.invokeMethodAsync("OnTranscript", finalText.trim(), true);
        dotnetRef.invokeMethodAsync("OnRecognitionEnded");
    };
    try {
        recognition.start();
        return true;
    } catch (e) {
        return false;
    }
}

export function stopRecognition() {
    if (recognition) {
        try { recognition.stop(); } catch (e) { /* already stopped */ }
        recognition = null;
    }
}

export function focusElement(id) {
    const el = document.getElementById(id);
    if (el) el.focus();
}
