# Prompt: Audit gramatičke pokrivenosti za Goethe-Zertifikat B2

Nalepi sve ispod crte u novu sesiju (Claude Code, Opus). Radni direktorijum: `C:\Users\a.micic\Documents\dev AI\Lotse`.

---

Ti si revizor sadržaja aplikacije **Lotse**, adaptivne Blazor aplikacije za učenje nemačkog do nivoa B2 (korisnik: Srbin, C#/.NET programer u Bremenu, cilj: položiti **Goethe-Zertifikat B2**, telc B2 kao rezerva). Tvoj zadatak je da **izmeriš koliko posto gramatike potrebne za B2 aplikacija pokriva**, gde su rupe, i da rupe zatvoriš tamo gde je to razumno. Ne prepravljaj arhitekturu, ne diraj UI osim ako je neophodno.

## 1. Šta aplikacija ima (pročitaj pre bilo kakvog zaključka)

- `content/taxonomy.json` – 48 čvorova veština (`GR.*` gramatika, `WS.*` vokabular, `RM.*` Redemittel, `LS.*`/`HV.*` čitanje/slušanje, `SC.*`/`SP.*` pisanje/govor) i 54 koda grešaka. Svaki čvor ima `difficulty` po CEFR-pojasu (B1_2, B2_1, B2_2).
- `content/exercises/*.json` – banka od ~750 vežbi; svaka vežba nosi `nodeId`, `band`, `type` (Cloze, MultipleChoice, Transform, Translate, Reading, Dialogue, SpotError, Match, produkcija SW/SP …).
- `content/lessons/01..24-*.json` – kurs od 24 lekcija u dve celine (prva i druga godina u firmi Nordlicht GmbH). Svaka lekcija ima `grammar.nodeId` (tema koja se objašnjava sa nemačko-srpskom tabelom), `steps` (ID-jevi vežbi), `nodeIds`.
- `src/Lotse.Core/Model/ContentCatalog.cs`, `ExerciseValidator` u `src/Lotse.Core/Model/Exercise.cs`, `src/Lotse.Infrastructure/Content/ContentLoader.cs` – kako se sadržaj učitava i validira.
- `tests/Lotse.Core.Tests/LessonTests.cs` i `ContentAndPlannerTests.cs` – pravila koja svaki sadržaj mora da zadovolji (npr. svaka lekcija ≥ 8 koraka, Dialogue+Match+SpotError, 3 opcije + 3 feedback-a po replici učenika, svaki gramatički čvor objašnjen u lekciji, svaka seed-vrednost prolazi kroz `AnswerChecker`).
- `docs/KONZEPT.md`, `docs/PLAN.md`, `CHANGELOG.md` – namera i stanje. **Stanje uzimaj iz koda, ne iz dokumentacije.**

## 2. Referentni inventar B2 gramatike (napravi ga sam, eksplicitno)

Goethe ne objavljuje zvaničnu listu gramatike za B2. Sastavi referentnu listu iz tri izvora i navedi ih: (a) *Profile Deutsch* / Goethe „Sprachliche Mittel" za B2, (b) gramatički inventari standardnih B2-udžbenika (Aspekte neu B2, Sicher! B2, Erkundungen B2 – ono što im je zajedničko), (c) ono što se stvarno ocenjuje u Schreiben/Sprechen rubrikama (Korrektheit, Repertoire). Očekujem 35–50 stavki, grupisanih (Verb/Tempus/Modus, Satzbau/Konnektoren, Nominalgruppe/Kasus, Nominalstil/Passiv-Ersatz, Präpositionen, Wortbildung, Orthografie/Zeichensetzung). Za svaku stavku označi težinu: **Muss** (bez toga nema B2), **Soll** (očekuje se u dobrom rezultatu), **Kann** (bonus).

Primeri koje lista mora da sadrži (nije potpuna): Konjunktiv II (Gegenwart, Vergangenheit, irreale Bedingung/Vergleich/Wunsch), Passiv (Vorgang/Zustand, mit Modalverben, Passiversatz: sich lassen, -bar, sein + zu), Konjunktiv I / indirekte Rede, Modalverben subjektiv, Nomen-Verb-Verbindungen, Nominalisierung ↔ Verbalisierung, Partizipialattribute (I/II, erweitert), Relativsätze (auch mit Präposition, was/wo, Genitiv-Relativpronomen), zweiteilige Konnektoren (nicht nur … sondern auch, je … desto, zwar … aber, entweder … oder, weder … noch, sowohl … als auch), Konnektoren mit Präpositionen (wegen/trotz/während/infolge/aufgrund ↔ weil/obwohl/während/sodass), temporale Nebensätze (nachdem, bevor, seit(dem), bis, sobald, während), Plusquamperfekt, Futur I/II (auch Vermutung), Verben mit Präposition + Präpositionaladverbien, Adjektivdeklination inkl. nach Nullartikel/Zahlwörtern, Genitiv (auch Präpositionen), n-Deklination, Negation (nicht/kein, Position, nicht … sondern), Wortstellung im Mittelfeld (TeKaMoLo, Pronomen), Infinitivsätze (um/ohne/statt … zu), Wortbildung (Suffixe/Präfixe/Komposita), Kommaregeln.

## 3. Merenje – budi kvantitativan i proverljiv

Napiši skriptu (Node ili Python, smesti je u `tools/grammar-coverage.*` i ostavi je u repou) koja iz `taxonomy.json`, `exercises/*.json` i `lessons/*.json` izračuna za svaku stavku tvoje referentne liste:

1. **Mapiranje**: koji `GR.*` čvor(ovi) je pokrivaju (jedna stavka može biti deo čvora – npr. Passiversatz je deo `GR.PASSIV`; onda gledaj sadržaj vežbi, ne samo ime čvora – pretraži `prompt`/`answers`/`explanation` po ključnim rečima kao *lässt sich*, *-bar*, *sein + zu*).
2. **Broj vežbi** po pojasu (B1_2 / B2_1 / B2_2) i po tipu (receptivno: MultipleChoice/Match/SpotError; produktivno: Cloze/Transform/Translate; slobodna produkcija: SW/SP).
3. **Da li postoji objašnjenje** (lekcija sa `grammar.nodeId` na tom čvoru) i u kojoj lekciji.
4. **Ocena pokrivenosti** stavke: 0 (ništa), 1 (pomenuto/≤3 vežbe), 2 (≥4 vežbe, ali bez objašnjenja ili bez produktivnog tipa), 3 (objašnjenje + ≥6 vežbi + bar jedan produktivni tip + bar jedna B2_2 vežba).

Izvesti u `docs/GRAMMATIK_ABDECKUNG.md`:
- Tabela stavka → Muss/Soll/Kann → čvor(ovi) → broj vežbi (po pojasu) → objašnjenje (lekcija) → ocena 0–3.
- **Tri procenta**, jasno definisana: (a) % stavki sa ocenom ≥ 2, (b) % **Muss**-stavki sa ocenom 3, (c) ponderisani skor (Muss ×3, Soll ×2, Kann ×1). Kaži koji od njih smatraš relevantnim za „može li da položi" i zašto.
- Lista rupa, sortirana: Muss sa ocenom 0–1 prvo.
- Kvalitativni deo (max. 15 rečenica): da li su vežbe za ključne teme stvarno na B2 nivou ili su B1 sa B2 etiketom; da li su srpski kontrasti tačni (proveri nasumično 20 tabela u lekcijama – lažne analogije su gore od nikakvih); da li Schreiben/Sprechen zadaci traže te strukture.

## 4. Zatvaranje rupa – ali disciplinovano

Za svaku **Muss**-stavku sa ocenom 0–1 i **Soll** sa ocenom 0:
- Ako čvor ne postoji, dodaj ga u `taxonomy.json` (isti oblik kao postojeći, sa `difficulty` po pojasu i srpskom napomenom `l1Note` ako polje postoji – proveri model `SkillNode` u `src/Lotse.Core/Model`).
- Dodaj **najmanje 6 vežbi po stavci**, mešavina tipova, u novu datoteku `content/exercises/grammatik-d.json` (ne diraj postojeće datoteke). Svaka vežba mora proći `ExerciseValidator` i seed-odgovor mora proći `AnswerChecker` (testovi to proveravaju). Bez engleskog, bez šablonskih rečenica – kontekst je posao u IT firmi, svakodnevica u Bremenu, ispit.
- **Ne dodaj nove lekcije** (kurs je namerno 24). Ako stavki nedostaje objašnjenje, dopuni `grammar.text`/`examples` najbliže postojeće lekcije, kratko i kontrastivno (srpski ↔ nemački).
- Vežbe koje tvrde da su B2_2 moraju to i biti (nominalni stil, indirektni govor, erweiterte Partizipialattribute, zweiteilige Konnektoren u složenim rečenicama).

Nakon izmena ponovo pokreni skriptu i u izveštaj upiši **pre/posle** procente.

## 5. Tehnička pravila (obavezno)

- Pre `dotnet build` zaustavi Preview-server `lotse` (port 5310), inače DLL-lock.
- Testove pokreći sa `dotnet exec tests/Lotse.Core.Tests/bin/Debug/net10.0/Lotse.Core.Tests.dll` i isto za `Lotse.Web.Tests` (`dotnet test -v q` lažno javlja „keine Tests"). Svih 111 + tvoji novi moraju biti zeleni; dodaj test koji čuva da nijedna **Muss**-stavka nema ocenu < 2 (uzmi listu iz skripte ili je ugradi u test kao konstantu).
- Svaki novi ID vežbe mora biti jedinstven u celoj banci; proveri skriptom pre builda.
- Ne stavljaj API ključeve nigde; nema ključa na mašini, tutor nije potreban za ovaj zadatak.
- Git: commituj slobodno na lokalnoj grani, poruka na nemačkom, završna linija `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>` zameni svojim modelom. Ne pushuj.
- Ažuriraj `CHANGELOG.md` (nova verzija 0.6.1) i jednu rečenicu u `README.md` o meri pokrivenosti.

## 6. Šta mi vratiš na kraju

Kratak izveštaj na srpskom: tri procenta pre/posle, top-10 rupa koje su bile i šta si sa njima uradio, šta si svesno ostavio otvoreno i zašto, i tvoja iskrena procena u jednoj rečenici: *da li neko ko prođe sav ovaj materijal ima gramatiku dovoljnu za Goethe B2 sa ≥ 70 %, i šta je jedina najveća preostala slabost.*
