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

pa na telefonu otvori `http://<IP-adresa-računara>:5178` (IP vidiš sa `ipconfig`). Za govor i na telefonu koristi Chrome.

## 2. Prvi dan: Einstufung

Na početnoj strani klikni **Einstufung starten**: 28 kratkih zadataka iz 14 ključnih tema, oko 15–20 minuta, prvo lakši prolaz kroz sve teme, pa teži. Odgovaraj iskreno, „Weiß ich nicht" je legitiman odgovor – cilj je da sistem zna gde stojiš, ne da skupiš poene. Ako prekineš pre 70 %, ne računa se; nastavi kasnije preko **Weitermachen**.

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

Tutor ocenjuje pisanje i govor, igra partnera u diskusiji i generiše zadatke. Ključevi se nikad ne čuvaju u aplikaciji ni u repou.

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

- Aplikacija se ne pokreće: proveri `dotnet --version` (treba 10.x) i da port 5178 nije zauzet (`--urls http://localhost:5311`).
- Mikrofon ne radi: samo Chrome/Edge; dozvoli mikrofon u pregledaču; možeš i da kucaš transkript.
- Nema zvuka pri diktatu: pregledač bez nemačkog glasa – Windows: Einstellungen → Zeit und Sprache → Sprache → Deutsch dodati.
- Log servera vidiš u konzoli u kojoj si pokrenuo `dotnet run`.
