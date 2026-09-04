# Nutzertest mit zehn Personas – September 2026

Zehn simulierte Lernende (je ein eigener Agent mit Persona, Konto und Auftrag) haben die App am 4. September 2026
im Browser bedient – registriert, die Einstufung gemacht, Sessions gespielt, Texte geschrieben, Aufnahmen versucht,
Themen, Fortschritt, Fehlerjournal, Einstellungen, Handy- und Dunkelansicht geprüft – und nach denselben neun
Kriterien bewertet. Werkzeug: `tools/persona-harness` (Playwright, echtes Chromium, headless).

## Ergebnis

| Persona | Profil | Note | Kern der Kritik |
|---|---|---|---|
| Marko | Backend-Entwickler, B1+, Ziel B2 | 8 | Absturz beim Klick auf das Konto; Entwurf beim Neuladen weg; Bereitschaft „bewegt sich nicht“ |
| Milica | Marketing, spricht gut, schreibt schwach | 8 | Ohne Schlüssel keine Korrektur der Mails; Fokus-Session war keine |
| Jelena | Krankenschwester, nur Handy | 7 | Kein Pflege-Wortschatz; Einstufung blockiert 5-Minuten-Sessions; Legende sieht aus wie Filter |
| Dragan | Bauingenieur, skeptisch | 7 | „Session läuft noch“ nach fertiger Session; Übung geht beim Schließen verloren |
| Nikola | Pflege-Umschüler, A2+/B1, abends | 7 | Fachjargon auf der Startseite; Einstufung zu lang; Datenverlust bei Reload |
| Petar | Arzt, Statistik-Fan, will C1 | 7 | Reset „löscht nicht“ (Fehlbedienung des Skripts, geprüft); kein C1; unerklärte 45 % |
| Ivana | sehbehindert, Tastatur | 7 | Kein Fokusring, kein Skip-Link, keine Überschriften, Fokus fällt nach „Prüfen“ auf `body` |
| Ana | Studentin, B2, will C1 | 7 | Nur Selbstcheck ohne Schlüssel; nichts als C1 ausgewiesen; Name im Muster fest verdrahtet |
| Stefan | DaF-Lehrer, B2-Prüfer | 6,5 | Inhalt fehlerfrei (80 Übungen gesichtet), aber doppelte Einstufungs-Sessions |
| Tamara | Au-pair, 19, Handy, Dunkelmodus | 6 | Einstufung zu lang, Fortschritt trocken, keine Belohnung, „Beenden“ ohne Rückfrage |

Durchschnitt 7,0. Einhellig gelobt: die serbisch-kontrastiven Erklärungen, das Fehlerjournal mit „Deine Form → richtig“,
die Story-Lektionen. Fachlich: kein einziger Sprachfehler im Inhalt (Stefan), die Zahlen sind bis ins Detail konsistent (Petar).

## Was daraus wurde (0.8.0)

| Befund | Personas | Umsetzung |
|---|---|---|
| Doppelte/„offene“ Sessions nach Abschluss | Dragan, Milica, Stefan | Starts sind idempotent (offene Session wird fortgesetzt, nie dupliziert); Abschluss der Einstufung schließt alle offenen Einstufungen |
| Absturz über den Konto-Link | Marko | Statische Seiten verlassen den Circuit per Vollnavigation (`Routes.razor`), E2E-Test |
| Entwurf/Selbstcheck beim Neuladen weg | Marko, Nikola, Ana | Entwurf im Browser gesichert, abgegebener Text kommt aus der Datenbank zurück, eigener Text neben der Musterlösung |
| Ohne Schlüssel keine Korrektur | 8 von 10 | Regelbasierter Textanalysator (`FreeTextAnalyzer`): Perfekt haben/sein, Verbstellung, Komma, Umlaut/ß, Kasus nach Präposition, n-Deklination, Artikel, Verb + Präposition, Register, Grußformel, Länge, Kohärenz – Fehler landen im Journal und im Lernermodell |
| Einstufung zu lang, blockiert kurze Sessions | Jelena, Nikola, Tamara | Kurze Einstufung (14 Aufgaben), Pausieren mit Rückfrage, Sessions neben offener Einstufung möglich |
| „Beenden“ ohne Rückfrage | Tamara | Dialog Pausieren / Abschließen / Weiterüben |
| Tastatur/Screenreader | Ivana | Fokusring für alle Bedienelemente, Skip-Link, Fokus nach „Prüfen“ auf „Weiter“, Live-Region für das Urteil |
| Legende sieht aus wie Filter | Jelena | Legende filtert jetzt wirklich |
| Fokus-Session ohne Fokus | Milica | Eigener Planer-Zweig: nur das Wunschthema, Wiederholungen des Themas zuerst |
| Fehlerjournal zeigt „1“ statt der Antwort | Marko | Optionstext statt Index |
| 45 % ohne Erklärung | Petar, Dragan, Marko | Kennzahl trägt ihre Sicherheit („Schätzung – noch wenig Daten“) |
| Zu wenig Wortschatz, kein C1, nichts für Pflege | Jelena, Ana, Petar | Elf neue Wortschatzpakete (≈1.200 Einträge), darunter Gesundheit/Pflege und ein C1-Paket; Ziel-Niveau C1 im Planer bleibt offen (siehe PLAN.md) |
| Falscher Konsolenfehler bei Passkey-Autofill | Ivana | Autofill-Absage wird still behandelt |
| „1 Minuten“ | Stefan | Singular |

Erledigt in 0.9.0:

- **Ziel-Niveau C1 als Planer-Modus** (Petar, Ana) – Einstellung neben dem Prüfungstermin, hebt die Bandgrenze der
  Schwerpunkte auf C1, „C1-Nähe" auf Heute; die Prüfungsseite bleibt ausdrücklich B2.
- **Beruf als Profilmerkmal** (Jelena, Dragan, Petar) – Branche im Profil, +0,15 für die passenden Wortschatzknoten
  und ein Stichentscheid unter gleich geeigneten Aufgaben, im Begründungstext benannt. Jelenas Pflege-Wortschatz
  kommt damit von selbst dran.

- **Vorname im Profil statt des festen Namens** (Ana) – der Kurs trägt jetzt Tokens; wer seinen Namen einträgt, wird
  im Dialog und in Musterlösungen so angesprochen. Leere Felder behalten den Namen des Autors.

Offen (bewusst): Gamification jenseits von Streak und Zielen (Tamara, Ana).

## Methode, kurz

Eine Persona ist ein Prompt: Herkunft, Niveau, Alltag, Gerät, Ungeduld, typische Fehler – und der Auftrag, alles aus
dieser Sicht zu bewerten, nur Gesehenes zu berichten, Screenshots anzusehen. Neun Kriterien 1–10, Top-3-Lob, Top-5-Kritik,
Bugs mit Repro, Zahlungsbereitschaft, Gesamtnote. Die Personas lügen nicht, aber sie irren wie Menschen: Petars „Reset
löscht nicht“ war ein Skriptfehler (der Reset löscht, geprüft mit `verify-fixes.mjs`) – deshalb wird jeder Befund vor der
Umsetzung im Code oder mit einem Skript nachvollzogen.
