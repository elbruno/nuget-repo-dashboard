# NuGet + GitHub Dashboard

A public dashboard that tracks NuGet packages and GitHub repos — collecting download counts, versions, stars, issues, PRs, and more.

## Architecture

A C# Collector service fetches data from the NuGet and GitHub APIs, producing structured JSON consumed by a frontend dashboard.

```
config/tracked-packages.json   ← packages & repo mappings
        ↓
  src/Collector/               ← .NET 9 console app
        ↓
  data/latest/data.json        ← latest snapshot
  data/history/YYYY/MM/DD/     ← daily history
```

## Quick Start

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

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

## Configuration

Edit `config/tracked-packages.json` to track packages:

```json
[
  { "packageId": "Microsoft.Extensions.AI", "repos": ["dotnet/extensions"] }
]
```

## Output

The collector writes:

- **`data/latest/data.json`** — latest metrics snapshot
- **`data/history/{YYYY}/{MM}/{DD}/data.json`** — daily historical snapshots

## PRD

See [docs/nuget-dashboard-prd-v2.md](docs/nuget-dashboard-prd-v2.md) for the full product requirements document.
