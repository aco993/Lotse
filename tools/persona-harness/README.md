# Persona harness

A thin Playwright layer for driving the running app the way a person would - register, click, type, read,
screenshot - so that a scripted "persona" (a nurse on her phone, a keyboard-only user, a DaF teacher) can
test Lotse end to end and report back. It is what produced `docs/NUTZERTEST_2026-09.md`.

```bash
npm install                      # once; reuses the Chromium that the .NET Playwright package installed
node run.mjs verify-fixes.mjs --out verify          # against http://localhost:5310 (LOTSE_URL overrides)
node run.mjs my-script.mjs --mobile --dark --state me.json
```

A script exports `default async (h) => { ... }` and gets `h.register`, `h.login`, `h.goto`, `h.text()`,
`h.aria()`, `h.click(name)`, `h.fill`, `h.shot(name)` and the raw Playwright `h.page`. `--state` keeps the cookies
between scripts, `--mobile` is 375×812 with touch, `--dark` sets `prefers-color-scheme`.

Audio is muted twice (`--mute-audio` and a stubbed `speechSynthesis`): headless Chromium still speaks through
the machine's speakers otherwise, and the app reads dictations aloud.

`validate-vocab.mjs <file.json>` checks a vocabulary pack against the content contract and the existing bank
(unique ids and lemmas, known node, band, article/plural/example rules) before it goes into `content/exercises/`.
