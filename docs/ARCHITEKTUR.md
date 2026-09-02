# Lotse – Architecture

## Layers

```
┌──────────────────────────────────────────────────────────────────┐
│ Lotse.Web  (Blazor Server, MudBlazor)                            │
│   Pages: Heute · Session · Schreiben · Sprechen · Prüfung ·      │
│          Fortschritt · Fehler · Themen · Einstellungen           │
│   Components: ExerciseRunner → Closed / Reading / Production     │
│   SpeechService (JS interop: Web Speech API TTS + STT)           │
├──────────────────────────────────────────────────────────────────┤
│ Lotse.Infrastructure                                             │
│   LearningService  (application service, the only DB+engine     │
│                     coordinator; short-lived DbContexts)         │
│   ContentLoader / ContentCatalogProvider (seed + generated)      │
│   LotseDbContext (EF Core, SQLite)                               │
│   ClaudeTutor : ITutor  (official Anthropic .NET SDK)            │
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

Dependencies point downwards only. The engine has no reference to EF Core, Blazor or the Anthropic SDK, which is what makes it unit-testable in milliseconds and reusable behind another host.

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
| Blazor Server, no prerender | Blazor WASM, MVC | Single process, single language, no API layer for a personal tool; no prerender because a local app gains nothing from it and would double-initialise pages. |
| SQLite via `EnsureCreated` (for now) | Migrations from day one | Schema is still moving in Phase 1; migrations are planned once it settles (see PLAN.md). |

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

SQLite file `data/lotse.db`. Tables: Profiles (1 row), SkillStates (per node), ReviewStates (per exercise), Attempts, ErrorEvents, Sessions (plan as JSON), Productions (text + evaluation JSON), GeneratedExercises (JSON), Settings.

`SkillState` and `ReviewState` are Core types mapped directly by EF Core – no duplicate entity classes; computed members are ignored in the model.

## AI integration

`ClaudeTutor` uses `client.Messages.Create` with `OutputConfig.Format = JsonOutputFormat(schema)` for evaluation and generation, and plain text for the discussion partner. The system prompt carries the learner context (native language, occupation, known weak nodes, recent error codes) and the full error catalogue so the model tags with known codes only; unknown codes are dropped on ingestion. Effort defaults to `medium`. The API key is read from configuration or `ANTHROPIC_API_KEY` and never persisted by the app.
