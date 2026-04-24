# Watch List — External Reference Repos

The **watch list** tracks external GitHub repositories that inform architecture, patterns, or workflows for the nuget-repo-dashboard project. These are reference implementations or related projects we monitor for inspiration or code patterns.

## Purpose

- **Reference implementations:** Examples of similar systems or patterns we want to study
- **Pattern discovery:** GitHub workflows, AI integrations, automation techniques
- **Dependency monitoring:** External projects this dashboard may depend on
- **Knowledge base:** Repositories that answer "how did they solve this?"

## File Format

**Location:** `config/watch-list.json`

Each entry is a JSON object with:

| Field | Type | Required | Purpose |
|-------|------|----------|---------|
| `owner` | string | ✅ | GitHub username/org (lowercase) |
| `repo` | string | ✅ | Repository name (lowercase) |
| `url` | string | ✅ | Full GitHub URL |
| `description` | string | ✅ | One-line human-readable description |
| `dateAdded` | string | ✅ | ISO date added (`YYYY-MM-DD`) |
| `purpose` | string | ✅ | Why we're watching this (1–2 sentences) |

**Example:**

```json
[
  {
    "owner": "elbruno",
    "repo": "openclawnet",
    "url": "https://github.com/elbruno/openclawnet",
    "description": "Open Claw .NET project",
    "dateAdded": "2025-04-02",
    "purpose": "Reference architecture and AI-assisted workflow patterns"
  },
  {
    "owner": "microsoft",
    "repo": "dotnet",
    "url": "https://github.com/microsoft/dotnet",
    "description": ".NET runtime and SDK",
    "dateAdded": "2025-04-02",
    "purpose": "Track .NET SDK updates and breaking changes affecting the dashboard"
  }
]
```

## How to Add a Repo

1. Open `config/watch-list.json`
2. Add a new object to the array with:
   - GitHub username in `owner`
   - Repository name in `repo`
   - Full URL in `url`
   - Brief description (what is it?)
   - Today's date in `dateAdded`
   - 1–2 sentence explanation in `purpose` (why watch it?)
3. Save and commit

That's it. No scripts needed.

## Example: Adding a New Repo

**Goal:** Watch the Spectre.Console GitHub repo for rich terminal output patterns.

```json
{
  "owner": "spectreconsole",
  "repo": "spectre.console",
  "url": "https://github.com/spectreconsole/spectre.console",
  "description": "Spectre.Console — .NET rich console library",
  "dateAdded": "2025-04-03",
  "purpose": "Study rich terminal formatting patterns for dashboard CLI output"
}
```

Then add to the array in `watch-list.json`.

## Future Automation (Optional)

Possible enhancements (not implemented yet):

- **Validation script:** Check that URLs are valid and repos exist (quick GitHub API call)
- **Changelog workflow:** Auto-generate a "repos added this week" summary for team standup
- **Freshness check:** Alert if a watched repo hasn't been updated in 6 months (useful for archival detection)
- **Dashboard view:** Display watch list on the public dashboard with purpose and link

Keep it simple for now. Add automation only when the manual process becomes a burden.
