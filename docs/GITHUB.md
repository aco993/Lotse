# Objavljivanje na GitHub

Repozitorijum je spreman za javno objavljivanje: MIT licenca, README na engleskom, CI (build + testovi + provera sadržaja), `.gitignore` koji izostavlja bazu, `bin/obj` i tajne. API ključ se nikad ne nalazi u repou (env varijabla ili user-secrets).

## Koraci

1. Na GitHubu napravi prazan repozitorijum `Lotse` (bez README, bez licence – oba već postoje lokalno).
2. U folderu projekta:

```bash
git remote add origin https://github.com/<tvoj-nalog>/Lotse.git
git push -u origin main
```

3. Otvori karticu **Actions**: workflow `CI` se pokreće sam pri svakom push-u i PR-u (Ubuntu, .NET 10, `dotnet test`).
4. U README zameni `<this repo>` u sekciji Quick start pravim URL-om.
5. Opciono, za portfolio: dodaj 3 screenshot-a u `docs/img/` (Heute, Session, Fortschritt) i uključi ih u README tabelu „Screens"; dodaj GitHub „Topics": `dotnet`, `blazor`, `language-learning`, `spaced-repetition`, `adaptive-learning`, `claude`.

## Šta NE ide na GitHub

- `src/Lotse.Web/data/` (tvoja baza sa napretkom, plus keš izgovorenog zvuka) – već u `.gitignore`.
- API ključ – nikad u `appsettings.json`; `ApiKey` tamo ostaje `null`.
- `appsettings.Local.json` ako ga napraviš – u `.gitignore`.
- Piper i nemački glasovi (~140 MB) – oni žive u `%LOCALAPPDATA%\Lotse`, van repozitorijuma. U repou je samo skript koji ih preuzme.

## Posle `git clone` na novom računaru

```bash
dotnet run --project src/Lotse.Web        # radi odmah, sa glasovima pregledača
pwsh -File tools/install-piper.ps1        # jednom, za prirodan nemački izgovor (vidi UPUTSTVO 1c)
```

Drugi korak je opcion i CI ga ne izvršava: na GitHub Actions (Ubuntu) Pipera nema, pa se ti testovi sami prijave kao preskočeni (`Assert.SkipWhen`), a aplikacija koristi glasove pregledača. Zato push nikad ne pada zbog zvuka.

## Kako se projekat predstavlja (za CV / razgovor)

- **Domen**: adaptivni sistem učenja sa merljivim modelom učenika (Rasch/Elo sposobnost po temi), spaced repetition, planer sesija sa objašnjivim odlukama, ciklus ponovne provere.
- **Arhitektura**: čist `Core` bez zavisnosti (testiran u milisekundama), `Infrastructure` (EF Core/SQLite, sadržaj, Claude), `Web` (Blazor Server + MudBlazor). Vidi `docs/ARCHITEKTUR.md`.
- **AI integracija**: strukturisan JSON izlaz preko šeme, katalog kodova grešaka koji deterministički deo i AI deo dele, graceful fallback bez ključa.
- **Kvalitet**: 198 testova (motor, sadržaj, komponente u bUnit-u, host u procesu) plus Playwright E2E nad pravom aplikacijom na Kestrel-u; testovi integriteta sadržaja proveravaju svaki od 2.048 zadataka. Format-gate (`dotnet format --verify-no-changes`) i `TreatWarningsAsErrors` u CI.

## Sledeće što bi impresioniralo

- Release workflow koji pravi self-contained `win-x64` ZIP pri tagu `v*`.
- Screenshot-i u README (Heute, Session, Fortschritt).
