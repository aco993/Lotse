# Lotse – uputstvo za upotrebu

Lotse je tvoj lični, adaptivni trener za nemački do nivoa B2 (Goethe-Zertifikat B2 kao referentni format; telc B2 je praktično isto). Aplikacija radi lokalno na tvom računaru, svi podaci ostaju kod tebe.

## 1. Instalacija i pokretanje

Potrebno: .NET 10 SDK (https://dotnet.microsoft.com/download). Za prepoznavanje govora Chrome ili Edge.

```bash
cd Lotse
dotnet run --project src/Lotse.Web
```

Otvori http://localhost:5178 (port piše u konzoli). Baza `src/Lotse.Web/data/lotse.db` nastaje pri prvom startu.

**Sa telefona (u istoj Wi-Fi mreži):**

```bash
dotnet run --project src/Lotse.Web --launch-profile lan
```

pa na telefonu otvori `http://<IP-adresa-računara>:5178` (IP vidiš sa `ipconfig`). Za govor i na telefonu koristi Chrome. U Chrome meniju „Zum Startbildschirm hinzufügen" – aplikacija se instalira kao PWA sa svojom ikonom, donjom navigacijom i bez adresne trake.

## 1a. Nalog

Prvi ekran koji vidiš je **Registrieren** – unesi email i lozinku (min. 8 karaktera), nema potvrde mejlom, odmah si ulogovan/a. Svaki sledeći nalog (npr. na drugom uređaju, ili neko drugi ko proba app) dobija potpuno svoj, prazan napredak – ništa se ne meša sa tvojim. Podaci ostaju samo na ovom računaru (lokalna SQLite baza), nikuda se ne šalju.

**Passkey umesto lozinke:** u Konto (klik na svoj email u meniju) → Passkeys → „Passkey hinzufügen“ – otisak prsta, lice ili PIN uređaja. Posle toga na login strani samo email + „Mit Passkey anmelden“. Radi na `localhost` ili preko HTTPS-a; sa telefona preko IP-adrese u lokalnoj mreži (http://192.168…) browser to ne dozvoljava – tu ostaje lozinka.

Zaboravljena lozinka: **Passwort vergessen?** na login strani. Kako na ovoj mašini nije podešen mejl-server, link za reset se ne šalje na mejl nego ispisuje u konzoli gde `dotnet run` radi – otvori terminal i potraži ga posle klika na „Link anfordern".

„Alle Lerndaten löschen" (u Einstellungen) briše SAMO tvoj napredak (sesije, greške, savladanost tema) – ne i tvoj nalog, ne i tuđe naloge, ne Tutor-podešavanja (ključ ostaje) i ne zajedničku banku vežbi koje je Tutor generisao (nju dele svi nalozi na ovom računaru).

## 1b. Šta je novo u 0.8.0 (posle testa sa 10 persona)

- **Einstufung:** puna (28 zadataka, ~20 min) ili **kratka** (14, ~8 min). „Beenden“ pita: *Pausieren* (stanje ostaje, nastavljaš sa „Einstufung fortsetzen“ na Heute), *Abschließen* ili *Weiterüben*. Dnevne sesije možeš raditi i dok je Einstufung otvoren.
- **Pisanje bez API-ključa:** posle „Abgeben und selbst prüfen“ app sama pronalazi tipične greške (haben/sein u Perfektu, glagol na kraju posle *weil/dass*, zarez, ä/ö/ü/ß, padež posle predloga, n-deklinacija, član, glagol + predlog, registar, „Mit freundlichen Grüßen“, dužina, veznici) i upisuje ih u Fehlerjournal. Šta pravila ne vide (sadržaj, struktura, izbor reči) – ostaje Selbstcheck.
- **Nacrt je siguran:** tekst se čuva u browseru dok pišeš; posle reload-a se vraća. Predat tekst čeka Selbstcheck i posle reload-a.
- **Themen:** legenda boja sada filtrira po oblasti (klik = filter, drugi klik = sve).
- **Ponavljanja po FSRS-u:** intervali prate krivu zaborava (stavka dolazi kad verovatnoća sećanja padne na 90 %); ništa ne moraš da menjaš.
- **Tastatura:** vidljiv fokus, „Zum Inhalt springen“ na prvi Tab, posle „Prüfen“ fokus je na „Weiter“ (Enter nastavlja).
- **Fond reči:** 11 novih tematskih paketa (Arbeit, Gesundheit/Pflege, Umwelt, Medien, Bildung, Geld, Wohnen/Mobilität, Charakter/Gefühle, Präfixverben, IT-Beruf, C1-gehoben).

## 1c. Prirodan nemački glas (preporučeno, jednom po računaru)

Bez ovog koraka app čita nemački glasovima koje nudi sam pregledač. Na Windowsu su to stare Hedda/Katja/Stefan i zvuče osetno robotski. Jedna komanda to menja:

```powershell
pwsh -File tools/install-piper.ps1
```

Skript preuzima **Piper** (MIT, ~21 MB) i dva nemačka glasa u `%LOCALAPPDATA%\Lotse` (~120 MB ukupno):

| Glas | Uloga |
|---|---|
| `de_DE-thorsten-medium` | podrazumevani i muški — Herr Krüger, Jonas, Tarek, tvoje replike |
| `de_DE-kerstin-low` | ženski — Sabine, Lena, Ana, Frau Kaya, Prüferin |

Sve radi **offline**: nema ključa, nema interneta, nijedna rečenica ne napušta računar. Rečenica se izgovori za oko pola sekunde i posle toga je keširana, pa je ponovno slušanje trenutno.

Ništa ne moraš da podešavaš — app sama pronađe glasove i pri startu u konzoli ispiše `Lokale Sprachausgabe aktiv: ...`. Ako preskočiš ovaj korak, sve i dalje radi, samo sa starim glasovima pregledača. Skript smeš da pokreneš više puta; preskače ono što već postoji.

> Glasovi **nisu** deo repozitorijuma (preveliki su), pa ovaj korak ponavljaš na svakom novom računaru posle `git clone`.

## 1d. Zielniveau: B2 ili C1

U **Einstellungen**, pored termina ispita, biraš cilj:

- **B2 – Prüfungsvorbereitung** (podrazumevano): težišta ostaju na B2.2 i ispod. Najkraći put do ispita.
- **C1 – darüber hinaus**: težišta smeju da idu do C1, pa u dnevne sesije ulaze i `C1 gehoben` (biran rečnik) i B2.2-gramatika.

Šta se **ne** menja: Einstufung (ona ionako meri granicu B1/B2), težina zadataka u odnosu na tvoj izmereni nivo, i strana **Prüfung B2** — ona ostaje ispit po Goethe-B2 obrascu i to ti izričito piše kad je cilj C1. Ova app nema C1-ispit i ne pretvara se da ga ima.

Kad je cilj C1, na strani Heute uz „B2-Bereitschaft" stoji i druga linija **C1-Nähe** — koliko ti sedi gradivo iznad B2. To nije druga „spremnost za ispit", nego mera savladanosti; dok ima malo podataka piše „(Schätzung)".

## 1e. Branche (oblast u kojoj radiš)

U **Einstellungen** pored polja „Beruf" (slobodan tekst, koji ide tutoru kao kontekst) sada biraš i **Branche**: IT/Software, Pflege, Bau/Handwerk, Medizin, Büro/Verwaltung, Handel/Verkauf — ili ništa.

Efekat je namerno mali i objašnjiv:

- par rečničkih tema dobije blagu prednost (Pflege/Medizin → `Gesundheit & Körper`, Bau → `Wohnen & Mobilität` i `Arbeit & Karriere`, IT → `IT & Software` …),
- kad su dva zadatka podjednako prikladna, pobeđuje onaj iz tvoje oblasti,
- kad je to odlučilo, u obrazloženju zadatka piše zašto: *„Vorgezogen, weil du in der Pflege arbeitest."*

Šta se **ne** dešava: tvoje slabosti i dalje odlučuju šta se vežba. Ako ti je pasiv slab, dobijaš pasiv — bez obzira na branšu. Branša nikad ne sakriva gradivo i ne pravi ti „ugodnu zonu".

> **Priča u kursu ostaje ista.** 24 lekcije prate posao u softverskoj firmi — to je autorova priča i namerno je IT-obojena. Branša utiče na *vežbe i rečnik*, ne na lekcije.

Bez izbora sve radi tačno kao i pre.

## 1f. Tvoje ime u kursu

U **Einstellungen** unosiš **Vorname** i **Nachname**. Kurs onda oslovljava tebe: mejl od Herr Krügera glasi „Sehr geehrter Herr *tvoje prezime*", a potpisi u uzornim rešenjima nose tvoje ime.

Ako polja ostaviš prazna, ostaje autorovo ime (Aleksandar Micić) — ništa se ne kvari.

Tehnički: u sadržaju stoje `{Vorname}`, `{Nachname}` i `{Name}`, a ime se ubacuje tek kad se lekcija ili zadatak predaju **tebi**. Zajednička baza zadataka ostaje neutralna, pa se imena ne mešaju između naloga na istom računaru.

## 1g. Nedeljni cilj i tri broja o pamćenju

U **Einstellungen** biraš **Wochenziel** (30–300 minuta nedeljno, podrazumevano 60). Na strani **Heute** stoji traka: „48 von 60 Minuten diese Woche". Nedelja počinje ponedeljkom, broje se samo završene sesije.

**Ponedeljkom** iznad toga dobiješ dvoredni **Wochenrückblick** o prethodnoj nedelji: koliko zadataka, koliki procenat tačno, koliko reči „sedi" i koja ti je tema bila najteža. Bez tutora i bez interneta — sve se računa iz tvojih tabela.

Na strani **Fortschritt** su tri kartice iz FSRS-stanja:

| Kartica | Šta znači |
|---|---|
| **Wörter, die sitzen** | stavke čija stabilnost pamćenja prelazi 21 dan |
| **Fällig in den nächsten 7 Tagen** | koliko ponavljanja stiže po danima; sve zaostalo se broji na „danas" da se ne sakrije |
| **Behalten** | koliki deo zakazanog gradiva bi upravo sada pogodio iz prve |

Nema poena, bedževa ni rang-liste — ovo je alat za jednog čoveka, pa nema koga da prestigneš (obrazloženje u `docs/KONZEPT.md` §5).

> Sitnica radi poštenja: piše „davon diese Woche wiederholt", a ne „novo ove nedelje". Istorija starih stabilnosti se ne čuva, pa se ne može znati kada je tačno neka stavka prešla granicu od 21 dana — a izmišljen precizan broj je gori od dosadnog tačnog.

## 1h. Kuda ide „B2-Bereitschaft"

Pored procenta spremnosti na strani **Heute** stoji i strelica: **▲ 3 % seit letzter Woche**, ▼ ako je palo, ili „unverändert".

App svaki dan kad otvoriš Heute zapiše jedno očitavanje i poredi ga sa onim od pre otprilike nedelju dana (6–8 dana). Dok takvog zapisa nema, linija se **ne prikazuje** — bolje ništa nego izmišljen trend.

## 2. Prvi dan: Einstufung

Na početnoj strani klikni **Einstufung starten**: 28 kratkih zadataka iz 14 ključnih tema, oko 15–20 minuta, prvo lakši prolaz kroz sve teme, pa teži. Odgovaraj iskreno, „Weiß ich nicht" je legitiman odgovor – cilj je da sistem zna gde stojiš, ne da skupiš poene. Ako prekineš pre 70 %, ne računa se; nastavi kasnije preko **Weitermachen**.

## 2a. Kurs: 24 lekcije sa pričom (dva dela)

Pored dnevnih sesija postoji **Kurs** (meni → Kurs): 24 lekcije u dva dela koje prate tvoje dve godine u firmi Nordlicht GmbH u Bremenu.

- **Teil 1 (L01–L12, B1+ → B2):** prvi dan u timu, prvi mejl šefici, stand-up, ljut klijent, Bürgeramt, stan i komšije, lekar, pad produkcionog sistema, debata o radu od kuće, vikend, razgovor o plati, dan ispita.
- **Teil 2 (L13–L24, B2):** uvodiš novog kolegu Tareka (članovi i rod), roditeljski sastanak u vrtiću (zavisne rečenice), organizuješ Betriebsausflug (glagoli s predlozima), prezentuješ klijentu (deklinacija prideva), rešavaš sukob s Lenom (povratni glagoli, negacija), večernji kurs na VHS (lažni prijatelji, tvorba reči), osporavaš Nebenkostenabrechnung (genitiv), glasine o fuziji (subjektivni modalni glagoli), interna prijava za vođu tima (participi kao pridevi), slušanje vesti (indirektni govor), pa dva ispitna dana: Lesen i Schreiben – i položen B2.

Svaka lekcija ima:
- **Situaciju kao dijalog** u kom biraš svoje replike; svaka od tri opcije dobija objašnjenje zašto je (ne)prikladna. Tu je srž lekcije. Dugme „Gespräch anhören" čita ceo dijalog – muški i ženski likovi različitim glasom, ako si uradio korak 1c.
- **Objašnjenje gramatike** sa tabelom nemački ↔ srpski i zvučnikom uz svaki primer.
- **10–11 interaktivnih koraka**: razgovor, „Fehler finden" (klikni pogrešnu reč u kolegin mejl), spajanje parova, praznine, prevodi.
- **Završni zadatak**: pišeš ili govoriš sam.
- **Merksatz** – jedna rečenica koju nosiš sa sobom.

Redosled prati težinu; početna strana uvek predlaže sledeću lekciju. Lekcije možeš ponavljati, napredak i ocena se pamte.

## 3. Svaki dan: jedno dugme

Na strani **Heute** Lotse već zna šta ti treba i predlaže sesiju (npr. „Schwerpunkt Passiv · 2 Wiederholungen · eine Sprechaufgabe"). Klikni **10 Minuten starten** (ili 5/15/30). Redosled u sesiji je namerno takav:

1. **Wiedervorlage** – teme koje su bile slabe i „oporavile se" proverava ponovo posle 7, 21 i 60 dana.
2. **Wiederholung** – zadaci koji su po rasporedu ponavljanja dospeli.
3. **Schreiben / Sprechen** – jedan produktivni zadatak, naizmenično.
4. **Schwerpunkt** – vežbe na trenutno najslabijim temama, malo iznad tvog nivoa.
5. **Lesen / Hören** – kad ima vremena.

Svaki zadatak nosi razlog zašto je izabran. Tastatura: **Enter** proverava i ide dalje, cifre **1–4** biraju odgovor kod višestrukog izbora.

Kako se ocenjuje: velika/mala slova, ä→ae, ß→ss, jedna slovna greška u dugoj reči i vokabular bez člana računaju se kao „fast richtig" – ali svaki takav propust se beleži, jer baš to ispitivači gledaju.

## 4. Pisanje i govor

- **Schreiben**: kratki zadaci (5 min) za posao i svakodnevicu, plus ispitni formati Teil 1 (forum, 150 reči) i Teil 2 (formalna poruka, 100 reči). Bez API ključa: predaš tekst i sam se oceniš po rubrici uz uzorno rešenje. Sa ključem: Claude ocenjuje po ispitnim kriterijumima, označava greške kodom i prepisuje tvoje rečenice na B2.
- **Sprechen**: klikni mikrofon, govori, transkript se pojavljuje i možeš ga ispraviti pre slanja. Vortrag (Teil 1) ima standardne četiri tačke; Diskussion (Teil 2) sa ključem ima KI-partnera koji zastupa suprotnu poziciju.
- **Prüfung B2**: format ispita sa savetima po delovima, prognoza po modulima i simulacije.

## 5. Praćenje

- **Fortschritt**: savladanost po temi (bledo = malo podataka), aktivnost 30 dana, oznaka za zakazane Wiedervorlage.
- **Fehlerjournal**: svaka greška sa kodom, iz vežbi i iz ocena tutora. Ono što se gomila ide u sledeće sesije.
- **Themen**: kad hoćeš baš određenu temu (pre sastanka, na primer) – **Fokus-Session**. Sa ključem: **Auffüllen** puni banku za 3 najslabije teme; kod tema čitanja/slušanja generiše nove tekstove u ispitnom formatu.

## 6. KI-tutor (opciono): Claude ili bilo koji drugi model

Tutor ocenjuje pisanje i govor, igra partnera u diskusiji i generiše zadatke.

**Najlakše: u samoj aplikaciji.** *Einstellungen → KI-Tutor*: izaberi provajdera iz liste (Groq je preporučen: besplatan i brz), nalepi ključ, klikni **Verbindung testen** (vidiš latenciju i odgovor), pa **Speichern & aktivieren**. Ključ se čuva šifrovano na tvom računaru (ASP.NET Data Protection) i ne ide nigde osim ka provajderu. Promena važi odmah, bez restarta. Ako nešto ne štima, poruka ti kaže tačno šta (npr. „Schlüssel abgelehnt (401)", „Modell nicht gefunden – ollama pull").

Groq ključ: https://console.groq.com/keys (besplatan nalog, model `llama-3.3-70b-versatile`).

Ispod su alternative preko okruženja/konfiguracije, za one koji to više vole.

**A) Claude (najbolji kvalitet, plaća se po potrošnji, oko 4 centa po oceni teksta sa Opus, petina sa Sonnet):**

```bash
setx ANTHROPIC_API_KEY "sk-ant-..."
```

ili `dotnet user-secrets set "Lotse:Tutor:ApiKey" "sk-ant-..."` u folderu `src/Lotse.Web`. Model u `appsettings.json` → `Lotse:Tutor:Model` (`claude-opus-5` ili jeftiniji `claude-sonnet-5`).

**B) Bilo koji OpenAI-kompatibilni model.** Provajder `OpenAi` radi sa svakim serverom koji govori `chat/completions`:

| Gde | BaseUrl | Ključ | Napomena |
|---|---|---|---|
| Ollama (lokalno, besplatno) | `http://localhost:11434/v1` | ne treba | `ollama pull qwen2.5:7b`; na CPU sporo (minuti po oceni), sa GPU brzo |
| LM Studio (lokalno) | `http://localhost:1234/v1` | ne treba | učitaj model u LM Studio, uključi server |
| OpenRouter (cloud, ima besplatne modele) | `https://openrouter.ai/api/v1` | `OPENAI_API_KEY` | modeli sa `:free` sufiksom, npr. `meta-llama/llama-3.3-70b-instruct:free` |
| Groq (cloud, besplatna kvota, vrlo brzo) | `https://api.groq.com/openai/v1` | `OPENAI_API_KEY` | npr. `llama-3.3-70b-versatile` |
| Mistral / DeepSeek / OpenAI | njihov `/v1` URL | `OPENAI_API_KEY` | |

Primer pokretanja sa Ollamom:

```bash
ollama pull qwen2.5:7b
dotnet run --project src/Lotse.Web -- --Lotse:Tutor:Provider=OpenAi --Lotse:Tutor:BaseUrl=http://localhost:11434/v1 --Lotse:Tutor:Model=qwen2.5:7b
```

Trajno: iste vrednosti u `src/Lotse.Web/appsettings.Local.json` (fajl je u `.gitignore`), primer je `appsettings.Ollama.json`. Ključ za cloud provajdere: `setx OPENAI_API_KEY "..."`.

Iskreno o kvalitetu: modeli od 3B parametara greše u nemačkoj gramatici i ponekad izmišljaju kodove grešaka (nepoznati kodovi se odbacuju, ne kvare tvoj model). Izmereno na ovom računaru bez GPU-a: `llama3.2` (3B) daje ocenu za 8–9 minuta i ocenio je tekst sa očiglednim greškama sa 100 % – tehnički radi, pedagoški ne vredi. Od 7B (qwen2.5, gemma3, mistral) ocena je upotrebljiva; 70B preko Groq/OpenRouter je blizu Claude Sonnet-u. Za ispitnu pripremu Claude ostaje merilo.

## 7. Podešavanja i reset

**Einstellungen**: ime (za uzorna rešenja), zanimanje (kontekst za tutora), dnevni minuti, datum ispita (početna strana onda odbrojava). „Alle Lerndaten löschen" briše samo tvoj napredak, sadržaj ostaje.

## 8. Uz Claude Chat

Za razgovor, dubinske ispravke i coaching koristi prompt iz `docs/CLAUDE_CHAT_PROMPT.md` u Claude Projektu. Greške koje tamo dobiješ vežbaj ovde kao Fokus-Session; „häufigste Fehler" odavde zalepi u chat.

## 9. Ako nešto ne radi

- Prvo: *Einstellungen → Zustand der App → Prüfen*. Vidiš stanje sadržaja, baze i tutora; **Reparieren** sam sređuje šta može (tabele, integritet, zaostale sesije, neispravne generisane zadatke) i kaže šta je uradio. Isto maštinski: http://localhost:5178/health.
- Ako se stranica „sruši", aplikacija pokazuje prijateljsku poruku sa dugmetom „Noch einmal" – tvoj napredak je već sačuvan.
- Aplikacija se ne pokreće: proveri `dotnet --version` (treba 10.x) i da port 5178 nije zauzet (`--urls http://localhost:5311`).
- Mikrofon ne radi: samo Chrome/Edge; dozvoli mikrofon u pregledaču; možeš i da kucaš transkript.
- Zvuk robotski: nisi pokrenuo `tools/install-piper.ps1` (vidi 1c). U konzoli piše koji je glas aktivan – `Lokale Sprachausgabe aktiv: ...` znači da Piper radi, `nicht eingerichtet` znači da app koristi glasove pregledača.
- Nema zvuka uopšte: bez Pipera trebaju ti glasovi pregledača – Windows: Einstellungen → Zeit und Sprache → Sprache → Deutsch dodati. Sa Piperom to nije potrebno.
- Svi likovi zvuče isto: nedostaje ženski glas `de_DE-kerstin-low`; pokreni skript iz 1c ponovo.
- Log servera vidiš u konzoli u kojoj si pokrenuo `dotnet run`.
