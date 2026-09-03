# Grammatik-Abdeckung für das Goethe-Zertifikat B2

> Automatisch erzeugt von `tools/grammar-coverage.py` aus `tools/b2-grammar-reference.json`
> und den Inhalten in `content/`. Nicht von Hand bearbeiten – Skript neu laufen lassen.

Gemessener Bestand: **911 Übungen**, 24 Lektionen, 49 Kompetenzknoten. Referenzinventar: **51 Stellen** (26 Muss, 21 Soll, 4 Kann).

## 1. Woher das Referenzinventar stammt

Das Goethe-Institut veröffentlicht für B2 **keine** verbindliche Grammatikliste. Die folgende
Liste ist deshalb aus drei Quellen zusammengesetzt:

- Profile Deutsch (Glaboniat u. a.), Kapitel „Sprachliche Mittel: Grammatik“, Niveau B2, dazu die Prüfungsziele und die Wortliste des Goethe-Instituts zum Goethe-Zertifikat B2 – eine verbindliche Grammatikliste für B2 veröffentlicht das Institut nicht.
- Schnittmenge der Grammatikinventare gängiger B2-Lehrwerke: Aspekte neu B2 (Klett), Sicher! B2 (Hueber), Erkundungen B2 (Schubert).
- Bewertungsraster Goethe B2 Schreiben und Sprechen, Kriterien „Korrektheit“ und „Wortschatz/Repertoire“ – also die Strukturen, die dort tatsächlich Punkte kosten oder bringen.

- **Muss** – Ohne diese Struktur ist B2 nicht erreichbar – sie kommt in jeder Prüfung vor, und Fehler daran senken die Korrektheitsnote sofort.
- **Soll** – Wird in einem guten Ergebnis (ab etwa 80 %) erwartet; Fehlen kostet im Kriterium Repertoire.
- **Kann** – Bonus – hebt Text und Rede an den C1-Rand, für das Bestehen nicht nötig.

## 2. Die drei Prozentzahlen

| Kennzahl | Definition | Wert |
| --- | --- | --- |
| (a) Breite | Anteil aller Stellen mit Note ≥ 2 (mindestens 4 Übungen vorhanden) | **100.0 %** |
| (b) Tiefe an den Pflichtstellen | Anteil der **Muss**-Stellen mit Note 3 (Erklärung + ≥ 6 Übungen + produktiver Typ + B2.2-Übung) | **100.0 %** |
| (c) Gewichteter Skor | Σ(Gewicht × Note) / Σ(Gewicht × 3), Gewicht Muss 3 / Soll 2 / Kann 1 | **100.0 %** |

**Vorher / Nachher**

| Kennzahl | vorher | nachher | Δ |
| --- | ---: | ---: | ---: |
| (a) Breite | 84.3 % | 100.0 % | +15.7 |
| (b) Muss mit Note 3 | 69.2 % | 100.0 % | +30.8 |
| (c) Gewichtet | 82.5 % | 100.0 % | +17.5 |

**Relevant für „reicht das zum Bestehen?“ ist (c), der gewichtete Skor.** (a) belohnt bereits
vier beliebige Übungen und ist damit zu gutmütig; (b) misst nur die Pflichtstellen und ist so
streng, dass ein einziges fehlendes B2.2-Item eine sonst gut abgedeckte Stelle auf 2 drückt.
(c) bildet beides ab: es zählt Vollständigkeit, gewichtet aber nach Prüfungsrelevanz.

Alle drei messen jedoch **Abdeckung des Inventars durch das Material**, nicht Prüfungsreife.
Sie sättigen: sobald jede Stelle erklärt und sechsmal geübt ist, stehen sie auf 100 % und
können nicht mehr zwischen „gerade ausreichend“ und „gründlich“ unterscheiden. Wie tief die
Abdeckung wirklich reicht, steht in Abschnitt 5 und 6, nicht in dieser Tabelle.

## 3. Vollständige Tabelle

Legende Note: 0 = nichts · 1 = erwähnt (≤ 3 Übungen) · 2 = ≥ 4 Übungen, aber ohne Erklärung,
ohne produktiven Typ oder ohne B2.2-Item · 3 = Erklärung + ≥ 6 Übungen + produktiv + B2.2.


### Verb / Tempus / Modus

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| V01 | Präsens, Perfekt, Präteritum: Formen und Gebrauch (haben/sein) | Muss | PERFEKT_PRAETERITUM, VERBFORMEN | 41 (14/22/5) | 9/31/1 | L01 | **3** |
| V02 | Plusquamperfekt und Vorzeitigkeit (nachdem) | Muss | PLUSQUAMPERFEKT_FUTUR | 8 (0/6/2) | 1/7/0 | L08 | **3** |
| V03 | Futur I: Zukunft und Vermutung (wohl) | Soll | PLUSQUAMPERFEKT_FUTUR | 10 (0/6/4) | 3/7/0 | L08 | **3** |
| V04 | Futur II: Vermutung über Vergangenes | Kann | querliegend (1 Knoten) | 6 (0/2/4) | 3/3/0 | L08 | **3** |
| V05 | Konjunktiv II Gegenwart: Höflichkeit, Irreales, würde vs. Originalform | Muss | KONJUNKTIV2 | 32 (2/20/10) | 8/23/1 | L02, L07 | **3** |
| V06 | Konjunktiv II Vergangenheit (hätte/wäre + Partizip II) | Muss | KONJUNKTIV2 | 11 (1/5/5) | 2/8/1 | L07 | **3** |
| V07 | Irreale Bedingungssätze (wenn + Konjunktiv II, auch ohne wenn) | Muss | KONJUNKTIV2 | 19 (0/12/7) | 3/15/1 | L07 | **3** |
| V08 | Irrealer Vergleich (als ob) und irrealer Wunsch (wenn … nur/doch) | Soll | querliegend (1 Knoten) | 8 (0/3/5) | 2/6/0 | L07 | **3** |
| V09 | Konjunktiv I und indirekte Rede (Redewiedergabe, Ersatzformen) | Soll | INDIREKTE_REDE | 11 (0/0/11) | 6/5/0 | L22 | **3** |
| V10 | Modalverben: Formen und Bedeutungen (auch Präteritum) | Muss | querliegend (42 Knoten) | 172 (13/109/50) | 51/96/25 | L01, L04, L14, L20, L24 | **3** |
| V11 | Perfekt der Modalverben und doppelter Infinitiv (hat … machen müssen) | Soll | querliegend (4 Knoten) | 8 (0/4/4) | 1/6/1 | L01 | **3** |
| V12 | Modalverben subjektiv: Vermutung und Distanz (müsste, dürfte, soll, will) | Soll | MODALVERBEN_SUBJEKTIV | 12 (0/0/12) | 5/7/0 | L20 | **3** |

### Passiv und Passiversatz

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| P01 | Vorgangspassiv in allen Zeiten (von/durch) | Muss | PASSIV | 35 (1/19/15) | 9/25/1 | L04 | **3** |
| P02 | Zustandspassiv (sein + Partizip II) | Soll | querliegend (1 Knoten) | 8 (0/5/3) | 3/5/0 | L04 | **3** |
| P03 | Passiv mit Modalverben (muss … werden) | Muss | PASSIV | 11 (0/2/9) | 2/9/0 | L04 | **3** |
| P04 | Passiversatz: sich lassen, sein + zu, -bar, man | Soll | querliegend (4 Knoten) | 11 (0/3/8) | 3/8/0 | L04 | **3** |

### Satzbau und Konnektoren

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| S01 | Hauptsatz: Verb an Position 2, Inversion | Muss | HAUPTSATZ_V2 | 21 (6/10/5) | 4/17/0 | L03, L09 | **3** |
| S02 | Nebensatz: Verb am Ende; Satzklammer bei trennbaren Verben | Muss | NEBENSATZ_WORTSTELLUNG, TRENNBARE_VERBEN | 39 (13/21/5) | 9/29/1 | L03, L14 | **3** |
| S03 | Kausal, konzessiv, konsekutiv, final (weil, obwohl, sodass, damit) | Muss | querliegend (24 Knoten) | 75 (10/42/23) | 36/33/6 | L01, L03, L04, L07, L09, … | **3** |
| S04 | Temporale Nebensätze (nachdem, bevor, seit(dem), bis, sobald, während, solange) | Muss | querliegend (14 Knoten) | 34 (2/19/13) | 11/22/1 | L05, L08, L09, L11, L19 | **3** |
| S05 | Konnektorklassen und Wortstellung (Adverb vs. Subjunktion vs. Konjunktion) | Muss | KONNEKTOREN | 28 (1/19/8) | 9/18/1 | L09 | **3** |
| S06 | Zweiteilige Konnektoren (nicht nur … sondern auch, je … desto, zwar … aber, entweder … oder, weder … noch, sowohl … als auch) | Muss | querliegend (5 Knoten) | 12 (0/6/6) | 3/9/0 | L09 | **3** |
| S07 | Konnektor ↔ Präposition (wegen/trotz/während/aufgrund/infolge ↔ weil/obwohl/während) | Muss | querliegend (12 Knoten) | 21 (1/15/5) | 9/11/1 | L03, L05, L09, L11, L19 | **3** |
| S08 | Relativsätze: Nominativ, Akkusativ, Dativ | Muss | RELATIVSATZ | 21 (2/13/6) | 3/18/0 | L10 | **3** |
| S09 | Relativsätze fortgeschritten: mit Präposition, dessen/deren, was/wo- | Soll | RELATIVSATZ | 8 (0/6/2) | 2/6/0 | L10 | **3** |
| S10 | Infinitivsätze: zu, um … zu, ohne … zu, (an)statt … zu | Muss | INFINITIV_ZU | 16 (3/12/1) | 4/12/0 | L24 | **3** |
| S11 | Indirekte Fragesätze (ob / W-Wort) | Soll | querliegend (11 Knoten) | 19 (2/15/2) | 11/5/3 | L07, L12, L14, L23 | **3** |
| S12 | Mittelfeld: TeKaMoLo und Pronomenstellung | Soll | querliegend (2 Knoten) | 7 (1/4/2) | 2/5/0 | L03 | **3** |

### Nominalgruppe und Kasus

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| N01 | Artikel, Genus, Plural | Muss | ARTIKEL_GENUS | 23 (13/8/2) | 10/13/0 | L13 | **3** |
| N02 | Kasus nach Verben (Akkusativ-/Dativobjekt, Dativverben) | Muss | KASUS_VERBOBJEKTE | 16 (4/10/2) | 3/13/0 | L10, L17 | **3** |
| N03 | Präpositionen mit festem Kasus | Muss | KASUS_PRAEPOSITIONEN | 18 (7/10/1) | 3/15/0 | L05 | **3** |
| N04 | Wechselpräpositionen (wo? / wohin?) | Muss | WECHSELPRAEPOSITIONEN | 19 (5/12/2) | 4/15/0 | L06 | **3** |
| N05 | Adjektivdeklination nach bestimmtem/unbestimmtem/Nullartikel | Muss | ADJEKTIVDEKLINATION | 26 (1/17/8) | 5/20/1 | L16 | **3** |
| N06 | Adjektivdeklination nach Zahl- und Mengenwörtern (viele, einige, mehrere, andere) | Soll | querliegend (17 Knoten) | 40 (1/24/15) | 9/28/3 | L13, L16 | **3** |
| N07 | Genitiv: Nomen und Präpositionen mit Genitiv | Soll | GENITIV | 14 (0/11/3) | 4/10/0 | L19 | **3** |
| N08 | n-Deklination (der Kollege – dem Kollegen, der Kunde, der Name) | Soll | querliegend (20 Knoten) | 35 (9/21/5) | 11/23/1 | L13, L19 | **3** |
| N09 | Komparativ und Superlativ (auch attributiv, so … wie / als) | Soll | querliegend (7 Knoten) | 12 (0/9/3) | 5/7/0 | L16 | **3** |
| N10 | Reflexive Verben (Akkusativ/Dativ, reziprok) | Soll | REFLEXIVE_VERBEN | 16 (2/12/2) | 3/13/0 | L17 | **3** |
| N11 | „es“ als Korrelat und Platzhalter (Es ist wichtig, dass …) | Kann | querliegend (1 Knoten) | 6 (0/3/3) | 2/4/0 | L14 | **3** |

### Nominalstil und Attribute

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NS1 | Nominalisierung ↔ Verbalisierung (Nebensatz ↔ Präpositionalausdruck) | Soll | NOMINALISIERUNG | 15 (1/0/14) | 4/11/0 | L11 | **3** |
| NS2 | Nomen-Verb-Verbindungen / Funktionsverbgefüge (zur Verfügung stellen, in Frage stellen) | Soll | WS NOMEN_VERB_VERBINDUNGEN | 24 (0/2/22) | 0/24/0 | L11 | **3** |
| NS3 | Partizipialattribute: Partizip I/II als Adjektiv, erweitertes Attribut ↔ Relativsatz | Soll | PARTIZIPIALATTRIBUT | 13 (0/0/13) | 3/10/0 | L21 | **3** |

### Präpositionen

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| PR1 | Verben, Nomen und Adjektive mit fester Präposition | Muss | VERB_PRAEPOSITION | 30 (1/25/4) | 4/25/1 | L15 | **3** |
| PR2 | Präpositionaladverbien (darauf, worauf) und da-/wo- + dass-Satz | Muss | querliegend (27 Knoten) | 39 (4/23/12) | 19/18/2 | L12, L15 | **3** |

### Wortbildung

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| W01 | Wortbildung: Präfixe und Suffixe (-ung, -heit, -keit, un-, -los, -bar, ver-/be-/ent-) | Soll | WS WORTBILDUNG | 26 (0/20/6) | 7/19/0 | L03, L13, L18 | **3** |
| W02 | Komposita und Wortfamilien | Kann | querliegend (3 Knoten) | 10 (1/6/3) | 4/6/0 | L13, L18 | **3** |

### Orthografie und Zeichensetzung

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| O01 | Kommaregeln (Nebensatz, Infinitivsatz mit zu, Aufzählung, Relativsatz) | Muss | querliegend (4 Knoten) | 11 (3/5/3) | 5/6/0 | L14, L24 | **3** |
| O02 | Groß-/Kleinschreibung inkl. Nominalisierung (das Wichtigste, beim Arbeiten) | Soll | querliegend (3 Knoten) | 9 (4/2/3) | 2/7/0 | L11 | **3** |
| O03 | das / dass unterscheiden | Soll | querliegend (5 Knoten) | 11 (2/6/3) | 6/5/0 | L14, L21, L22 | **3** |
| O04 | ß/ss-Schreibung und Umlaute | Kann | querliegend (1 Knoten) | 7 (1/5/1) | 5/2/0 | L18 | **3** |

### Negation

| # | Stelle | Prio | Knoten | Übungen (B1.2/B2.1/B2.2) | rez/prod/frei | Erklärung | Note |
| --- | --- | --- | --- | --- | --- | --- | --- |
| NG1 | Negation: nicht/kein, Stellung von nicht, nicht … sondern | Muss | NEGATION | 14 (5/7/2) | 6/8/0 | L17 | **3** |

## 4. Lücken (sortiert nach Dringlichkeit)

Keine – jede Stelle hat Note 3.

## 5. Wie tief ist die Abdeckung wirklich?

Note 3 ist eine **Anwesenheitsschwelle**: erklärt, sechsmal geübt, davon einmal produktiv und
einmal auf B2.2. Sie sagt nichts darüber, ob sechs Übungen zum Beherrschen reichen. Die
folgenden Stellen stehen genau auf dieser Schwelle und sind die ersten Kandidaten für Nachschub
(„Drills“ = alle Übungen außer freier Produktion):

| # | Stelle | Prio | Drills | davon B2.2 |
| --- | --- | --- | ---: | ---: |
| N11 | „es“ als Korrelat und Platzhalter (Es ist wichtig, dass …) | Kann | 6 | 3 |
| V04 | Futur II: Vermutung über Vergangenes | Kann | 6 | 4 |
| O04 | ß/ss-Schreibung und Umlaute | Kann | 7 | 1 |
| S12 | Mittelfeld: TeKaMoLo und Pronomenstellung | Soll | 7 | 2 |
| V11 | Perfekt der Modalverben und doppelter Infinitiv (hat … machen müssen) | Soll | 7 | 4 |
| P02 | Zustandspassiv (sein + Partizip II) | Soll | 8 | 3 |
| S09 | Relativsätze fortgeschritten: mit Präposition, dessen/deren, was/wo- | Soll | 8 | 2 |
| V02 | Plusquamperfekt und Vorzeitigkeit (nachdem) | Muss | 8 | 2 |
| V08 | Irrealer Vergleich (als ob) und irrealer Wunsch (wenn … nur/doch) | Soll | 8 | 5 |
| O02 | Groß-/Kleinschreibung inkl. Nominalisierung (das Wichtigste, beim Arbe | Soll | 9 | 3 |
| V03 | Futur I: Zukunft und Vermutung (wohl) | Soll | 10 | 4 |
| V06 | Konjunktiv II Vergangenheit (hätte/wäre + Partizip II) | Muss | 10 | 5 |

## 6. Qualitative Einschätzung

Dieser Abschnitt wird von Hand gepflegt (`tools/grammar-coverage-notes.md`) und vom Skript ans
Ende des Berichts gehängt. Stand: 03.09.2026.

**Niveau.** Die Übungen zu den Kernthemen sind überwiegend echt B2: die Transformationen zu
Konjunktiv II, Passiv, Nominalstil, Partizipialattribut und indirekter Rede verlangen wirkliche
Umbauten und nicht das Einsetzen einer Endung. Ein Etikettenproblem gibt es trotzdem – unter den
85 B2.2-Items der Bänke A–C finden sich einzelne Aufgaben, die eher B2.1 sind, etwa `gr.vf.106`
(Präteritum von „steigen“), `gr.adj.104` („alle neuen Regeln“) und `gr.gen.104` („während der
Sitzung“). Umgekehrt sind die schwersten Stellen sauber einsortiert: Nominalisierung,
Nomen-Verb-Verbindungen und indirekte Rede liegen fast vollständig auf B2.2. Bei Orthografie ist
B2.2 naturgemäß eine Frage des Satzmaterials, nicht der Regel – dort trägt das Band weniger
Aussage als anderswo.

**Serbische Kontraste.** Alle 24 Grammatiktabellen wurden gelesen, 96 Beispielzeilen. Falsche
Analogien – der eigentliche Schadensfall – sind keine darunter, und die riskanten Stellen sind
korrekt gelöst: „da li → ob, nicht dass“ (L14), „zbog + genitiv = wegen + Genitiv“ als echte
Übereinstimmung (L05), „se-Passiv → werden“ (L04) und „koji“ mit getrenntem Genus und Kasus (L10).
Drei Kleinigkeiten bleiben: „dostiže biciklom“ (L10) ist unidiomatisch – besser „do kojeg se stiže
biciklom“; die Zeile „an die Verwaltung schreiben“ (L06) wird als wohin?-Fall etikettiert, obwohl
es eine feste Präposition ist; und in L17 bleibt der lohnende Kontrast „mich stört“ (Akkusativ)
gegen „smeta mi“ (Dativ) ungenutzt. Nur die zweite Stelle ist eine Vereinfachung, die später
Rückfragen provoziert.

**Produktion.** Hier liegt die deutlichste Schwäche. Von 60 Schreib- und Sprechaufgaben verlangt
die Rubrik in 12 den Konjunktiv II, in 10 Konnektoren und in 7 das Passiv – Relativsätze, Genitiv,
indirekte Rede und Passiversatz fordert keine einzige ein. Die Strukturen werden also geübt, aber
selten unter Produktionsdruck abverlangt, und genau dorthin schaut der Prüfer (Kriterien
„Korrektheit“ und „Repertoire“). Die Drills tragen bis zur sicheren Wiedererkennung; der Sprung
zur freien Verwendung bleibt weitgehend dem KI-Tutor überlassen, der auf dieser Maschine mangels
API-Schlüssel nicht läuft.

**Zu den 100 %.** Dass alle drei Kennzahlen nach der Nacharbeit auf 100 % stehen, heißt nicht
„fertig“: Note 3 ist eine Anwesenheitsschwelle (Abschnitt 5), und ein Dutzend Stellen erreicht sie
mit sechs bis neun Drills. Wer die Abdeckung weiter verbessern will, erhöht nicht die Zahl der
Themen, sondern die Zahl der Aufgaben an den dünnen Stellen – und die Zahl der Rubriken, die eine
Struktur wirklich verlangen.

