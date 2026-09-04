# Auftrag 0.9 – die offenen Persona-Befunde

Work order for the next session. Everything here comes from `docs/NUTZERTEST_2026-09.md` (ten simulated learners,
average 7.0/10) and the "From the persona test – next" list in `docs/PLAN.md`. Read those two files first, then
`docs/ARCHITEKTUR.md` (sections "Sessions are idempotent", "Drafts and the reload", the decision table rows for
FSRS and `FreeTextAnalyzer`) and `docs/KONZEPT.md`. Reply to the user in Serbian; code, comments and docs stay in
the languages the repo already uses (English docs/comments, German UI, Serbian `docs/UPUTSTVO.md`).

## State you start from

- `main` at `4adf606` (0.8.0): FSRS-5, rule-based text analyzer, idempotent sessions, quick placement, eleven
  vocabulary packs (2,048 exercises, 919 lemmas, 59 nodes), 167 tests green, `dotnet format` gate green.
- The working tree may hold uncommitted work on **Piper TTS** (CHANGELOG "Unreleased", `SpeechService.cs`,
  `speech.js`, `Program.cs`, `TextToSpeechTests.cs`). If that is yours, finish and commit it first. Never revert
  or "clean up" changes you did not make; if they block you, say so.
- Dev server: `.claude/launch.json` entry `lotse` (port 5310), plain `dotnet run` (no watch). **Stop it before
  any `dotnet build`, `dotnet test`, `dotnet ef`** - the running app locks the DLLs (MSB3021/3026/3027 is the lock,
  not a code error). Restart it afterwards; the app reads `content/` from the source folder, so content edits
  need only a restart.
- Git: commit freely on `main` (personal repo, no remote), **never push**. One commit per coherent item, with a
  message that says why. Wipe `src/Lotse.Web/data/lotse.db*` before handing over so the app starts fresh.

## Gates (all of them, before every commit)

```bash
dotnet format --no-restore && dotnet build && dotnet test --no-build && dotnet format --verify-no-changes --no-restore
```

- `TreatWarningsAsErrors` is on; migrations are formatter-exempt via `.editorconfig`.
- Schema change ⇒ `dotnet ef migrations add <Name> -p src/Lotse.Infrastructure -s src/Lotse.Web -o Data/Migrations`
  (server stopped). `DatabaseInitializerTests` asserts no pending model changes.
- Every `.razor` change is exercised in a browser before it is committed - the Browser pane or
  `tools/persona-harness` (`npm install` once, then `node run.mjs <script> --out <dir>`; `--mobile`, `--dark`).
  `verify-fixes.mjs` there re-checks the 0.8.0 fixes end to end; extend it or write siblings for the new items.
  Web **and** mobile (375×812), light **and** dark, are equal citizens. Only declared MudBlazor parameters
  (MudBlazor 9.9 - invented ones kill the circuit at runtime with a green build).
- E2E (`tests/Lotse.E2E`, Playwright on Kestrel): add a test for each user-visible flow you build. Build the E2E
  project with the server stopped (it references `Lotse.Web`).
- bUnit (`tests/Lotse.Web.Tests`): `FakeLearningService` throws on anything a test did not expect - add members
  when `ILearningService` grows.

## The items, in priority order

### 1. Target level B2 / C1 (Ana, Petar) - the biggest gap for the C1-bound

- `LearnerProfile.TargetLevel` (`enum TargetLevel { B2, C1 }`, default B2, migration). Setting on
  `Einstellungen` next to the exam date, with one sentence of consequence for each choice.
- `SessionPlanner.RankFocusNodes` filters `n.Band <= CefrBand.B2_2` today: with C1 the cap is `C1`, so
  `WS.C1_GEHOBEN` and the B2.2 grammar reach the focus slot; `Ability.TargetDifficulty` stays as is.
  `PlacementTest` is unchanged (it calibrates the B1/B2 boundary either way).
- Readiness (`LearnerAnalysis.Readiness`) keeps the Goethe-B2 blueprint; with C1 add a second line on Heute:
  "C1-Nähe" = weighted mastery of the C1 and B2.2 nodes, with the same confidence caption. Do not invent a C1
  exam blueprint - say honestly on `Prüfung B2` that the exam page is B2 and the C1 pack feeds vocabulary
  and register.
- The app bar's "Dein Kurs auf B2" and the Themen intro follow the setting. Themen shows C1 nodes with a "C1"
  chip already (band label) - verify.
- Tests: planner test that a C1 learner gets a C1 node in focus and a B2 learner never does; E2E: switch to C1,
  see the label change.

### 2. Occupation in the profile (Jelena, Dragan, Petar)

- `LearnerProfile.Occupation` (`enum Occupation { Unspecified, IT, Pflege, Bau, Medizin, Buero, Handel }`,
  migration), setting on `Einstellungen`.
- Effect, deliberately small and explainable: in `SessionPlanner.PickDrill` and the filler loop, a tie-break
  that prefers exercises whose `Tags` or `Context` fit the occupation (IT → tags `it`, context `Beruf`;
  Pflege/Medizin → node `WS.GESUNDHEIT_KOERPER` gets +0.15 priority in `RankFocusNodes` and its items win
  ties; Bau → `WS.WOHNEN_MOBILITAET`/`WS.ARBEIT_KARRIERE`). Reason strings must mention it when it decided
  ("… weil du in der Pflege arbeitest"). No occupation ⇒ identical behaviour to today (planner tests must still
  pass unchanged with `Unspecified`).
- The story lessons stay as they are (they are the author's story); note in `UPUTSTVO.md` that lesson content
  is IT-flavoured by design and the occupation steers drills and vocabulary, not the story.

### 3. First name in the profile (Ana)

- The content hard-codes the author's name ("Aleksandar", "Aleksandar Micić") in lesson dialogues and model
  answers. Replace every occurrence in `content/**` with tokens `{Vorname}` / `{Name}` (a Node one-liner; keep a
  test that greps `content/` for the literal name and fails if it returns).
- `LearnerProfile.FirstName` / `LastName` (migration), set on `Einstellungen`; defaults when empty:
  "Aleksandar" / "Aleksandar Micić" (the author's own experience is unchanged).
- One `NameTemplate.Render(text, profile)` in Core, applied at the render boundary: lesson story lines
  (`Lektion.razor`, `DialogueExercise`), `ModelAnswer`, `Prompt`/`Text`/`Instruction` of exercises, TTS text.
  `AnswerChecker` compares learner input against `Answers` - check whether any answer contains a token (it
  should not; assert in the content test).
- Tests: render substitutes; content contains no literal name; a lesson test still passes.

### 4. Motivation beyond the streak (Tamara, Ana, Petar) - "Fortschritt" that people look at

- `Fortschritt` gets a top row of three honest cards computed from FSRS state (`ReviewState.Stability`,
  `DueUtc`, `RetrievabilityAt`):
  - **Wörter, die sitzen**: items with `Stability >= 21` days, plus "davon neu diese Woche".
  - **Fällig in den nächsten 7 Tagen**: a small 7-bar chart of due counts per day (like the 30-day activity bars
    already there), so the learner sees the load coming.
  - **Behalten**: average retrievability of all scheduled items right now, one sentence of meaning.
- **Wochenziel** in the profile (`WeeklyGoalMinutes`, default 60, setting on `Einstellungen`) with a progress
  ring/bar on Heute ("48 von 60 Minuten diese Woche") and a two-line "Wochenrückblick" on Heute every Monday
  (attempts, accuracy, words that stuck, the one weakest node) - deterministic, no tutor needed.
- No XP, no leaderboard: this is a single-learner tool; say so in `KONZEPT.md` §5.
- `LearningService.GetDashboardAsync` / a new `GetProgressStatsAsync` supply the numbers; unit tests on the
  aggregation with hand-built `ReviewState`s.

### 5. Readiness trend (Marko, Dragan)

- Table `ReadinessSnapshots (UserId, Day, Overall, ModulesJson)`, one row per learner and day, written when
  the dashboard loads and no row for today exists. Heute shows "▲ 3 % seit letzter Woche" (or ▼/=) next to the
  readiness number when a snapshot from 6-8 days ago exists; nothing otherwise. Migration + tests.

### 6. Plain-language names on Heute (Nikola, Tamara)

- `SkillNode.PlainTitle` (optional, in `taxonomy.json`) for the 26 grammar nodes: "Hauptsatz: Verb an Position 2
  & TeKaMoLo" → "Satzbau: das Verb steht an zweiter Stelle". Heute's "Der Lotse schlägt vor" and the KPI
  captions use `PlainTitle ?? Title`; Themen and Fortschritt keep the precise title. `ContentTests` assert every
  grammar node has one.

### 7. Smaller items

- Home buttons show a spinner/disabled state while a session starts ("Weitermachen" took 1-3 s without
  feedback - Tamara).
- Placement: when the easier item of a node is answered wrong, skip that node's harder item in the second pass
  (Stefan: "kein echtes Überspringen"). `PlacementTest` builds the list up front, so do it in
  `LearningService.SubmitAnswerAsync` for `SessionKind.Placement`: mark the sibling step `Done` with score 0 and
  `Reason` "übersprungen – Grundlage fehlt". Planner/session tests cover it.
- `Fortschritt`: collapse nodes with `IsStrong` into a "Sitzt (n)" group to shorten the page (Nikola).
- Model answers vs. rubric (Milica): a content test that each `FreeWrite`/`Speak` model answer contains at
  least one marker of every structure its rubric names (keyword lists per structure in the test); fix the
  answers that fail.

## What you must not do

- No new NuGet packages without a reason and an MIT/free licence; no gamification frameworks.
- No AI calls for anything above - every item is deterministic.
- Do not touch the Piper work, the persona harness scripts, or `content/exercises/wortschatz-*.json` beyond the
  name-token replacement.
- Do not change FSRS parameters or the analyzer's rules unless a test proves a false positive.

## Definition of done

Each item: code + tests + docs (`CHANGELOG.md` entry per item under 0.9.0, `README.md` highlight if
user-visible, `UPUTSTVO.md` in Serbian, `ARCHITEKTUR.md` decision row where a design choice was made, tick in
`PLAN.md`, and the "Offen (bewusst)" paragraph of `NUTZERTEST_2026-09.md` updated) + exercised in the browser
(screenshot in the final message) + committed. Final message: what is done, what is not and why, test count,
and the numbers a reader needs - nothing else.
