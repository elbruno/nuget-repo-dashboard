# Inventory Review Workflow

## Description

This GitHub Agentic Workflow automatically reviews package-to-repository mappings discovered by the `refresh-inventory` workflow. It provides advisory suggestions for mapping quality and completeness.

⚠️ **This workflow is non-authoritative and advisory only.** All suggestions must be manually reviewed and approved before incorporation into tracked-packages.json.

## Triggers

- **Automatic:** When a PR is opened by the `refresh-inventory` workflow with the title pattern `[Inventory]` or `[inventory]`
- **Manual:** Can be invoked manually via `workflow_dispatch` on a PR branch

## Behavior

### 1. Read Discovered Mappings

The workflow reads the PR content to identify:
- Packages discovered in the NuGet profile
- Proposed repository mappings for each package
- Any existing mappings in `config/tracked-packages.json`

### 2. Quality Checks

For each package-repo mapping, the workflow verifies:

- **Repository Exists:** Does the specified GitHub repo exist and is it publicly accessible?
- **Repo Connection:** Does the repository contain code related to the package (detected via readme, description, or recent commits)?
- **Active Maintenance:** Has the repository been updated in the past 12 months?
- **Archived Status:** Is the repository archived? (Flag for review)
- **Multiple Matches:** Are there multiple repos that could be associated with this package?

### 3. Completeness Checks

- **Missing Repos:** Are there packages with no proposed mappings?
- **Ambiguous Names:** Are there packages with unclear ownership or sponsorship?
- **Duplicates:** Are the same repos mapped to multiple similar packages?

### 4. Output

The workflow creates a **PR review comment** with:
- ✅ Valid mappings (passed all checks)
- ⚠️ Questionable mappings (requires human judgment)
- ❓ Missing mappings (packages without repos)
- 🔍 Suggested alternatives (possible repos for ambiguous packages)

Each suggestion includes:
- Rationale (GitHub stars, commit history, readme content, etc.)
- Links to relevant repos and package pages
- Recommendation (accept / revise / skip)

## Example Output

```markdown
## 📊 Inventory Review Results

### ✅ Valid Mappings (5)
- **NewtonSoft.Json** → `JamesNK/Newtonsoft.Json` ✓ (active, 11k+ stars)
- **Serilog** → `serilog/serilog` ✓ (active, 2.3k+ stars)

### ⚠️ Questionable Mappings (2)
- **Humanizer** → `Humanizr/Humanizer` (Archived as of 2024. Recommendation: Check for fork or successor)
- **CsvHelper** → `JoshClose/CsvHelper` (No commits in 8+ months. Verify maintenance status)

### ❓ Missing Mappings (1)
- **MyCustomPackage** (Not found in any GitHub repos. Manual repo mapping required.)

### 🔍 Suggested Alternatives
- **EntityFramework.Core** could also map to `aspnet/EntityFramework`
```

## Non-Authoritative Advisory

This workflow:
- ✅ CAN: Suggest improvements, flag anomalies, provide data-driven recommendations
- ❌ CANNOT: Modify tracked-packages.json, close/approve PRs, or make unilateral decisions
- ❌ CANNOT: Access or modify production data without explicit human approval

All recommendations must be reviewed and approved by a human team member before merging into main.

## Configuration

This workflow requires:
- Read access to the repository
- GitHub CLI (`gh`) with proper authentication
- Access to the GitHub API (rate-limited; handle gracefully)
- NuGet API access (public endpoint, no credentials required)

## Error Handling

If the workflow encounters errors:
- Network timeouts or API rate limits: Post a comment indicating limited analysis, retry scope
- Repository access issues: Flag with a warning, suggest manual verification
- Missing data: Note what data is unavailable and recommend supplementary review

The workflow must not fail silently; all errors must be surfaced in the PR comment.

## Workflow Integration

This workflow is part of the larger dashboard ecosystem:
- **Depends on:** `refresh-inventory.yml` (provides PR with discovered mappings)
- **Feeds into:** Manual review process (human decision-making)
- **Related:** `weekly-summary.md`, `health-triage.md`
