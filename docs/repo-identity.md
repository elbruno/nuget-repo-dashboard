# repo-identity — Terminal Profile Generator

Generates [Oh My Posh](https://ohmyposh.dev/) terminal profiles for each repository tracked by this dashboard. Each profile gives your terminal a unique color and icon so you instantly know which repo context you're working in.

## How it works

1. Reads `data/latest/data.repositories.json` (populated daily by GitHub Actions)
2. Assigns each repo a deterministic accent color (SHA256-based — same repo always same color)
3. Selects an icon from the repo name's purpose keywords, falling back to the language icon
4. Generates one Oh My Posh `.json` profile per repo in `terminal/ohmyposh/`
5. Writes `terminal/ohmyposh/index.json` — the manifest used by the activation script

## Generated profile structure

Each profile is a complete Oh My Posh config:

```json
{
  "$schema": "https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/schema.json",
  "version": 2,
  "console_title_template": " 🔌 nuget-mcp-server ",
  "blocks": [
    {
      "type": "prompt",
      "alignment": "left",
      "segments": [
        {
          "type": "text",
          "foreground": "#FFFFFF",
          "background": "#6B4CA8",
          "style": "plain",
          "template": " 🔌 nuget-mcp-server "
        }
      ]
    }
  ]
}
```

Key fields:
- **`console_title_template`** — sets the Windows Terminal tab title automatically
- **`background`** — the repo's unique accent color (solid, not transparent)
- **`foreground`** — auto-selected for contrast (white on dark, dark on light)

## Purpose-based icon mapping

Icons are selected by scanning the repo name for keywords (first match wins):

| Keywords | Icon | Example repos |
|----------|------|---------------|
| `whisper` | 🎙️ | nuget-whisper-net |
| `tts`, `speech`, `speak`, `voice` | 🔊 | elbruno-tts-azure |
| `embed`, `embedding`, `semantic`, `rag` | 🧠 | semantic-memory |
| `qr`, `qrcode`, `barcode` | 📷 | qrcode-generator |
| `mcp`, `modelcontext` | 🔌 | nuget-mcp-server |
| `realtime`, `streaming` | ⚡ | realtime-api |
| `vision`, `image`, `img` | 🖼️ | vision-demo |
| `llm`, `gpt`, `claude`, `openai` | 🤖 | llm-helper |
| `agent`, `agentic`, `copilot` | 🤖 | copilot-agent |
| `nuget`, `package`, `sdk` | 📦 | (NuGet libraries) |
| `dashboard`, `metrics` | 📊 | (monitoring tools) |
| `api`, `rest`, `http` | 🌐 | (API projects) |
| *(fallback: language)* | 🔷 🐍 🟦 | C# / Python / TypeScript |
| *(fallback: unknown)* | 📦 | (any other) |

## Color generation

Colors are derived from `SHA256("{owner}/{repo}:{language}")` — deterministic and portable. The same repo always gets the same color on any device. Post-processing ensures no two repos have visually identical colors (minimum Euclidean RGB distance of 60).

## CLI commands

```bash
# Regenerate all profiles from current data
dotnet run --project src/RepoIdentity -- generate

# Preview what would be generated (no files written)
dotnet run --project src/RepoIdentity -- preview

# Copy profiles to ~/.poshthemes/ for use
dotnet run --project src/RepoIdentity -- apply
```

See `docs/repo-identity-install.md` for the full cross-device setup guide.
