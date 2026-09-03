#!/usr/bin/env python3
"""Misst, wie weit die Lotse-Inhalte das Referenzinventar der B2-Grammatik abdecken.

Liest  tools/b2-grammar-reference.json  (das Inventar),  content/taxonomy.json,
content/exercises/*.json  und  content/lessons/*.json  und schreibt
docs/GRAMMATIK_ABDECKUNG.md  sowie  docs/grammatik-abdeckung.json.

    python tools/grammar-coverage.py              # Bericht neu schreiben
    python tools/grammar-coverage.py --check      # nur prüfen (Exit 1, wenn eine Muss-Stelle < 2)
    python tools/grammar-coverage.py --baseline docs/grammatik-abdeckung.json   # Vorher/Nachher

Zuordnung einer Referenzstelle zu Übungen:
  * "nodes" nicht leer  -> Übung muss an einem dieser Knoten hängen;
  * "pattern" gesetzt   -> der gesamte Übungstext (Prompt, Instruction, Answers, Options,
                           Explanation, Hint, SerbianNote, Text, Dialogzeilen, Match-Paare,
                           Lesefragen, ModelAnswer) muss zusätzlich passen.
  Eine Stelle ohne "nodes" wird also allein über das Muster gefunden - so werden Themen
  gemessen, die quer zu den Knoten liegen (Passiversatz, zweiteilige Konnektoren, n-Deklination).
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
from collections import Counter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

BANDS = ["B1_1", "B1_2", "B2_1", "B2_2", "C1"]

# Rezeptiv = erkennen/auswählen, produktiv = selbst formulieren (getippt), frei = Text/Rede.
RECEPTIVE = {"MultipleChoice", "Match", "SpotError", "Reading", "Dialogue", "Dictation"}
PRODUCTIVE = {"Cloze", "Transform", "Translate", "WordOrder", "Vocab"}
FREE = {"FreeWrite", "Speak"}


def load_json(path):
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def exercise_blob(e):
    """Der Teil einer Übung, an dem die Struktur wirklich geübt oder erklärt wird.

    Bewusst NICHT durchsucht: `rubric` und `modelAnswer` (eine Struktur, die nur im
    Musterlösungstext vorkommt, ist Anschauung, kein Training) und bei Lesetexten der
    Textkörper samt Fragen (eine Struktur irgendwo in einem 400-Wörter-Text übt sie nicht).
    """
    parts = [
        e.get("prompt") or "",
        e.get("instruction") or "",
        e.get("explanation") or "",
        e.get("hint") or "",
        e.get("serbianNote") or "",
        e.get("exampleDe") or "",
    ]
    if e.get("type") != "Reading":
        parts.append(e.get("text") or "")
        for q in e.get("questions") or []:
            parts.append(q.get("question") or "")
            parts += q.get("options") or []
    parts += e.get("answers") or []
    parts += e.get("options") or []
    for line in e.get("lines") or []:
        parts.append(line.get("text") or "")
        parts += line.get("options") or []
        parts += line.get("feedback") or []
    for pair in e.get("pairs") or []:
        parts += [pair.get("left") or "", pair.get("right") or ""]
    return "\n".join(parts)


def lesson_blob(lesson):
    g = lesson.get("grammar") or {}
    parts = [g.get("title") or "", g.get("text") or "", lesson.get("merksatz") or ""]
    for ex in g.get("examples") or []:
        parts += [ex.get("german") or "", ex.get("serbian") or "", ex.get("note") or ""]
    return "\n".join(parts)


def load_content(root):
    taxonomy = load_json(os.path.join(root, "content", "taxonomy.json"))
    exercises = []
    for path in sorted(glob.glob(os.path.join(root, "content", "exercises", "*.json"))):
        exercises += load_json(path).get("exercises") or []
    lessons = []
    for path in sorted(glob.glob(os.path.join(root, "content", "lessons", "*.json"))):
        data = load_json(path)
        lessons.append(data["lesson"])
        exercises += data.get("exercises") or []
    dupes = [i for i, n in Counter(e["id"] for e in exercises).items() if n > 1]
    if dupes:
        raise SystemExit("Doppelte Übungs-IDs: " + ", ".join(sorted(dupes)))
    return taxonomy, exercises, lessons


def score_item(item, exercises, lessons, blobs, lesson_blobs):
    nodes = set(item.get("nodes") or [])
    pattern = re.compile(item["pattern"]) if item.get("pattern") else None
    expl_pattern = re.compile(item["explanationPattern"]) if item.get("explanationPattern") else pattern

    hits = []
    for e in exercises:
        if nodes and e["nodeId"] not in nodes:
            continue
        if pattern and not pattern.search(blobs[e["id"]]):
            continue
        hits.append(e)

    by_band = Counter(e["band"] for e in hits)
    by_kind = Counter(
        "rezeptiv" if e["type"] in RECEPTIVE else "produktiv" if e["type"] in PRODUCTIVE else "frei"
        for e in hits
    )
    used_nodes = sorted({e["nodeId"] for e in hits})

    # Ein eigenes "explanationPattern" entscheidet allein: Themen, die quer zu den Knoten liegen,
    # werden dort erklärt, wo sie hingehören, nicht dort, wo der Knoten der Lektion steht.
    # Ohne eigenes Muster gilt: die Lektion muss den Knoten als Grammatikthema haben (und, wenn die
    # Stelle ein Muster hat, es auch im Erklärtext treffen).
    override = item.get("explanationPattern") is not None
    explained_in = []
    for lesson in lessons:
        blob = lesson_blobs[lesson["id"]]
        if override:
            ok = bool(expl_pattern.search(blob))
        else:
            ok = ((lesson.get("grammar") or {}).get("nodeId") in nodes if nodes else True) \
                 and (not expl_pattern or bool(expl_pattern.search(blob)))
        if ok:
            explained_in.append(f"L{lesson['order']:02d}")

    total = len(hits)
    has_expl = bool(explained_in)
    has_prod = by_kind["produktiv"] + by_kind["frei"] > 0
    has_b22 = by_band["B2_2"] + by_band["C1"] > 0

    if total == 0:
        score = 0
    elif total <= 3:
        score = 1
    elif has_expl and total >= 6 and has_prod and has_b22:
        score = 3
    else:
        score = 2

    missing = []
    if score < 3:
        if not has_expl:
            missing.append("keine Erklärung in einer Lektion")
        if total < 6:
            missing.append(f"nur {total} Übungen (6 nötig)")
        if not has_prod:
            missing.append("kein produktiver Aufgabentyp")
        if not has_b22:
            missing.append("keine B2.2-Übung")

    return {
        "id": item["id"],
        "group": item["group"],
        "title": item["title"],
        "priority": item["priority"],
        "declaredNodes": sorted(nodes),
        "usedNodes": used_nodes,
        "total": total,
        "bands": {b: by_band[b] for b in BANDS},
        "kinds": {k: by_kind[k] for k in ("rezeptiv", "produktiv", "frei")},
        "explainedIn": explained_in,
        "score": score,
        "missing": missing,
        "exerciseIds": [e["id"] for e in hits],
    }


def production_demands(reference, exercises):
    """Wie oft verlangt eine Rubrik einer Schreib-/Sprechaufgabe eine Struktur ausdrücklich?

    Bewusst getrennt von der Übungsabdeckung: eine Struktur zu drillen und sie unter
    Produktionsdruck zu verlangen sind zwei verschiedene Dinge, und der Prüfer sieht nur das zweite.
    """
    spec = reference.get("productionDemands")
    if not spec:
        return None
    tasks = [e for e in exercises if e["type"] in ("FreeWrite", "Speak")]
    rows = []
    for s in spec["structures"]:
        pattern = re.compile(s["pattern"])
        hits = [e["id"] for e in tasks if any(pattern.search(r) for r in e.get("rubric") or [])]
        rows.append({"name": s["name"], "tasks": len(hits), "exerciseIds": hits})
    return {"minTasksPerStructure": spec.get("minTasksPerStructure", 0), "totalTasks": len(tasks), "structures": rows}


def percentages(rows):
    total = len(rows)
    muss = [r for r in rows if r["priority"] == "Muss"]
    a = 100.0 * sum(1 for r in rows if r["score"] >= 2) / total if total else 0.0
    b = 100.0 * sum(1 for r in muss if r["score"] == 3) / len(muss) if muss else 0.0
    w = {"Muss": 3, "Soll": 2, "Kann": 1}
    got = sum(w[r["priority"]] * r["score"] for r in rows)
    maxi = sum(w[r["priority"]] * 3 for r in rows)
    c = 100.0 * got / maxi if maxi else 0.0
    return {"a_score_ge_2": round(a, 1), "b_muss_score_3": round(b, 1), "c_weighted": round(c, 1)}


def render_markdown(reference, rows, pcts, stats, baseline=None, notes=None, demands=None):
    out = []
    w = out.append
    w("# Grammatik-Abdeckung für das Goethe-Zertifikat B2")
    w("")
    w("> Automatisch erzeugt von `tools/grammar-coverage.py` aus `tools/b2-grammar-reference.json`")
    w("> und den Inhalten in `content/`. Nicht von Hand bearbeiten – Skript neu laufen lassen.")
    w("")
    w(f"Gemessener Bestand: **{stats['exercises']} Übungen**, {stats['lessons']} Lektionen, "
      f"{stats['nodes']} Kompetenzknoten. Referenzinventar: **{len(rows)} Stellen** "
      f"({stats['muss']} Muss, {stats['soll']} Soll, {stats['kann']} Kann).")
    w("")

    w("## 1. Woher das Referenzinventar stammt")
    w("")
    w("Das Goethe-Institut veröffentlicht für B2 **keine** verbindliche Grammatikliste. Die folgende")
    w("Liste ist deshalb aus drei Quellen zusammengesetzt:")
    w("")
    for s in reference["sources"]:
        w(f"- {s}")
    w("")
    for name, text in reference["priorities"].items():
        w(f"- **{name}** – {text}")
    w("")

    w("## 2. Die drei Prozentzahlen")
    w("")
    w("| Kennzahl | Definition | Wert |")
    w("| --- | --- | --- |")
    w(f"| (a) Breite | Anteil aller Stellen mit Note ≥ 2 (mindestens 4 Übungen vorhanden) | **{pcts['a_score_ge_2']} %** |")
    w(f"| (b) Tiefe an den Pflichtstellen | Anteil der **Muss**-Stellen mit Note 3 (Erklärung + ≥ 6 Übungen + produktiver Typ + B2.2-Übung) | **{pcts['b_muss_score_3']} %** |")
    w(f"| (c) Gewichteter Skor | Σ(Gewicht × Note) / Σ(Gewicht × 3), Gewicht Muss 3 / Soll 2 / Kann 1 | **{pcts['c_weighted']} %** |")
    w("")
    if baseline:
        w("**Vorher / Nachher**")
        w("")
        w("| Kennzahl | vorher | nachher | Δ |")
        w("| --- | ---: | ---: | ---: |")
        for key, label in (("a_score_ge_2", "(a) Breite"), ("b_muss_score_3", "(b) Muss mit Note 3"), ("c_weighted", "(c) Gewichtet")):
            before, after = baseline[key], pcts[key]
            w(f"| {label} | {before} % | {after} % | {after - before:+.1f} |")
        w("")
    w("**Relevant für „reicht das zum Bestehen?“ ist (c), der gewichtete Skor.** (a) belohnt bereits")
    w("vier beliebige Übungen und ist damit zu gutmütig; (b) misst nur die Pflichtstellen und ist so")
    w("streng, dass ein einziges fehlendes B2.2-Item eine sonst gut abgedeckte Stelle auf 2 drückt.")
    w("(c) bildet beides ab: es zählt Vollständigkeit, gewichtet aber nach Prüfungsrelevanz.")
    w("")
    w("Alle drei messen jedoch **Abdeckung des Inventars durch das Material**, nicht Prüfungsreife.")
    w("Sie sättigen: sobald jede Stelle erklärt und sechsmal geübt ist, stehen sie auf 100 % und")
    w("können nicht mehr zwischen „gerade ausreichend“ und „gründlich“ unterscheiden. Wie tief die")
    w("Abdeckung wirklich reicht, steht in Abschnitt 5 und 6, nicht in dieser Tabelle.")
    w("")

    w("## 3. Vollständige Tabelle")
    w("")
    w("Legende Note: 0 = nichts · 1 = erwähnt (≤ 3 Übungen) · 2 = ≥ 4 Übungen, aber ohne Erklärung,")
    w("ohne produktiven Typ oder ohne B2.2-Item · 3 = Erklärung + ≥ 6 Übungen + produktiv + B2.2.")
    w("")
    current_group = None
    for r in rows:
        if r["group"] != current_group:
            current_group = r["group"]
            w("")
            w(f"### {current_group}")
            w("")
            w("| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |")
            w("| --- | --- | --- | --- | --- | --- | --- | --- |")
        if r["declaredNodes"]:
            nodes = ", ".join(n.replace("GR.", "").replace("WS.", "WS ") for n in r["declaredNodes"])
        else:
            # Querliegende Stelle: nur über das Muster gefunden, verteilt über viele Knoten.
            nodes = f"querliegend ({len(r['usedNodes'])} Knoten)"
        if len(nodes) > 58:
            nodes = nodes[:55] + "…"
        b = r["bands"]
        bands = f"{r['total']} ({b['B1_1'] + b['B1_2']}/{b['B2_1']}/{b['B2_2']})"
        k = r["kinds"]
        kinds = f"{k['rezeptiv']}/{k['produktiv']}/{k['frei']}"
        expl = ", ".join(r["explainedIn"]) if r["explainedIn"] else "–"
        if len(expl) > 28:
            expl = expl[:25] + "…"
        w(f"| {r['id']} | {r['title']} | {r['priority']} | {nodes} | {bands} | {kinds} | {expl} | **{r['score']}** |")
    w("")

    w("## 4. Lücken (sortiert nach Dringlichkeit)")
    w("")
    order = {"Muss": 0, "Soll": 1, "Kann": 2}
    gaps = sorted((r for r in rows if r["score"] < 3), key=lambda r: (order[r["priority"]], r["score"], r["id"]))
    hard = [r for r in gaps if r["score"] <= 1]
    if hard:
        w("**Echte Lücken (Note 0–1):**")
        w("")
        for r in hard:
            w(f"- `{r['id']}` **{r['priority']}** – {r['title']}: Note {r['score']} ({', '.join(r['missing'])})")
        w("")
    soft = [r for r in gaps if r["score"] == 2]
    if soft:
        w("**Vorhanden, aber nicht vertieft (Note 2):**")
        w("")
        for r in soft:
            w(f"- `{r['id']}` {r['priority']} – {r['title']}: {', '.join(r['missing'])}")
        w("")
    if not gaps:
        w("Keine – jede Stelle hat Note 3.")
        w("")

    w("## 5. Wie tief ist die Abdeckung wirklich?")
    w("")
    w("Note 3 ist eine **Anwesenheitsschwelle**: erklärt, sechsmal geübt, davon einmal produktiv und")
    w("einmal auf B2.2. Sie sagt nichts darüber, ob sechs Übungen zum Beherrschen reichen. Die")
    w("folgenden Stellen stehen genau auf dieser Schwelle und sind die ersten Kandidaten für Nachschub")
    w("(„Drills“ = alle Übungen außer freier Produktion):")
    w("")
    w("| # | Stelle | Prio | Drills | davon B2.2 |")
    w("| --- | --- | --- | ---: | ---: |")
    thin = sorted(rows, key=lambda r: (r["kinds"]["rezeptiv"] + r["kinds"]["produktiv"], r["id"]))[:12]
    for r in thin:
        w(f"| {r['id']} | {r['title'][:70]} | {r['priority']} | {r['kinds']['rezeptiv'] + r['kinds']['produktiv']} | {r['bands']['B2_2']} |")
    w("")

    if demands:
        w("## 6. Produktionsdruck: Was die Rubriken wirklich verlangen")
        w("")
        w("Eine Struktur zu drillen und sie unter Produktionsdruck zu verlangen sind zwei")
        w("verschiedene Dinge – und im Schreiben und Sprechen sieht der Prüfer nur das zweite")
        w(f"(Kriterien „Korrektheit“ und „Repertoire“). Gezählt wird hier allein der Rubriktext der")
        w(f"{demands['totalTasks']} Schreib- und Sprechaufgaben, keine Drills.")
        w("")
        w("| Struktur | Aufgaben, die sie verlangen |")
        w("| --- | ---: |")
        for s in sorted(demands["structures"], key=lambda s: (-s["tasks"], s["name"])):
            mark = "" if s["tasks"] >= demands["minTasksPerStructure"] else " ⚠"
            w(f"| {s['name']} | {s['tasks']}{mark} |")
        w("")
        w(f"Untergrenze: {demands['minTasksPerStructure']} Aufgaben je Struktur, abgesichert durch")
        w("`GrammarCoverageTests`.")
        w("")

    if notes:
        w(notes.strip())
        w("")
    return "\n".join(out) + "\n"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--root", default=ROOT)
    ap.add_argument("--check", action="store_true", help="nur prüfen, nichts schreiben")
    ap.add_argument("--baseline", help="frühere grammatik-abdeckung.json für Vorher/Nachher")
    ap.add_argument("--json-only", action="store_true")
    args = ap.parse_args()

    reference = load_json(os.path.join(args.root, "tools", "b2-grammar-reference.json"))
    taxonomy, exercises, lessons = load_content(args.root)

    blobs = {e["id"]: exercise_blob(e) for e in exercises}
    lesson_blobs = {l["id"]: lesson_blob(l) for l in lessons}

    known = {n["id"] for n in taxonomy["nodes"]}
    for item in reference["items"]:
        for n in item.get("nodes") or []:
            if n not in known:
                raise SystemExit(f"{item['id']}: unbekannter Knoten {n}")

    rows = [score_item(i, exercises, lessons, blobs, lesson_blobs) for i in reference["items"]]
    demands = production_demands(reference, exercises)
    pcts = percentages(rows)
    counts = Counter(r["priority"] for r in rows)
    stats = {
        "exercises": len(exercises), "lessons": len(lessons), "nodes": len(taxonomy["nodes"]),
        "muss": counts["Muss"], "soll": counts["Soll"], "kann": counts["Kann"],
    }

    failing = [r for r in rows if r["priority"] == "Muss" and r["score"] < 2]

    thin_demands = [s for s in (demands or {}).get("structures", [])
                    if s["tasks"] < demands["minTasksPerStructure"]]

    if args.check:
        for r in failing:
            print(f"FEHLT: {r['id']} ({r['title']}) Note {r['score']} – {', '.join(r['missing'])}")
        for s in thin_demands:
            print(f"PRODUKTIONSDRUCK: „{s['name']}“ nur in {s['tasks']} Rubriken "
                  f"(mindestens {demands['minTasksPerStructure']})")
        print(f"(a) {pcts['a_score_ge_2']} %  (b) {pcts['b_muss_score_3']} %  (c) {pcts['c_weighted']} %")
        return 1 if failing or thin_demands else 0

    baseline = None
    if args.baseline and os.path.exists(args.baseline):
        baseline = load_json(args.baseline)["percentages"]

    # Der qualitative Teil wird von Hand gepflegt und hier ans Ende des Berichts gehängt,
    # damit ein Neulauf des Skripts ihn nicht überschreibt.
    notes_path = os.path.join(args.root, "tools", "grammar-coverage-notes.md")
    notes = open(notes_path, encoding="utf-8").read() if os.path.exists(notes_path) else None

    payload = {"percentages": pcts, "stats": stats, "items": rows, "productionDemands": demands}
    docs = os.path.join(args.root, "docs")
    os.makedirs(docs, exist_ok=True)
    with open(os.path.join(docs, "grammatik-abdeckung.json"), "w", encoding="utf-8") as fh:
        json.dump(payload, fh, ensure_ascii=False, indent=2)
        fh.write("\n")
    if not args.json_only:
        with open(os.path.join(docs, "GRAMMATIK_ABDECKUNG.md"), "w", encoding="utf-8", newline="\n") as fh:
            fh.write(render_markdown(reference, rows, pcts, stats, baseline, notes, demands))

    print(f"{len(rows)} Stellen gemessen über {len(exercises)} Übungen.")
    print(f"(a) Note>=2: {pcts['a_score_ge_2']} %   (b) Muss=3: {pcts['b_muss_score_3']} %   (c) gewichtet: {pcts['c_weighted']} %")
    for r in failing:
        print(f"  Muss unter 2: {r['id']} {r['title']} (Note {r['score']})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
