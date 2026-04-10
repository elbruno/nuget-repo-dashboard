# NuGet + GitHub Dashboard

A public dashboard that tracks NuGet packages and GitHub repos — collecting download counts, versions, stars, issues, PRs, and more.

🌐 **Live dashboard:** [https://elbruno.github.io/nuget-repo-dashboard/](https://elbruno.github.io/nuget-repo-dashboard/)

## Architecture

The dashboard uses a **two-pipeline architecture**:

### Pipeline 1: Discovery
Automatically discovers NuGet packages and their associated GitHub repositories:

1. **Query NuGet Search API** — search for packages by owner (`owner:elbruno`)
2. **Build package list** — deduplicate and filter discovered packages
3. **Extract GitHub repos** — parse `projectUrl` from package metadata to build deduplicated repo list

### Pipeline 2: Collection
Collects metrics from NuGet and GitHub APIs using the discovered lists:

1. **Collect NuGet metrics** — fetch download counts, versions, and metadata → `data.nuget.json`
2. **Collect GitHub metrics** — fetch stars, forks, issues, PRs → `data.repositories.json`

```
config/dashboard-config.json   ← NuGet profile + ignore list
config/tracked-packages.json   ← optional manual package mappings
         ↓
   src/Collector/              ← .NET 10 console app (two-pipeline architecture)
         ↓
   data/latest/                ← latest snapshots
   ├── data.nuget.json         ← NuGet package metrics
   └── data.repositories.json  ← GitHub repository metrics
         ↓
   data/history/YYYY/MM/DD/    ← daily history
```

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Build

```bash
dotnet build src/Collector/Collector.csproj
```

### Run

```bash
dotnet run --project src/Collector/Collector.csproj
```

### Environment Variables

| Variable | Required | Description |
|---|---|---|
| `NUGET_PROFILE` | No | Override the NuGet profile username from `dashboard-config.json` |
| `GITHUB_TOKEN` | No | GitHub personal access token for higher rate limits |
| `DASHBOARD_REPO_ROOT` | No | Override the repo root path (useful in CI) |

### GitHub Token Setup

The `GITHUB_TOKEN` can be provided via **.NET User Secrets** (recommended for local development) or environment variables.

#### Option 1: .NET User Secrets (recommended for local dev)

```bash
cd src/Collector
dotnet user-secrets set "GITHUB_TOKEN" "ghp_your_token_here"
```

User Secrets are stored outside the repo in your OS user profile, so they are never committed to source control.

#### Option 2: Environment Variable

```bash
# Linux / macOS
export GITHUB_TOKEN="ghp_your_token_here"

# Windows PowerShell
$env:GITHUB_TOKEN = "ghp_your_token_here"
```

> **Note:** If both User Secrets and an environment variable are set, the environment variable takes precedence.

### NuGet Profile Override

The NuGet profile defaults to `"elbruno"` in `config/dashboard-config.json`. You can override it via **.NET User Secrets** or an **environment variable** without editing the config file.

**Precedence:** Environment Variable > User Secrets > config file default.

#### Option 1: .NET User Secrets

```bash
cd src/Collector
dotnet user-secrets set "NUGET_PROFILE" "someuser"
```

#### Option 2: Environment Variable

```bash
# Linux / macOS
export NUGET_PROFILE="someuser"

# Windows PowerShell
$env:NUGET_PROFILE = "someuser"
```

The Collector will show which source is active at startup:

```
  [1/3] Loading config...
    Profile: someuser (source: environment variable)
```

> **⚠️ Single Profile Design:** This dashboard is designed and optimized for tracking a single NuGet user profile. In the future we may rebuild to support multiple profiles, but currently it targets one NuGet user at a time.

## Configuration

### Dashboard Config (`config/dashboard-config.json`)

Primary configuration for auto-discovery:

```json
{
  "nugetProfile": "elbruno",
  "mergeWithTrackedPackages": true,
  "ignorePackages": [
    "LocalEmbeddings",
    "Microsoft.Extensions.AI"
  ]
}
```

**Fields:**
- `nugetProfile` — NuGet username for auto-discovery (queries `owner:{username}`)
- `mergeWithTrackedPackages` — merge manual package mappings from `tracked-packages.json`
- `ignorePackages` — exclude specific packages from collection (case-insensitive)

### Tracked Packages (`config/tracked-packages.json`)

Optional manual package-to-repo mappings (merged with discovered packages):

```json
[
  { "packageId": "Microsoft.Extensions.AI", "repos": ["dotnet/extensions"] }
]
```

## Output

The collector writes two JSON files:

### `data.nuget.json`
NuGet package metrics with download counts, versions, and metadata.

### `data.repositories.json`
GitHub repository metrics with stars, forks, issues, PRs, and additional metadata.

Both files are written to:
- **`data/latest/`** — latest metrics snapshot
- **`data/history/{YYYY}/{MM}/{DD}/`** — daily historical snapshots

## Dashboard

The dashboard is automatically deployed to **GitHub Pages** after each metrics refresh. The [`Refresh Metrics`](.github/workflows/refresh-metrics.yml) workflow collects data and then deploys `site/index.html` with the latest JSON data.

### Enabling GitHub Pages

1. Go to **Settings → Pages** in your repository
2. Under **Build and deployment**, set **Source** to **GitHub Actions**
3. The next workflow run will deploy the dashboard automatically

### Dashboard URL

Once enabled, the dashboard is available at:

```
https://{owner}.github.io/{repo}/
```

For this repo: `https://elbruno.github.io/nuget-repo-dashboard/`

### Site Structure

The deployed site contains:
- `index.html` — dashboard UI (from `site/index.html`)
- `data/data.nuget.json` — NuGet package metrics
- `data/data.repositories.json` — GitHub repository metrics

## 🎨 repo-identity

A .NET CLI tool that reads the repositories tracked by this dashboard and generates [Oh My Posh](https://ohmyposh.dev/) terminal theme files — one per repo — with deterministic accent colors.

### Usage

```bash
# Preview what would be generated (no files written)
dotnet run --project src/RepoIdentity -- preview

# Generate Oh My Posh profiles into terminal/ohmyposh/
dotnet run --project src/RepoIdentity -- generate

# Use a custom source file
dotnet run --project src/RepoIdentity -- generate --source path/to/data.repositories.json

# Copy all generated profiles to your Oh My Posh themes directory
dotnet run --project src/RepoIdentity -- apply

# Copy a single repo's profile
dotnet run --project src/RepoIdentity -- apply --repo elbruno/elbruno.localembeddings
```

### Output

Each tracked repo gets a file in `terminal/ohmyposh/`:
- `{owner}-{repo}.json` — a valid Oh My Posh theme with a deterministic accent color
- `index.json` — summary manifest of all generated profiles

Colors are derived deterministically from the repo's full name and primary language, so they are stable across re-runs.

### Optional: repo.identity.json

Place a `repo.identity.json` in any tracked repo to customize its profile:

```json
{
  "name": "My Library",
  "type": "library",
  "accentColor": "#0078D4",
  "icon": "🧠"
}
```


