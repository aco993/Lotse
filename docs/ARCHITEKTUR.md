# Lotse – Architecture

## Layers

```
┌──────────────────────────────────────────────────────────────────┐
│ Lotse.Web  (Blazor Server, MudBlazor)                            │
│   Account/*: Login (Passwort oder Passkey) · Register ·          │
│              ForgotPassword · ResetPassword · Manage (Passwort,  │
│              Passkeys) - ASP.NET Core Identity, static SSR       │
│   Pages: Heute · Session · Schreiben · Sprechen · Prüfung ·      │
│          Fortschritt · Fehler · Themen · Einstellungen           │
│          (all behind the login, InteractiveServer)               │
│   Components: ExerciseRunner → Closed / Reading / Production     │
│   SpeechService (JS interop: Web Speech API TTS + STT)           │
│   WebCurrentUserAccessor : ICurrentUserAccessor                  │
├──────────────────────────────────────────────────────────────────┤
│ Lotse.Infrastructure                                             │
│   LearningService  (application service, the only DB+engine     │
│                     coordinator; short-lived DbContexts;         │
│                     every query/insert scoped by ICurrentUser-   │
│                     Accessor's UserId)                           │
│   ContentLoader / ContentCatalogProvider (seed + generated,      │
│                     generated exercises are shared across all    │
│                     accounts - see "Multi-user accounts" below)  │
│   LotseDbContext : IdentityDbContext<ApplicationUser> (EF Core,  │
│                     SQLite)                                      │
│   TutorRegistry : ITutor  (per-account settings + API key)       │
│   ClaudeTutor / OpenAiCompatibleTutor (transport)                │
├──────────────────────────────────────────────────────────────────┤
│ Lotse.Core  (no dependencies)                                    │
│   Model:  SkillNode, ErrorType, Exercise, SkillState,            │
│           ReviewState, Attempt, ErrorEvent, ContentCatalog       │
│   Engine: Ability, AnswerChecker, ReviewScheduler,               │
│           SessionPlanner, LearnerAnalysis, PlacementTest         │
│   Exam:   ExamBlueprint (Goethe B2)                              │
│   Tutor:  ITutor, NullTutor, evaluation records                  │
└──────────────────────────────────────────────────────────────────┘
```

Dependencies point downwards only. The engine has no reference to EF Core, Blazor or the Anthropic SDK, which is what makes it unit-testable in milliseconds and reusable behind another host. `ICurrentUserAccessor` is the one seam through which "which account is this?" enters Infrastructure: a plain interface there, implemented in Web against `AuthenticationStateProvider`, so Infrastructure's only Identity dependency is the store itself (`IdentityDbContext<ApplicationUser>` and `ApplicationUser : IdentityUser` in `LotseDbContext.cs`) - no cookie, claims or sign-in types. `ILearnerBound` is its companion for Scoped services that cache per-learner state (`TutorRegistry`): the cache is filled once, before the first render, in `MainLayout.OnInitializedAsync`, and re-checked cheaply before every provider call.

Databases from before 0.7.0 (single-user, no `UserId` columns) are migrated structurally but their rows are not assigned to any account - they belong to the empty owner `""` and stay invisible. The author started fresh; anyone else upgrading a real pre-0.7.0 database would run a one-off `UPDATE <table> SET UserId = '<their AspNetUsers.Id>' WHERE UserId = ''` over the nine scoped tables.

## The daily loop

```
Heute ──start──▶ LearningService.StartSessionAsync(minutes)
                   │  builds PlannerInput from DB (skill states, due reviews,
                   │  recent ids, 30-day errors, last production/input)
                   ▼
                 SessionPlanner.Plan  ──▶ SessionEntity(PlanJson = steps + reasons)
                   │
Session page ◀─────┘  renders step i via ExerciseRunner
                   │
   answer ──▶ SubmitAnswerAsync
                • AnswerChecker.Check          → Outcome, slips
                • Attempt + ErrorEvents        → DB
                • LearnerAnalysis.ApplyAttempt → SkillState θ, re-check lifecycle
                • ReviewScheduler.Apply        → ReviewState due date
                • MarkStepDone                 → session progress
   production ──▶ SubmitProductionAsync
                • ITutor.EvaluateAsync (if available) → rubric, errors, upgrades
                • errors → ErrorEvents + small negative observations on their nodes
                • otherwise self-check → SubmitSelfCheckAsync
```

## Key design decisions

| Decision | Alternatives considered | Why |
|---|---|---|
| Logistic (Rasch-style) ability per node with Elo update | Bayesian Knowledge Tracing, deep knowledge tracing | One interpretable scale shared by learners and items (CEFR bands map to difficulty), no offline fitting, confidence is explicit. |
| SM-2 family for items, separate from node ability | FSRS | Simpler to reason about and test; grades derived from behaviour remove self-rating. FSRS can replace it behind the same `ReviewScheduler` API later. |
| Errors as coded events, aggregated in windows | Only per-node accuracy | Free production (the main gap) yields errors, not right/wrong; codes let AI feedback and deterministic checks feed the same model. |
| Planner with fixed slot order and reasons | Pure priority queue | Predictable sessions, explicit pedagogy (re-check → review → production → focus → input), transparent to the learner. |
| Deterministic core + optional AI | AI-only chat tutor | Works offline and for free every day; AI adds what only AI can (judging free text). Structured output keeps AI results machine-readable. |
| Flat `Exercise` record with per-type validation | Class hierarchy per type | JSON content files and DB storage stay trivial; `ExerciseValidator` enforces the per-type contract. |
| Blazor Server, no prerender | Blazor WASM, MVC | Single process, single language, no API layer for a personal tool; no prerender because a local app gains nothing from it and would double-initialise pages. Account/* pages are the one exception: they need genuine static SSR to set the auth cookie from a real HTTP response, so `@rendermode` is applied once, at `<Routes>` in `App.razor`, computed per-request from the path — not lower down, since a render-mode boundary can only wrap a component whose *own* parent gives it nothing but serializable parameters (a layout's `Body`, or `AuthorizeRouteView`'s `NotAuthorized`, are `RenderFragment`s and fail that check with an explicit framework error if you try). See `App.razor`'s own comment for the full reasoning - this was the hardest part of the accounts feature to get right. |
| Scoped `ICurrentUserAccessor`, not a `UserId` parameter threaded through `ILearningService` | Add `UserId` to every one of ~30 method signatures | Blazor Server DI scope = one SignalR circuit = one signed-in learner, which makes a Scoped accessor the idiomatic seam; `ILearningService`'s public surface (and every UI call site) stays untouched. `TutorRegistry` is the one place this needs care beyond "resolve fresh per call": it caches a built `ITutor` instance, so it re-checks the signed-in learner before every provider call (`EnsureCurrentAsync`) rather than trusting a single load at circuit start - a circuit can, in practice, outlive a logout/login round-trip. |
| SQLite via EF Core migrations, `IdentityDbContext<ApplicationUser>` | Custom membership table, external auth provider | ASP.NET Core Identity ships in the shared framework already (only the EF Core store is a separate package); `SkillState`/`ReviewState` keep their Core-only shape via EF Core *shadow* `UserId` properties instead of gaining a Web/Identity dependency of their own. `GeneratedExercisesEntity` is the one table that stays global/shared across accounts - `ContentCatalogProvider` is a process-wide singleton merging one catalog for everyone, and making that per-user would be a materially bigger change than "add accounts"; documented here as a conscious trade-off, not an oversight. |
| No email sender configured | Wire up SMTP/SendGrid for a personal project | `IdentityNoOpEmailSender` logs the confirmation/reset link instead of emailing it (same fallback the official template uses without SMTP configured). `RequireConfirmedAccount = false`, so registration signs the learner in immediately rather than blocking on a mail that won't arrive. The reset-password flow is still fully wired and tested end to end (link read from the server log) - swapping in a real sender later is a one-line registration change, not a redesign. |

## Content model

`content/taxonomy.json` – `nodes[]` (id, area, title, band, weight, serbianInterference, interferenceNote, prerequisites) and `errorTypes[]` (code, nodeId, title, severity).

`content/exercises/*.json` – `exercises[]` in one flat shape; the fields a type needs:

| Type | Required | Optional |
|---|---|---|
| cloze, transform, translate, dictation | prompt, answers | instruction, hint, explanation, serbianNote, text (dictation: spoken sentence) |
| multipleChoice | prompt, options, correctIndex | explanation |
| wordOrder | prompt, options (chunks), answers (ordered sentence) | explanation |
| vocab | prompt (Serbian), answers (German incl. article), lemma | article, plural, exampleDe, explanation |
| freeWrite | prompt, minWords, rubric | modelAnswer, tags |
| speak | prompt, rubric | targetSeconds, modelAnswer |
| reading | prompt, text, questions | audioOnly (true = Hören: text is spoken, never shown) |

The test suite loads the real content and rejects any item that violates this contract, any answer the checker would not accept, and any core node lacking items on both sides of the B1/B2 boundary.

## Persistence

SQLite file `data/lotse.db`. `LotseDbContext : IdentityDbContext<ApplicationUser>` - the Identity tables (`AspNetUsers` and friends) alongside Lotse's own: LearnerProfiles (one per account), SkillStates (per account+node), ReviewStates (per account+exercise), Attempts, ErrorEvents, Sessions (plan as JSON), Productions (text + evaluation JSON), Settings (per account+key - this is where the Tutor's encrypted API key lives, never readable from another account), and the one shared table, GeneratedExercises (JSON, no `UserId` - see the trade-off table above).

`SkillState` and `ReviewState` are Core types mapped directly by EF Core – no duplicate entity classes; computed members are ignored in the model. Their `UserId` is an EF Core *shadow* property (set via `Entry(x).Property("UserId")`, never a real field on the Core type) for the same reason: `Lotse.Core` stays free of any notion of "account".

## Passkeys (WebAuthn)

Identity schema v3 (`IdentityDbContext` adds `AspNetUserPasskeys` when `IdentityOptions.Stores.SchemaVersion` says so). That option is read from the *application* service provider behind the DbContext options, so `LotseDbContext.OnConfiguring` supplies a minimal provider carrying exactly that one setting whenever nobody else did - tests and `dotnet ef` build the same model as the app, and `DatabaseInitializerTests` checks that the migrations agree. The browser side is the official template's `<passkey-submit>` custom element (`PasskeySubmit.razor.js`): it fetches creation/request options from two minimal-API endpoints (antiforgery token in a header, validated explicitly), runs `navigator.credentials.create/get`, and posts the credential back through the surrounding static form - the login page keeps working exactly like a plain HTML form, this is its only script. The E2E test drives the whole ceremony with Chromium's virtual authenticator (CDP `WebAuthn.addVirtualAuthenticator`), so it runs headless in CI without hardware.

## Multi-user accounts

Every account is fully isolated: separate skill state, sessions, error journal, productions, and Tutor settings (provider, model, encrypted API key). `MultiUserIsolationTests` (`tests/Lotse.Core.Tests`) asserts this directly rather than by inspection - it registers two learners, has each save different Tutor settings and answer different exercises, and checks neither can see the other's rows, including through `TutorRegistry`'s in-memory cache. "Alle Lerndaten löschen" on the settings page deletes only the signed-in account's rows (eight scoped `ExecuteDeleteAsync` calls in one transaction) - never the whole database, which is what an earlier, single-user version of this reset did - and leaves the shared generated-exercise bank and the account's tutor settings alone. `MultiUserIsolationTests` checks every one of the eight tables.

## AI integration

`ClaudeTutor` uses `client.Messages.Create` with `OutputConfig.Format = JsonOutputFormat(schema)` for evaluation and generation, and plain text for the discussion partner. The system prompt carries the learner context (native language, occupation, known weak nodes, recent error codes) and the full error catalogue so the model tags with known codes only; unknown codes are dropped on ingestion. Effort defaults to `medium`. The API key is read from configuration or `ANTHROPIC_API_KEY` and never persisted by the app.
