# Mal — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET, GitHub Actions, Blazor (future), JSON data pipeline
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Key architecture:** Deterministic workflows (refresh-metrics, refresh-inventory) produce public JSON. AI-assisted workflows (inventory-review, weekly-summary, health-triage) are advisory only and never overwrite production data.

## Learnings

(none yet)
