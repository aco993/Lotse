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

/**
 * Speaks German text. `voice` is "male" or "female" - a preference, honoured as far as the browser's German voices
 * allow. Resolves when finished (or immediately when TTS is unavailable).
 */
export function speak(text, rate, voice) {
    return new Promise((resolve) => {
        if (!ttsSupported()) { resolve(false); return; }
        const synth = window.speechSynthesis;
        synth.cancel();
        const utter = new SpeechSynthesisUtterance(text);
        utter.lang = "de-DE";
        utter.rate = rate || 0.95;
        const chosen = pickVoice(synth.getVoices(), voice);
        if (chosen) utter.voice = chosen;
        utter.onend = () => resolve(true);
        utter.onerror = () => resolve(false);
        synth.speak(utter);
    });
}

// Browser voices carry their gender only as a first name ("Microsoft Katja Online"; "Google Deutsch" carries none).
// A listening text with a moderator and a guest needs two voices that differ, so when the gender cannot be read
// the two requests still land on two different German voices wherever the browser has more than one.
const FEMALE_NAMES = /katja|hedda|anna|petra|marlene|vicki|elke|helena|amala|seraphina|louisa|ingrid|gisela/i;
const MALE_NAMES = /stefan|conrad|klaus|michael|bernd|christoph|killian|kasper|hans|ralf|florian|markus|yannick/i;
const natural = (list) => list.find(v => /natural|online|premium|neural/i.test(v.name)) || list[0];

function pickVoice(all, voice) {
    const german = all.filter(v => v.lang && v.lang.toLowerCase().startsWith("de"));
    if (german.length === 0) return null;
    const wanted = voice === "female" ? FEMALE_NAMES : MALE_NAMES;
    const other = voice === "female" ? MALE_NAMES : FEMALE_NAMES;
    const matching = german.filter(v => wanted.test(v.name));
    if (matching.length) return natural(matching);
    const pool = german.filter(v => !other.test(v.name));   // never answer "female" with a voice called Stefan
    const candidates = pool.length ? pool : german;
    if (candidates.length === 1 || voice !== "female") return natural(candidates);
    const first = natural(candidates);
    return candidates.find(v => v !== first) || first;
}

/**
 * Plays a WAV rendered on the server. Resolves with one of three outcomes, which the caller must tell apart:
 *   "ended"   - played to the end
 *   "failed"  - could not play (missing file, decode error, autoplay blocked) -> caller may use browser voices
 *   "stopped" - deliberately interrupted -> caller must stay silent, NOT fall back and start talking again
 */
export function playAudio(url) {
    return new Promise((resolve) => {
        stopSpeaking();
        const el = new Audio(url);
        let settled = false;
        const done = (outcome) => {
            if (settled) return;          // ended/error/stop can all arrive; the first one wins
            settled = true;
            el.onended = null;
            el.onerror = null;
            if (audio === el) { audio = null; audioDone = null; }
            resolve(outcome);
        };
        audio = el;
        audioDone = done;
        el.onended = () => done("ended");
        el.onerror = () => done("failed");
        el.play().catch(() => done("failed"));
    });
}

export function stopSpeaking() {
    if (ttsSupported()) window.speechSynthesis.cancel();
    if (audio) {
        const el = audio, done = audioDone;
        audio = null;
        audioDone = null;
        el.pause();
        // Resolve the pending playAudio(). Without this the awaiting .NET call would hang forever and leave the
        // Vorlesen button disabled for the rest of the circuit.
        if (done) done("stopped");
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

    // Chrome ends a "continuous" session after a few seconds of silence (error "no-speech", then "end") and after
    // roughly a minute regardless. For a 60-120 s speaking task that meant: the learner paused to think, the
    // button silently fell back to "Aufnahme starten", and the rest of the answer was never heard. So the session
    // is restarted from onend unless the learner pressed stop or a real error happened; the transcript so far is
    // kept across restarts. The restart cap only guards against a browser that ends every session immediately.
    let finalText = "";
    let fatal = false;
    let restarts = 0;
    const current = recognition;
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
        const error = event.error || "unknown";
        if (error === "no-speech") return;       // a pause, not a fault: onend restarts
        fatal = true;
        dotnetRef.invokeMethodAsync("OnRecognitionError", error);
    };
    recognition.onend = () => {
        const stoppedByUser = recognition !== current; // stopRecognition() nulls or replaces the reference
        if (!stoppedByUser && !fatal && restarts < 30) {
            restarts++;
            try { current.start(); return; } catch (e) { /* fall through and report the end */ }
        }
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
        const r = recognition;
        recognition = null;           // onend sees the reference gone and does not restart
        try { r.stop(); } catch (e) { /* already stopped */ }
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
