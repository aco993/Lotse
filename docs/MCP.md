# Lotse over MCP

You already talk to Claude or ChatGPT about German. Without Lotse, that chat starts from zero every time: it does not know your exam date, which mistakes keep coming back, or what you practised yesterday, and whatever it corrects is gone when the window closes.

`tools/Lotse.Mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server that connects the two. The chat reads your learning state from Lotse and writes two things back: exercises for your weak topics and the mistakes it found. Practice still happens in the app, where answers are checked, scheduled and counted. The chat becomes a client of the app instead of a replacement for it.

## What the assistant can do

| Tool | Reads or writes | What it is for |
|---|---|---|
| `get_learner_status` | reads | Exam and date, streak, due reviews, weekly minutes, readiness per module, weakest topics with their typical mistakes and the Serbian interference note, telc points per part against the pass marks |
| `get_error_patterns` | reads | The error journal grouped by error type, with the three newest examples of each, so the chat explains a rule with your own sentences |
| `list_topics` | reads | Topic ids and error codes, which the two write tools need |
| `add_exercises` | writes | Up to 10 closed exercises (cloze, transform, translate, multiple choice, word order) for one topic. They join the bank and come up in your sessions |
| `record_errors` | writes | Mistakes from a text you wrote in the chat, so the next sessions aim at them |

There is also a prompt, `lotse_coach`. It runs a short practice conversation: status, the weakest area (or a focus you name), five tasks with hints before answers, and at the end the mistakes and a few exercises written back into Lotse.

## Guardrails

These are the same rules the app applies to its own AI tutor.

- **Writes are checked, not trusted.**
  - An exercise goes through the same `ExerciseValidator` as the tutor's.
  - Only closed types are accepted, so the app can mark the answer itself.
  - A cloze needs its `___` gap.
  - A prompt that already exists in the topic is refused.
  - An error needs a code from the catalogue and the learner's actual words.
  - Every rejection comes back with its reason, so the model can fix it.
- **Nothing changes scores.** The chat cannot submit answers, change reviews, settings or the profile. Mastery, readiness and the schedule come only from practice in the app.
- **Hints before answers.** The server instructions tell the assistant to give the topic, then the rule, then the first letters, and the full solution only after a real attempt. That is the hint ladder the app uses. A field experiment with almost a thousand students found that free access to a solution-giving assistant lowered later exam results. A tutor that gave hints instead of answers largely avoided that harm (Bastani et al., PNAS 2025).
- **"Not measured" stays visible.** A readiness value or telc score without enough evidence is left out of the answer, and the assistant is told to say so, not to estimate.
- **No key, no cost on this side.** The server never calls a language model. Your chat client is the model. Lotse's own tutor key is not read.
- **One learner per process, locally.** The server speaks stdio, so it runs on your machine and is started by your own chat client. It serves the account given with `--user`, or the only account if there is exactly one.

## Setup

Build once:

```bash
dotnet build tools/Lotse.Mcp
```

Run Lotse at least once beforehand. The web app creates and migrates the database. The MCP server refuses to start on a database that is missing migrations instead of migrating it behind the app's back.

### Claude Code

```bash
claude mcp add lotse -- dotnet run --project /path/to/Lotse/tools/Lotse.Mcp --no-build -- --user you@example.com
```

### Claude Desktop

Add this to `claude_desktop_config.json` (Settings → Developer → Edit Config):

```json
{
  "mcpServers": {
    "lotse": {
      "command": "C:\\path\\to\\Lotse\\tools\\Lotse.Mcp\\bin\\Debug\\net10.0\\Lotse.Mcp.exe",
      "args": ["--user", "you@example.com"]
    }
  }
}
```

On macOS and Linux the binary has no `.exe` extension.

### Options

| Option | Default | Meaning |
|---|---|---|
| `--user` | the only account | E-mail of the Lotse account to serve |
| `--data` | `src/Lotse.Web/data` of this checkout | Folder that holds `lotse.db` |
| `--content` | `content/` of this checkout | Content folder, if you keep it elsewhere |

The defaults come from where the binary was built, not from the working directory. Claude Desktop starts servers from its own install folder.

## How it fits together

`ChatBridge` (`src/Lotse.Infrastructure/Services/ChatBridge.cs`) holds the logic: what the chat may read, and the checks on what it writes. It has its own tests against a real SQLite database (`ChatBridgeTests`). The MCP project is a thin layer over it: tool descriptions written for the model, argument errors passed through as `McpException` so the model sees the reason, and startup checks.

The web app and the MCP server are two processes on one SQLite file. When the chat adds exercises, the web app notices on the next session start: one `COUNT` and one `MAX` on the generated exercises (`ContentCatalogProvider.RefreshIfChangedAsync`), and a full reload only when they changed.

## Not included, and why

- **Remote (HTTP) transport, for ChatGPT or claude.ai on the phone.** A remote server needs a public endpoint and OAuth in front of personal learning data. That is a hosting decision. Lotse is not hosted publicly yet ([DEPLOY.md](DEPLOY.md)).
- **Starting or answering sessions from the chat.** Then the chat would be the practice venue again, without the checking, timing and scheduling that make the practice count.
