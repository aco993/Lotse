# Lotse – Konzept

*Lotse* (der Lotse = the pilot who steers a ship safely into harbour) is a personal, adaptive German-learning system whose single goal is a **real B2** – passing a recognised exam (Goethe-Zertifikat B2 as the reference format, telc Deutsch B2 transfers) and working confidently in German every day.

This document explains *why* the system is built the way it is. Implementation details live in [ARCHITEKTUR.md](ARCHITEKTUR.md), the roadmap in [PLAN.md](PLAN.md).

## 1. The learner and the gap

- Native language Serbian, lives and works in Germany as a software developer.
- Level B1–B2, uneven: **understands much more than he can produce**. Reading and listening are ahead; spontaneous speaking and writing lag.
- Little time per day (5–15 minutes), occasionally longer blocks, and a need for realistic exam rehearsal.

Two consequences shape everything:

1. **Production first.** Recognition tasks (multiple choice) are cheap but train the skill he already has. The default exercise formats are therefore *productive*: gap fill with typed answers, sentence transformation, Serbian→German translation, word order, vocabulary asked Serbian→German *with article*, dictation, and – every day – a short piece of free writing or speaking.
2. **Contrastive, not generic.** The mistakes a Serbian speaker makes are predictable: missing articles (Serbian has none), verb-second and verb-final word order, case after prepositions, *haben/sein* in the perfect (Serbian always uses *biti*), verb + preposition pairs, double negation, false friends (*Konkurs*, *Mappe*, *Pension*, *Küche/Kuchen*). Every skill node carries a Serbian-contrast note and the feedback surfaces it exactly when the corresponding mistake happens.

## 2. What "adaptive" means here

Adaptation is not a slogan; it is a closed loop with four concrete mechanisms.

### 2.1 A skill map instead of a course

The language is decomposed into **59 skill nodes** across seven areas (Grammatik, Wortschatz, Redemittel, Lesen, Hören, Schreiben, Sprechen), each placed on a CEFR sub-band (B1.1 … B2.2). Exercises point at nodes; **54 error codes** point at nodes. The learner is described by one ability estimate per node – nothing is ever "lesson 7 of 30".

### 2.2 An ability model that speaks CEFR

Each node keeps an ability θ on the same scale as exercise difficulty (Rasch-style logistic model, 0 = the B1/B2 boundary). After every answer θ moves by an Elo-like update whose step size shrinks as evidence accumulates. "Beherrschung" shown in the UI is simply *P(solve a B2.1 item on this node)*. The model is deliberately simple: it needs no offline training, is explainable to the learner, and its confidence (how many observations) is displayed, so an early guess never looks like a fact.

### 2.3 Errors as first-class data

Every mistake becomes an **error event with a code** – whether detected deterministically (wrong case, missing article, capitalisation slip) or by the AI tutor in free writing. Aggregating events over 7/30-day windows yields the weak areas, their trend, and feeds directly into the next session. The learner sees the same journal the planner uses.

### 2.4 The session planner

Given a time budget, the planner composes a session in a fixed, deliberate order:

1. **Wiedervorlage** – re-checks of nodes that used to be weak and now look fine (see 2.5),
2. **Wiederholung** – due spaced-repetition items (capped at 40 % of the budget),
3. **Produktion** – one writing or speaking task, alternating, even in short sessions every third day,
4. **Schwerpunkt** – drills on the two or three weakest nodes at *θ + 0.25* difficulty (≈ 70–80 % expected success), production-oriented formats preferred, no two consecutive items on the same node,
5. **Input** – a reading or listening task when time allows.

Every step carries a human-readable reason ("Schwerpunkt Passiv: 3 Fehler in den letzten 7 Tagen"), because an adaptive system that cannot explain itself is not trusted.

### 2.5 "Did you actually learn it?" – the re-check cycle

A node that was weak and then recovers is not trusted on the spot. It is flagged and re-tested after **7, 21 and 60 days**. Three passed re-checks clear the flag; one failed re-check sends it back into focus. This is the mechanism that "periodically brings back things I previously struggled with and checks whether I actually learned them".

### 2.6 Spaced repetition for items

Independent of node ability, each exercise has an SM-2-family review state. Grades are derived from behaviour (wrong / slip / hint / speed), never self-rated. Lapses shorten instead of resetting, intervals are capped, due dates get a small deterministic jitter so reviews do not pile up.

## 3. Deterministic core, AI on top

The system is designed so that **everything works without an API key**: content bank, checking, ability model, planner, spaced repetition, re-checks, dashboards, exam simulation with self-check rubrics.

With a Claude API key three things switch on:

| Capability | What it adds |
|---|---|
| **Evaluation of free production** | Rubric scores on the exam criteria (Erfüllung, Kohärenz, Wortschatz, Strukturen), an estimated level, tagged errors from the *same* 54-code catalogue, a corrected version, "upgrades" (the learner's sentences rewritten at B2), a coach message in German with Serbian contrast where useful. Every tagged error flows into the learner model. |
| **Exercise generation** | New drills for a weak node at the learner's current difficulty, in professional/everyday context, targeted at his recent error codes, validated before they enter the bank. |
| **Discussion partner** | A B2 exam partner for *Sprechen Teil 2* that argues the opposite position, asks back, and appends one short correction per turn. |

All structured AI output goes through JSON-schema output so the deterministic engine can ingest it; unknown error codes are dropped rather than corrupting the taxonomy.

## 4. Exam realism

The Goethe B2 blueprint (modules, parts, timing, item counts, pass rule, per-part tips) is encoded as data and drives the exam page, the readiness report and the tips. Writing tasks exist in exact exam formats (Teil 1 forum post ~150 words with four points, Teil 2 formal message ~100 words), speaking tasks as Teil 1 talks with the standard four guiding points and Teil 2 theses, reading in the Teil 1/3/5 formats and listening as audio-only items (browser TTS). Readiness per module is estimated from node mastery with confidence shrinkage and a plain-language verdict.

## 5. UX principles

- **Zero decisions to start.** One button on the *Heute* page. The planner decides; the learner can override on the *Themen* page.
- **Short by default, long on demand.** 5/10/15/30-minute sessions; exam simulations as separate entries.
- **Transparent adaptation.** Reason chips on every step, mastery bars with confidence, a readable error journal.
- **Honest scoring.** Slips (capitalisation, umlaut spelling, one typo in a long word, missing article) count as "almost right" but are always logged.
- **Immersion with a safety line.** UI and explanations in German; contrastive Serbian notes exactly where interference bites.

## 6. Why this stack

.NET 10 with Blazor Server keeps the whole loop (UI, engine, persistence, AI calls) in one process and one language – the fastest path to a working product and a direct fit for a C#/.NET portfolio. The learning engine is a dependency-free class library with unit tests, so it could be reused behind a Web API, a mobile client or a WASM build without change. SQLite keeps the personal data local and zero-ops.
