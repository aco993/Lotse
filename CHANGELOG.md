# Changelog

## 0.4.0 – 2026-09-03

- In-app tutor configuration: provider presets (Groq recommended as free default, Claude, OpenRouter, Ollama, LM Studio, Mistral, DeepSeek, OpenAI, custom), model suggestions, connection test with latency, API key encrypted at rest with ASP.NET Data Protection, runtime switch without restart (`TutorRegistry`).
- Self-diagnosis and self-repair: health panel in settings (content, database integrity, tutor) with one-click repair (schema, VACUUM, orphaned sessions, invalid generated items), `/health` endpoint, error boundary with friendly recovery instead of a crashed circuit, retries with backoff for transient provider errors, plain-language error translation.
- Mobile: bottom navigation, PWA manifest (installable), system dark mode on first load, keyboard hints hidden on touch devices.
- Architecture: `ILearningService` interface for testability; `TutorBase` shared prompt/schema layer.
- Tests: 100 (bUnit component tests for exercise flow and feedback, scripted-HTTP tests for the OpenAI-compatible provider incl. schema fallback and retries, registry persistence/encryption tests). A culture bug (de-DE decimal comma in CSS opacity) was caught by a component test.

## 0.3.0 – 2026-09-03

- Tutor is provider-agnostic: `TutorBase` holds prompts, schemas and mapping; `ClaudeTutor` (Anthropic SDK) and `OpenAiCompatibleTutor` (Ollama, LM Studio, OpenRouter, Groq, Mistral, DeepSeek, OpenAI) implement the transport. Configure with `Lotse:Tutor:Provider`, `BaseUrl`, `Model`.
- Verified end to end against a local Ollama model.
- Service uses the injected clock everywhere (a streak test caught one system-clock call).
- 78 tests.

## 0.2.0 – 2026-09-02

- Content bank grown to 749 exercises: thin grammar topics filled, exam-format reading (Teil 1–5) and listening (Teil 1–4) tasks, more writing and speaking tasks, exam vocabulary.
- Tutor can generate reading/listening tasks and top up the three weakest topics in one click.
- Keyboard flow: Enter continues after feedback, digits pick multiple-choice options.
- Placement test interleaves topics (easy pass, then harder pass).
- Session summary lists the error codes made in that session; dashboard counts down to the exam date.
- Navigation drawer starts closed on phones; tables scroll on narrow screens.
- LAN launch profile for using the app from a phone.
- 74 tests including service integration tests against a temporary SQLite database.
- MIT license, GitHub Actions CI, .editorconfig.

## 0.1.0 – 2026-09-02

- Foundation: skill taxonomy, error catalogue, 485 exercises, learning engine, Blazor Server UI, optional Claude tutor, 46 tests.
