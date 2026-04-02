# Mal — History

## Project Context

- **Project:** nuget-repo-dashboard — public dashboard tracking NuGet packages and GitHub repos
- **Stack:** C# / .NET, GitHub Actions, Blazor (future), JSON data pipeline
- **User:** Bruno Capuano
- **PRD:** `docs/nuget-dashboard-prd-v2.md`
- **Key architecture:** Deterministic workflows (refresh-metrics, refresh-inventory) produce public JSON. AI-assisted workflows (inventory-review, weekly-summary, health-triage) are advisory only and never overwrite production data.

## Learnings

### PRD Decomposition (2025-01-26)

**Architecture Pattern:** Deterministic workflows (refresh-metrics, refresh-inventory) own production data. AI-assisted workflows (.github/aw/) provide advisory insights only and never auto-merge or overwrite data.

**Key File Paths:**
- `docs/nuget-dashboard-prd-v2.md` — source of truth for requirements
- `config/tracked-packages.json` — package inventory (to be created)
- `src/Collector.NuGet/`, `src/Collector.GitHub/` — .NET collectors (to be created)
- `data/latest/*.json` — current metrics output (to be created)
- `data/history/YYYY/MM/DD/*.json` — historical metrics (to be created)
- `.github/workflows/refresh-*.yml` — deterministic automation (to be created)
- `.github/aw/*.md` — AI-assisted workflows (to be created)

**Decomposition Strategy:**
1. Foundation-first: Data models and config structure unblock all other work
2. Critical path: Models → Config → Collectors → Workflows → UI → Hosting
3. Parallel opportunities: Tests can run alongside collector development
4. Phased delivery: P0 (foundation) → P1 (core features) → P2 (enhancements)

**Team Assignment Logic:**
- Kaylee (Backend): C# models, collectors, Blazor UI
- Wash (DevOps): GitHub Actions workflows, AI-assisted workflows, hosting
- Zoe (Tester): Unit tests, integration tests, E2E validation

**Risk Mitigation:**
- API rate limits → exponential backoff, caching
- Config evolution → schema versioning
- AW complexity → start simple, iterate

**User Preferences:**
- Bruno wants daily automated metrics refresh (deterministic)
- Manual inventory refresh to control what's tracked
- Public JSON output as contract for future Blazor app
- AI workflows are advisory, never authoritative
