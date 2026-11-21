// Light or dark: the learner's choice, or the operating system's when there is none.
// The choice lives in a first-party cookie rather than localStorage so that the server can read it too: the account
// pages are static HTML without a circuit, and App.razor puts it on <html data-theme> for them. No expiry games,
// no tracking - one word, "light" or "dark", for a year; "system" deletes it.
const name = "lotse-theme";

export function getChoice() {
    const hit = document.cookie.split("; ").find(c => c.startsWith(name + "="));
    const value = hit ? decodeURIComponent(hit.substring(name.length + 1)) : "";
    return value === "light" || value === "dark" ? value : "system";
}

export function setChoice(choice) {
    const secure = location.protocol === "https:" ? "; Secure" : "";
    if (choice === "light" || choice === "dark") {
        document.cookie = `${name}=${choice}; Path=/; Max-Age=31536000; SameSite=Lax${secure}`;
        document.documentElement.setAttribute("data-theme", choice);
    } else {
        document.cookie = `${name}=; Path=/; Max-Age=0; SameSite=Lax${secure}`;
        document.documentElement.removeAttribute("data-theme");
    }
}

export function prefersDark() {
    return !!(window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches);
}

// Tells .NET when the operating system switches (evening mode, a phone's automatic dark mode), so "system" follows it
// live instead of on the next reload.
let query = null, listener = null;
export function watchSystem(dotnet) {
    if (!window.matchMedia) return;
    unwatchSystem();
    query = window.matchMedia("(prefers-color-scheme: dark)");
    listener = e => dotnet.invokeMethodAsync("OnSystemThemeChanged", e.matches);
    query.addEventListener("change", listener);
}

export function unwatchSystem() {
    if (query && listener) query.removeEventListener("change", listener);
    query = listener = null;
}
