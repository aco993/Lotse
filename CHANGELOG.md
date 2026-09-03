# Changelog

## 0.7.0 – 2026-09-03

- **Login, registration, separate accounts.** ASP.NET Core Identity (cookie auth, no external providers, no 2FA) replaces the fixed `LearnerProfile.Id = 1` single-user model: `LotseDbContext` is now `IdentityDbContext<ApplicationUser>`, and every learning table (`SkillState`, `ReviewState`, sessions, attempts, error events, productions, Tutor settings incl. the encrypted API key) is scoped to the signed-in account. Every page requires sign-in by default (`AddAuthorization` fallback policy); `Account/Login`, `Account/Register`, `Account/ForgotPassword` and `Account/ResetPassword` are the deliberate exceptions.
- `ICurrentUserAccessor` is the one new seam: a plain interface in `Lotse.Infrastructure` (kept free of any ASP.NET Core Identity dependency), implemented in `Lotse.Web` against `AuthenticationStateProvider`, registered Scoped - one instance per Blazor Server circuit, i.e. per signed-in learner. `ILearningService`'s ~30-method public surface, and every UI call site, is unchanged; only its implementation gained a `.Where(UserId == …)` per query. `TutorRegistry` additionally re-validates the signed-in learner before every provider call (`EnsureCurrentAsync`), not just once at circuit start - a Blazor Server circuit can in practice outlive a logout/login round-trip, and a cached `ITutor` built from the previous account's settings must never serve the next one.
- `GeneratedExerciseEntity` is the one table left global, shared across every account - `ContentCatalogProvider` is a process-wide singleton merging one exercise catalog for everyone; making it per-user would be a materially larger change than "add accounts." Documented as a conscious trade-off in `docs/ARCHITEKTUR.md`.
- **"Alle Lerndaten löschen" now deletes only the signed-in account.** It used to drop and recreate the whole SQLite file - correct for one learner, unacceptable once a second account exists. Rewritten as scoped `ExecuteDeleteAsync` calls per table.
- No email sender is configured: `IdentityNoOpEmailSender` logs the confirmation/reset-password link to the console instead (same fallback the official template uses without SMTP), and `RequireConfirmedAccount = false` so registration signs the learner in immediately. The reset-password flow is fully wired and tested end to end regardless (link read from the server log).
- The hardest part of this change was render-mode placement, not the schema: Account pages need genuine static SSR (setting the auth cookie needs a real HTTP response, which a live Blazor Server circuit no longer has once connected) inside an otherwise fully interactive app. `@rendermode` is applied exactly once, at `<Routes>` in `App.razor`, computed per-request from the path - not on individual pages, not on `MainLayout`, not on `AuthorizeRouteView`, all three of which were tried and each failed the same way (ASP.NET Core refuses to serialize the `RenderFragment` a lower component receives from its own parent across a freshly-declared render-mode boundary). See `App.razor`'s own comment.
- Tests: 120. New `MultiUserIsolationTests` registers two accounts, has each save different Tutor settings and answer different exercises, and asserts neither can see the other's skill state, sessions, or (critically) Tutor API key.

## 0.6.1 – 2026-09-03

- **Grammar coverage is now measured, not assumed.** `tools/b2-grammar-reference.json` holds a 51-item reference inventory of B2 grammar (compiled from Profile Deutsch, the common ground of Aspekte neu B2 / Sicher! B2 / Erkundungen B2, and the Goethe rating criteria for Korrektheit and Repertoire), each item marked Muss/Soll/Kann. `tools/grammar-coverage.py` scores every item against taxonomy, exercise bank and lessons and writes [docs/GRAMMATIK_ABDECKUNG.md](docs/GRAMMATIK_ABDECKUNG.md) plus a machine-readable JSON.
- **Gaps found and closed.** Before: 84.3 % of the inventory at grade ≥ 2, 69.2 % of the Muss items at grade 3, weighted score 82.5 %. Missing entirely were irrealer Vergleich/Wunsch (als ob), the correlate „es“, and ß/ss–umlaut spelling; barely present were the Perfekt of modal verbs (double infinitive), Zustandspassiv, Mittelfeld/TeKaMoLo, Futur II, Passiversatz and compounds. 89 new exercises in `content/exercises/grammatik-d.json`, ten lessons gained the missing explanation in their grammar block. After: 100 % / 100 % / 100 %.
- **Production tasks now demand the structures, not just the content.** Every one of the 60 writing and speaking tasks gained a named structural requirement in its rubric, matched to the situation — Genitiv in the contract cancellation, indirect speech when reporting studies, Passiversatz in the status mail to the customer, Teilnegation in the complaint. All 18 tracked structures are demanded by at least three tasks (before: relative clauses, Genitiv, indirect speech and Passiversatz by none at all). Section 6 of the report tracks this separately from drill coverage, because drilling a structure and being forced to produce it are two different things.
- New skill node `GR.N_DEKLINATION` with error code and seven drills — the classic „mit dem Kollege“ trap, which no node covered before (49 nodes, 55 error codes, 838 bank exercises).
- Tests: 115. `GrammarCoverageTests` reads the same reference inventory and fails if any Muss item drops below four exercises, if a tracked structure is demanded by fewer than three production rubrics, or if a production task names no structure at all.

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
