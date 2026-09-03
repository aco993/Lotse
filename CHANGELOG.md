# Changelog

## 0.6.0 – 2026-09-03

- **Course part 2**: lessons 13–24 continue the story into the second year (mentoring Tarek, Kita parents' evening, company outing, customer presentation, team conflict, VHS course, utility-bill dispute, merger rumours, internal application, news listening, exam days Lesen and Schreiben). Every remaining core grammar node now has its own lesson explanation with Serbian contrast (Artikel/Genus, Nebensätze, Verben mit Präposition, Adjektivdeklination, Reflexive Verben, Genitiv, Modalverben subjektiv, Partizipialattribut, Indirekte Rede, Infinitiv mit zu) plus falsche Freunde/Wortbildung and reading/listening strategy lessons. 36 new lesson exercises.
- Course page groups lessons into "Teil 1 · Das erste Jahr" and "Teil 2 · Das zweite Jahr" with per-part progress; works on phone widths.
- Tests: 111 (24 lessons, two parts of twelve, every grammar node explained).

## 0.5.0 – 2026-09-03

- **The course**: 12 lessons telling one story (first year at a Bremen software company: team, first formal mail, stand-up, angry customer, Bürgeramt, flat and neighbours, doctor, production incident, home-office debate, weekend small talk, salary talk, exam day). Each lesson: intro, interactive story dialogue (choose your line, every option explained), grammar explanation with Serbian contrast table and TTS, 10–11 mixed interactive steps, a production task, a Merksatz.
- **Three new exercise types**: interactive dialogue, spot-the-error (tap the wrong word), pair matching. 36 new lesson exercises; planner uses them as drills too.
- Course page with illustrated lesson cards (inline SVG scenes), progress, recommendation of the next lesson; lesson detail page; next-lesson card on Heute; Merksatz on lesson completion; bottom navigation gained "Kurs".
- **EF Core migrations** replace `EnsureCreated`: baseline `InitialCreate` + `AddLessons`; databases created by earlier versions are baselined automatically on start (no data loss). `dotnet-ef` as local tool.
- Tests: 110 (lesson content integrity, dialogue/match scoring, lesson flow, bUnit dialogue component).

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
