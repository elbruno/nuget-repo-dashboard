# repo-identity — Cross-Device Install Guide

This guide covers everything you need to set up the terminal identity experience on a device — including external dependencies, what changes on your machine, and how to stay in sync across multiple devices.

## External Dependencies

| Dependency | Required | Install |
|------------|----------|---------|
| [.NET 8+ SDK](https://dotnet.microsoft.com/download) | Yes | [dotnet.microsoft.com](https://dotnet.microsoft.com/download) |
| [Oh My Posh](https://ohmyposh.dev/) v3+ | Yes | See below |
| [PowerShell 7+](https://github.com/PowerShell/PowerShell) | Yes | `winget install Microsoft.PowerShell` |
| [Windows Terminal](https://aka.ms/terminal) | Recommended | `winget install Microsoft.WindowsTerminal` |
| Git | Yes | (likely already installed) |

### Installing Oh My Posh

**Windows:**
```powershell
winget install JanDeDobbeleer.OhMyPosh -s winget
```

**macOS:**
```bash
brew install jandedobbeleer/oh-my-posh/oh-my-posh
```

**Linux:**
```bash
curl -s https://ohmyposh.dev/install.sh | bash -s
```

Verify the install: `oh-my-posh --version`

## Cross-Device Bootstrap

The full setup on any new device is two commands:

```bash
git clone https://github.com/elbruno/nuget-repo-dashboard
cd nuget-repo-dashboard
dotnet run --project src/RepoIdentity --framework net8.0 -- install
```

That's it. The generated profiles are already committed to the repo — deterministic colors mean the same repo always gets the same color on every device.

## What `install` Changes on Your Machine

The `install` command makes exactly these changes — nothing more:

| What | Where | Notes |
|------|-------|-------|
| Oh My Posh profile `.json` files | `~/.poshthemes/*.json` | One per tracked repo. Overwritten on re-install. |
| Profile manifest | `~/.poshthemes/index.json` | Used by Set-RepoTheme.ps1 for repo lookup. |
| Auto-detection script | `~/.poshthemes/Set-RepoTheme.ps1` | Detects current repo and loads its profile. |
| PowerShell profile | `~/Documents/PowerShell/Microsoft.PowerShell_profile.ps1` | Appended once (idempotent — never duplicated). |

### The `$PROFILE` snippet

This is exactly what gets appended to your `$PROFILE`:

```powershell
# repo-identity: auto-detect terminal theme from current git repo
$repoIdentityScript = Join-Path $HOME ".poshthemes/Set-RepoTheme.ps1"
if (Test-Path $repoIdentityScript) { . $repoIdentityScript }
```

The snippet is a no-op if `Set-RepoTheme.ps1` doesn't exist — safe to leave even if you later uninstall.

## Keeping In Sync Across Devices

The dashboard's GitHub Actions workflow regenerates profiles daily (when `refresh-metrics.yml` runs). On any device:

```bash
git pull
dotnet run --project src/RepoIdentity --framework net8.0 -- install
```

Re-running `install` is **idempotent**:
- Profiles are overwritten (always get the latest)
- `$PROFILE` snippet is only appended once (never duplicated)

## How to Use `--dry-run`

Preview every action without touching anything:

```bash
dotnet run --project src/RepoIdentity --framework net8.0 -- install --dry-run
```

Example output:
```
Checking oh-my-posh...  ✅ oh-my-posh 23.x.x found
Copying profiles to ~/.poshthemes...
  [dry-run] Copy elbruno-ElBruno.ModelContextProtocol.json → ~/.poshthemes/...
  [dry-run] Copy index.json → ~/.poshthemes/index.json
  [dry-run] Copy Set-RepoTheme.ps1 → ~/.poshthemes/Set-RepoTheme.ps1
Patching $PROFILE...
  [dry-run] Append snippet to ~/Documents/PowerShell/Microsoft.PowerShell_profile.ps1

[dry-run] No changes made. Remove --dry-run to apply.
```

## Customizing a Repo's Identity

Create a `repo.identity.json` file in the root of any tracked repo to override its generated defaults:

```json
{
  "accentColor": "#FF6B6B",
  "icon": "🎯",
  "type": "library"
}
```

Fields:
| Field | Type | Description |
|-------|------|-------------|
| `accentColor` | hex string | Override the auto-generated color (e.g. `"#FF6B6B"`) |
| `icon` | emoji string | Override the auto-selected icon |
| `type` | string | Informational label (`"library"`, `"tool"`, `"demo"`, etc.) |

After editing, re-run `generate` then `install` to apply.

## Uninstalling

To remove repo-identity from a device:

1. Remove profile files:
   ```powershell
   Remove-Item ~/.poshthemes/elbruno-*.json
   Remove-Item ~/.poshthemes/index.json
   Remove-Item ~/.poshthemes/Set-RepoTheme.ps1
   ```

2. Remove the `$PROFILE` snippet (open `$PROFILE` in an editor, delete the 3-line block starting with `# repo-identity:`).

3. Restart your terminal.

## CLI Reference

```
repo-identity install [options]

Options:
  --profiles <dir>    Source directory with generated profiles
                      (default: ./terminal/ohmyposh)
  --target <dir>      Destination directory for profiles
                      (default: ~/.poshthemes)
  --skip-prereqs      Skip oh-my-posh availability check
  --dry-run           Print all actions without executing
  -?, -h, --help      Show help
```

For the full auto-detection script reference and troubleshooting, see the next section.

## How Set-RepoTheme.ps1 Works

See: [Auto-Detection Script](./repo-identity-install.md#set-repothemeps1-walkthrough)
*(This section is populated after Phase 8 completes.)*

## CI Auto-Regeneration

The `.github/workflows/refresh-metrics.yml` workflow regenerates profiles automatically after each daily metrics refresh. Profiles committed to the repo are always up to date — a `git pull` is all you need to get the latest.

See: [CI Configuration](./repo-identity-install.md#ci-auto-regeneration-1)
*(This section is populated after Phase 9 completes.)*
