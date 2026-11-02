# Deploying Lotse

Lotse runs fine on one machine with `dotnet run`. There are two reasons to put it on a server instead:

1. **Practice on the phone.** The quick three-minute session ("3 min" on Heute, or the "3 Minuten" shortcut on the installed app's icon) is meant for the bus stop. That only works if the phone can reach the app.
2. **A public portfolio demo.** Visitors click "Demo ansehen" on the login page and land in a shared account that already has two weeks of history.

Both use the same image and differ only in configuration.

## The image

```bash
docker build -t lotse .
docker compose up -d
```

This is the same app as `dotnet run`. The SQLite database, the Data Protection key ring and the speech cache live in the `/data` volume, and the container runs as the image's non-root user. Migrations apply on start, so a new version needs only `docker compose up -d --build`. CI builds the image on every push and checks three things against a fresh volume: it starts, `/health` answers, and the demo button is on the login page.

There is no Piper voice in the image, so speech uses the browser's own voices. Chrome on Android has good German voices; on iOS, Safari's are acceptable.

## HTTPS is not optional

Passkeys and the microphone (speech recognition in Sprechen) only work on `https://` or on `localhost`. Put a TLS-terminating proxy in front of the container. Caddy obtains the certificate by itself:

```
lotse.example.com {
    reverse_proxy 127.0.0.1:8080
}
```

`Lotse__Hosting__BehindProxy=true` (the default in `compose.yaml`) makes the app trust `X-Forwarded-Proto` and `X-Forwarded-For`. Without it, cookies and redirects would think the connection is plain http. The compose file binds the app to `127.0.0.1`, so nothing but the proxy on the same host can reach it. That is the condition under which trusting forwarded headers is safe.

## Configuration

| Setting (environment variable) | Default | Meaning |
|---|---|---|
| `Lotse__Hosting__RegistrationOpen` | `true` | On a personal server: register your account, then set this to `false` and restart. Nobody else can create an account afterwards. |
| `Lotse__Hosting__BehindProxy` | `false` (compose: `true`) | Honour the proxy's forwarded headers. |
| `Lotse__Hosting__Demo__Enabled` | `false` | Adds a "Demo ansehen" button and a shared demo account. |
| `Lotse__Hosting__Demo__ResetHours` | `24` | How often the demo account is emptied and refilled. |
| `GROQ_API_KEY`, `ANTHROPIC_API_KEY`, … | none | A provider key as the default for every account without its own. Leave it unset on a public instance unless you accept that every visitor spends it. |

## The demo account

Registration stays closed, and the demo account signs in without a password. It is filled through the real services, not hand-written rows, so it holds what a real learner would produce:

- fourteen days of ten-minute sessions, with accuracy rising from 50 % to 80 % and a skipped day now and then
- six telc parts and one email
- telc set as the exam, with the exam date eight weeks out

Everyone who clicks "Demo ansehen" shares it. For that reason the account pages (password, e-mail, passkeys, deletion) and the tutor settings are closed to it: a key stored there could be spent by the next visitor.

With no provider key on the server, the demo shows everything that works without AI: the hint ladder, the rule-based writing check, the telc parts, the plan and the forecast. The AI features say plainly that no tutor is configured.

## Data and privacy

On a server your learning data is on that server. It stays out of the image and the logs. Back it up by copying the volume:

```bash
docker run --rm -v lotse_lotse-data:/data -v "$PWD":/backup alpine tar czf /backup/lotse-data.tgz -C /data .
```

On Linux the Data Protection key ring that encrypts stored API keys is not itself encrypted. The framework says so at start; on Windows, DPAPI protects it. Anyone who can read the volume can read stored keys. Protect the volume like a password store, or keep keys in environment variables instead.
