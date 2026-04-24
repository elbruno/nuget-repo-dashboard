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

### Watch List System (2025-04-02)

**Pattern:** Minimal-friction configuration for external reference repos.

**Architecture Decision:** Simple JSON array (config/watch-list.json) with flat schema: `owner`, `repo`, `url`, `description`, `dateAdded`, `purpose`. No validation script or CI gate needed initially; manual editing keeps friction low and makes the process intentional.

**Key Files:**
- `config/watch-list.json` — watch list data
- `config/WATCH-LIST.md` — complete user guide (schema, examples, how-to)
- `.squad/decisions/inbox/mal-watch-list-system.md` — architectural decision record

**Design Principles Applied:**
1. **Zero ceremony:** 4-step manual process; no scripting or validation
2. **Self-documenting:** Markdown guide + clear field names in JSON
3. **Scalable:** Works from 1 to 50+ repos; automation can be added later without redesign
4. **Intentional:** Manual editing forces contributors to think about why each repo matters

**Future Extensibility:** URL validation, freshness checks, and dashboard integration possible without schema changes (flat schema is flexible).
