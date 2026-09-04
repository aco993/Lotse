// Speech for listening and speaking tasks. Two sources, both key-free and both local to this machine:
//   - playAudio(): a WAV rendered by the local Piper engine on the server (neural, near-native German)
//   - speak():     the browser's own voices, used whenever Piper is not installed or fails
// STT stays browser-only. Imported as an ES module from Blazor (SpeechService.cs).

let recognition = null;
let audio = null;
let audioDone = null;

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

/**
 * Plays a WAV rendered on the server. Resolves true when it finished, false on any problem (missing file,
 * decode error, autoplay blocked) so the caller can still fall back to the browser voices.
 */
export function playAudio(url) {
    return new Promise((resolve) => {
        stopSpeaking();
        const el = new Audio(url);
        let settled = false;
        const done = (ok) => {
            if (settled) return;          // ended/error/stop can all arrive; the first one wins
            settled = true;
            el.onended = null;
            el.onerror = null;
            if (audio === el) { audio = null; audioDone = null; }
            resolve(ok);
        };
        audio = el;
        audioDone = done;
        el.onended = () => done(true);
        el.onerror = () => done(false);
        // A rejected play() is the autoplay policy or a missing file - both mean "fall back", not "crash".
        el.play().catch(() => done(false));
    });
}

export function stopSpeaking() {
    if (ttsSupported()) window.speechSynthesis.cancel();
    if (audio) {
        const el = audio, done = audioDone;
        audio = null;
        audioDone = null;
        el.pause();
        // Resolve the pending playAudio() as "did not finish". Without this the awaiting .NET call would hang
        // forever and leave the Vorlesen button disabled for the rest of the circuit.
        if (done) done(false);
    }
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

export function prefersDark() {
    return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
}

export function focusElement(id) {
    const el = document.getElementById(id);
    if (el) el.focus();
}

// Global keyboard shortcuts for the exercise runner (Enter = continue, 1-4 = choose option).
// Ignored while typing in an input/textarea so Enter-to-check keeps working through the field itself.
let keyHandler = null;
export function registerKeys(dotnetRef) {
    unregisterKeys();
    keyHandler = (e) => {
        const tag = (e.target && e.target.tagName) || "";
        const typing = tag === "INPUT" || tag === "TEXTAREA" || (e.target && e.target.isContentEditable);
        if (e.key === "Enter" && !typing) {
            dotnetRef.invokeMethodAsync("OnGlobalKey", "Enter");
        } else if (!typing && /^[1-9]$/.test(e.key)) {
            dotnetRef.invokeMethodAsync("OnGlobalKey", e.key);
        }
    };
    document.addEventListener("keydown", keyHandler);
}
export function unregisterKeys() {
    if (keyHandler) { document.removeEventListener("keydown", keyHandler); keyHandler = null; }
}
