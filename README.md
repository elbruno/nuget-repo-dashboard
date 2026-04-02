# NuGet + GitHub Dashboard

A public dashboard that tracks NuGet packages and GitHub repos — collecting download counts, versions, stars, issues, PRs, and more.

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

## PRD

See [docs/nuget-dashboard-prd-v2.md](docs/nuget-dashboard-prd-v2.md) for the full product requirements document.
