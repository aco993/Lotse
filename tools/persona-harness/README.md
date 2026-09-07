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
Run it before the .NET tests: it names the offending id, while a content error only fails the catalogue fixture.

The rules that actually cost time when writing a pack:

- **`exampleDe` must contain the lemma's stem** - the first `max(4, length − 3)` characters of the lemma's *last*
  word. A participle silently fails this: "angeordnet" does not contain "anord", "gefunden" does not contain
  "find", "gerät" does not contain "gera". Put the infinitive in the sentence instead.
- **`prompt` is the bridge language, not German.** For `Vocab` and `Translate` the prompt *is* Serbian, and
  `promptEn` is mandatory; every other type has a German prompt and must not carry `promptEn` at all.
- `serbianNote` only ever together with `englishNote` - and written for that first language, not translated.
- `context` is one of `Alltag | Beruf | Pruefung`, ASCII, no umlaut.
- `Match` carries no `answers`, needs at least three pairs, and every **right** side must be unique.
- Lemmas are unique across the whole bank (~1,000 of them), so check before authoring rather than after.

A new node needs at least three drills. A new **grammar** node additionally needs a `plainTitle` and at least one
entry in `errorTypes`, otherwise nothing that happens there can reach the error journal - and
`Dialogue_and_match_are_scored_as_fractions_and_feed_the_node` turns red, because it takes the *first* `Match` in
the catalogue and the load order is alphabetical by filename.
