# Lotse – adaptive German coach for a real B2

**Lotse** (German for a harbour pilot) is a personal, adaptive German-learning application built to get one specific learner – a Serbian-speaking software developer living in Germany – from an uneven B1–B2 to a solid, exam-ready B2 (Goethe-Zertifikat B2 format; telc B2 transfers).

It is not a course. It is a closed loop: every answer updates a per-topic ability model, every mistake is tagged with an error code, and a planner composes each day's 5–30-minute session from due repetitions, targeted drills on the weakest topics, one production task (writing or speaking) and scheduled re-checks of topics that used to be weak.

> Concept and methodology: [docs/KONZEPT.md](docs/KONZEPT.md) · Roadmap: [docs/PLAN.md](docs/PLAN.md) · Architecture: [docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md)
> User guide (Serbian): [docs/UPUTSTVO.md](docs/UPUTSTVO.md) · Publishing on GitHub: [docs/GITHUB.md](docs/GITHUB.md) · Claude Chat tutor prompt: [docs/CLAUDE_CHAT_PROMPT.md](docs/CLAUDE_CHAT_PROMPT.md)

## Highlights

- **A course with a story.** 24 lessons in two parts follow two years at a Bremen software company. Part 1: the first day in the team, the first formal mail to the boss, a stand-up, an angry customer, the Bürgeramt, the flat and the neighbours, the doctor, a production incident, the home-office debate, weekend small talk, the salary talk, exam day. Part 2: mentoring a new colleague, a Kita parents' evening, organising the company outing, presenting to the customer, a team conflict, an evening course at the VHS, disputing the utility bill, rumours of a merger, an internal application, listening to the news, and the two exam days (Lesen, Schreiben). Each lesson opens with an interactive dialogue in which you choose your own lines and every option is explained, continues with a grammar focus explained through Serbian↔German example pairs (with text-to-speech), and ends with a writing or speaking task.
- **Interactive exercise types.** Dialogues, spot-the-error (tap the wrong word in a colleague's message), pair matching, gap fill, transformation, translation, word order, vocabulary with article, dictation, free writing, speaking.
- **Skill map underneath.** 49 skill nodes (grammar, vocabulary, Redemittel, reading, listening, writing, speaking) on CEFR sub-bands; 55 error codes; contrastive Serbian↔German notes on every interference-prone topic.
- **Adaptive engine, fully deterministic and unit-tested.** Rasch/Elo-style ability per node, SM-2-family spaced repetition with behaviour-derived grades, weak-area analysis with trends, a session planner that explains every choice, a 7/21/60-day re-check cycle for recovered weaknesses, exam readiness per module.
- **Production first.** Typed gap fills, transformations, Serbian→German translation, word order, vocabulary with article, dictation, daily free writing/speaking. Tolerant checking (umlauts, ß, one typo, capitalisation, missing article) that still logs every slip.
- **Works without any API key.** 838 hand-authored exercises, self-check rubrics with model answers, browser speech (TTS/STT).
- **Measured grammar coverage.** A 51-item reference inventory of B2 grammar (`tools/b2-grammar-reference.json`, compiled from Profile Deutsch, the common ground of the standard B2 textbooks, and the Goethe rating criteria) is scored against the content by `tools/grammar-coverage.py`; every *Muss* item now reaches the top grade, every writing and speaking task demands a named structure in its rubric, and tests keep both from falling back. Report: [docs/GRAMMATIK_ABDECKUNG.md](docs/GRAMMATIK_ABDECKUNG.md).
- **Separate accounts, own login, passkeys.** Register/log in (ASP.NET Core Identity, cookie auth, lockout, encrypted-at-rest API key) and everything - skill state, sessions, error journal, Tutor settings - is scoped to that account alone; a second account starts from a blank slate and can never see the first one's data or spend its API key. Every page requires sign-in by default (fallback authorization policy), with the login/registration pages the one deliberate exception. Passkeys (WebAuthn - fingerprint, face, device PIN) work alongside the password, end-to-end tested with Chromium's virtual authenticator.
- **AI tutor (optional), provider-agnostic, configured in the app.** Rubric-based evaluation of writing and speaking with tagged errors that feed the model, on-demand exercise and reading/listening generation, a discussion partner for the oral exam. Pick a provider on the settings page: Groq (free, fast, the default suggestion), Claude via the official Anthropic SDK, OpenRouter, Ollama or LM Studio locally, Mistral, DeepSeek, OpenAI or any OpenAI-compatible server. Connection test, encrypted key storage (ASP.NET Data Protection), switch at runtime, graceful fallback from `json_schema` to `json_object` to plain text, retries with backoff.
- **Self-diagnosis and self-repair.** Health panel (content, database integrity, tutor) with one-click repair, `/health` endpoint, error boundary with friendly recovery, provider errors translated into actionable sentences.
- **Mobile-first details.** Bottom navigation on phones, installable PWA, system dark mode, keyboard shortcuts on desktop (Enter, digits) hidden on touch.
- **Exam realism.** Goethe B2 blueprint as data, writing/speaking tasks in exact exam formats, reading and audio-only listening tasks in exam part formats, readiness report against the 60 % rule.

## Screens

| Heute | Session | Fortschritt |
|---|---|---|
| Streak, minutes, due reviews, readiness; one button starts the planned session; weak areas and exam prognosis. | One card per step with a reason chip ("Wiederholung fällig", "Schwerpunkt Passiv: 3 Fehler / 7 Tage"), immediate feedback with explanation and Serbian contrast. | Mastery per node with confidence, 30-day activity, re-check markers. |

Also: *Schreiben* (micro tasks, exam Teil 1/2), *Sprechen* (spontaneous, Vortrag, discussion with AI partner), *Prüfung B2* (blueprint + simulations), *Fehlerjournal*, *Themen* (focus sessions, generate exercises), *Einstellungen*, *Konto* (register/log in/change password - every other page requires being signed in).

## Quick start

Requirements: .NET 10 SDK. Chrome or Edge for speech recognition (any browser for the rest).

```bash
git clone <this repo> && cd Lotse
dotnet run --project src/Lotse.Web
```

Open http://localhost:5178 (or the port printed in the console), register an account (no email confirmation required - registration signs you in right away), and you're at the dashboard. Data lives in `src/Lotse.Web/data/lotse.db` (SQLite, created on first start).

No mail server is configured on a fresh clone, so "Passwort vergessen?" logs the reset link to the console instead of emailing it - watch the terminal Lotse is running in after requesting one.

### Optional: enable the AI tutor

Open **Einstellungen → KI-Tutor**, pick a provider, paste a key, press *Verbindung testen*, then *Speichern & aktivieren*. The key is stored encrypted on this machine only. Groq offers a free tier (https://console.groq.com/keys) and answers in seconds; Claude gives the best exam-grade feedback; Ollama runs fully offline.

Keys can also come from environment variables (`GROQ_API_KEY`, `ANTHROPIC_API_KEY`, `OPENROUTER_API_KEY`, `OPENAI_API_KEY`, …) or from configuration defaults in `appsettings.json` → `Lotse:Tutor` (`Preset`, `Model`, `BaseUrl`). See `appsettings.Ollama.json` and [docs/UPUTSTVO.md](docs/UPUTSTVO.md) §6 for a provider comparison. Without a tutor every feature except AI evaluation, generation and the discussion partner is available.

### Tests

```bash
dotnet test
```

146 tests: lesson and dialogue integrity, grammar coverage and production pressure against the B2 reference inventory, engine (ability updates, answer checking, scheduler, re-check lifecycle, planner behaviour), content integrity (every exercise valid, every core node covered below and above the B1/B2 boundary, every seed answer accepted by the checker), application service against a temporary SQLite database, migrations vs. model drift, the OpenAI-compatible provider against a scripted HTTP handler (schema fallback, retries, error mapping), tutor settings persistence and encryption, multi-user isolation (two accounts, neither can see the other's skill state, sessions or Tutor API key; a reset empties every table for one account and none for the other), the open-redirect guard and the German Identity messages, bUnit component tests for the exercise flow, and the real host in-process (`WebApplicationFactory`: public static assets, static login page, signed-out redirects, password policy). `dotnet format --verify-no-changes` is clean (EF migrations exempted as generated code).

### End-to-end (Playwright)

```bash
pwsh tests/Lotse.E2E/bin/Debug/net10.0/playwright.ps1 install chromium   # once, after the first build
dotnet test --project tests/Lotse.E2E
```

The real app on Kestrel (`WebApplicationFactory.UseKestrel`, .NET 10) in a real headless Chromium: the learner's day from registration to a resumed session, and the full passkey ceremony via the browser's virtual authenticator. On failure a Playwright trace lands in `tests/Lotse.E2E/bin/.../playwright-traces/` (`playwright show-trace <zip>`). CI runs all of it on every push.

## Stack

| Layer | What | Why |
|---|---|---|
| Runtime | .NET 10 (LTS), C# 14, `global.json`-pinned SDK, central package management, `TreatWarningsAsErrors`, `AnalysisLevel=latest`, `dotnet format` as a gate | One warning-free, formatter-clean build everywhere |
| UI | ASP.NET Core Blazor Web App: InteractiveServer for the app, static SSR for the Identity pages (decided per page from its layout, in `App.razor`); MudBlazor 9 | One process, one language, no API layer for a personal tool; static pages where a real HTTP response is needed |
| Accounts | ASP.NET Core Identity (cookies, lockout, passkeys/WebAuthn via Identity schema v3, German error messages), `ICurrentUserAccessor` seam | Everything per account, Core stays account-free |
| Data | EF Core 10 + SQLite, migrations (`DatabaseInitializer` baselines pre-migration files), Data Protection for the API key at rest | Zero-install persistence; keys never in clear text |
| AI | Anthropic SDK (Claude) and any OpenAI-compatible endpoint (Groq, Ollama, LM Studio, OpenRouter, Mistral, DeepSeek, OpenAI), structured JSON output with graceful fallback | Provider-agnostic, works offline with Ollama |
| Browser | Web Speech API (TTS/STT), PWA manifest, CSS custom properties + `color-mix()` for theme-aware accents | Phone-first, installable, dark mode for free |
| Tests | xUnit v3 on Microsoft.Testing.Platform, bUnit 2, `WebApplicationFactory` in-process (TestServer) and on Kestrel, Playwright 1.62 with CDP virtual authenticator | Engine in ms, components in memory, the host as it ships, the browser as the learner sees it |
| Delivery | GitHub Actions (format → build → tests → E2E, traces on failure), Dependabot (NuGet + actions, weekly, grouped), MIT | Green main, current dependencies |

## Project layout

```
content/                 taxonomy.json (nodes + error codes), exercises/*.json (the bank), lessons/*.json (the course)
src/Lotse.Core           domain model + learning engine, no dependencies
src/Lotse.Infrastructure EF Core (SQLite), content loader, Claude tutor, application service
src/Lotse.Web            Blazor Server UI (MudBlazor), browser speech interop
tests/Lotse.Core.Tests   xUnit v3 (Microsoft.Testing.Platform): engine, content, service, providers, migrations
tests/Lotse.Web.Tests    bUnit component tests + the host in-process (WebApplicationFactory)
tests/Lotse.E2E          Playwright: the real app on Kestrel in a real Chromium (daily loop, passkeys)
docs/                    concept, plan, architecture, grammar-coverage report
tools/                   B2 grammar reference inventory + the script that measures coverage
```

## Status

Phase 0 (foundation) complete; Phase 1 (daily-use hardening) in progress. See [docs/PLAN.md](docs/PLAN.md).

## License

MIT
