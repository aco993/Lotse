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

- `src/Lotse.Web/data/` (tvoja baza sa napretkom) – već u `.gitignore`.
- API ključ – nikad u `appsettings.json`; `ApiKey` tamo ostaje `null`.
- `appsettings.Local.json` ako ga napraviš – u `.gitignore`.

## Kako se projekat predstavlja (za CV / razgovor)

- **Domen**: adaptivni sistem učenja sa merljivim modelom učenika (Rasch/Elo sposobnost po temi), spaced repetition, planer sesija sa objašnjivim odlukama, ciklus ponovne provere.
- **Arhitektura**: čist `Core` bez zavisnosti (testiran u milisekundama), `Infrastructure` (EF Core/SQLite, sadržaj, Claude), `Web` (Blazor Server + MudBlazor). Vidi `docs/ARCHITEKTUR.md`.
- **AI integracija**: strukturisan JSON izlaz preko šeme, katalog kodova grešaka koji deterministički deo i AI deo dele, graceful fallback bez ključa.
- **Kvalitet**: 74 testa uključujući integracione nad pravom SQLite bazom i testove integriteta sadržaja (svaki od 749 zadataka mora da prođe proveru).

## Sledeće što bi impresioniralo

- Playwright smoke test dnevne petlje (start → odgovor → kraj) u CI.
- EF Core migracije umesto `EnsureCreated`.
- Release workflow koji pravi self-contained `win-x64` ZIP pri tagu `v*`.
