# NuGet + GitHub Dashboard PRD (v2)

## Overview

Build a public dashboard that tracks: - NuGet packages (downloads,
versions) - GitHub repos (stars, issues, PRs) - Relationships between
packages and repos

## Goals

-   Daily automated metrics refresh
-   Manual inventory refresh
-   Public JSON output for Blazor app
-   Optional AI-assisted workflows (non-authoritative)

------------------------------------------------------------------------

## Architecture

### Deterministic Workflows

1.  **refresh-metrics.yml**
    -   Trigger: daily + manual
    -   Collect:
        -   NuGet data
        -   GitHub repo metrics
    -   Output:
        -   data/latest/\*.json
        -   data/history/YYYY/MM/DD/\*.json
2.  **refresh-inventory.yml**
    -   Trigger: manual
    -   Discovers:
        -   packages from NuGet profile
        -   repo mappings
    -   Output:
        -   candidate tracked-packages.json
    -   Opens PR

------------------------------------------------------------------------

### AI-Assisted Workflows (GitHub Agentic Workflows)

These are **non-authoritative** and must NOT overwrite production data.

1.  **inventory-review**
    -   Reviews discovered mappings
    -   Suggests improvements
    -   Opens PR with rationale
2.  **weekly-summary**
    -   Generates markdown summary
    -   Highlights:
        -   top packages
        -   changes
        -   issues/PRs
3.  **health-triage**
    -   Detects anomalies:
        -   stale packages
        -   high issue count
    -   Creates GitHub issues with insights

------------------------------------------------------------------------

## Data Model

### tracked-packages.json

``` json
[
  {
    "packageId": "Example",
    "repos": ["owner/repo"]
  }
]
```

### output data.json

``` json
{
  "generatedAt": "",
  "packages": [],
  "repos": []
}
```

------------------------------------------------------------------------

## Repo Structure

    /config
      tracked-packages.json

    /src
      Collector/

    /data
      latest/
      history/

    /.github/workflows
      refresh-metrics.yml
      refresh-inventory.yml

    /.github/aw
      inventory-review.md
      weekly-summary.md
      health-triage.md

------------------------------------------------------------------------

## Key Principles

-   Deterministic = truth
-   AI = advisory
-   Public JSON = contract
-   History = trends

------------------------------------------------------------------------

## Hosting

-   GitHub Pages OR Azure Static Web Apps (free)

------------------------------------------------------------------------

## Future Enhancements

-   Blazor dashboard
-   Trend charts
-   Alerts
