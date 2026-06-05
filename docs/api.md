# NuGet Dashboard API

The NuGet Dashboard exposes metrics data via REST API endpoints. This enables external tools, dashboards, and integrations to consume the package and repository metrics programmatically.

## Base URL

```
https://elbruno.github.io/nuget-repo-dashboard/api
```

## Endpoints

### Packages

#### List all packages
```
GET /api/packages
```

Returns all NuGet packages with their metrics (download counts, versions, etc.).

**Response:**
```json
{
  "packages": [
    {
      "id": "ElBruno.QRCodeGenerator",
      "latestVersion": "1.0.0",
      "downloads": 1234,
      "tags": ["QR", "code", "generation"],
      ...
    }
  ]
}
```

#### Get single package
```
GET /api/packages/{packageId}
```

Returns detailed metrics for a specific package.

**Parameters:**
- `packageId` (string): The NuGet package ID (e.g., "ElBruno.QRCodeGenerator")

**Response:**
```json
{
  "id": "ElBruno.QRCodeGenerator",
  "description": "...",
  "latestVersion": "1.0.0",
  "downloads": 1234,
  "projectUrl": "https://github.com/...",
  ...
}
```

### Repositories

#### List all repositories
```
GET /api/repositories
```

Returns all GitHub repositories with their metrics (stars, forks, issues, PRs, etc.).

**Response:**
```json
{
  "repositories": [
    {
      "fullName": "elbruno/ElBruno.QRCodeGenerator",
      "name": "ElBruno.QRCodeGenerator",
      "stars": 42,
      "forks": 5,
      "openIssues": 3,
      "recentIssues": [],
      ...
    }
  ]
}
```

#### Get single repository
```
GET /api/repositories/{owner}/{repo}
```

Returns detailed metrics for a specific repository.

**Parameters:**
- `owner` (string): GitHub owner/org name
- `repo` (string): Repository name

### Trends

#### Get historical trends
```
GET /api/trends
```

Returns historical trend data for packages and repositories (90-day window by default).

**Response:**
```json
{
  "packages": {
    "ElBruno.QRCodeGenerator": {
      "days": [
        {
          "date": "2026-06-01",
          "downloads": 100,
          "trend": 1.05
        }
      ]
    }
  },
  "repositories": {...}
}
```

#### Filter trends by time window
```
GET /api/trends?days=30
```

**Query Parameters:**
- `days` (number): Time window in days (14, 30, or 90)

### Watch List

#### Get watch list
```
GET /api/watch-list
```

Returns repositories in the watch list with their metrics.

**Response:**
```json
{
  "watchList": [
    {
      "fullName": "dotnet/runtime",
      "purpose": "Core framework tracking",
      "dateAdded": "2026-01-01",
      ...
    }
  ]
}
```

## Response Format

All endpoints return JSON with the following structure:

```json
{
  "generatedAt": "2026-06-05T12:00:00Z",
  "data": { ... }
}
```

## CORS

API endpoints include CORS headers to allow cross-origin requests from web applications.

```
Access-Control-Allow-Origin: *
Access-Control-Allow-Methods: GET, OPTIONS
Access-Control-Allow-Headers: Content-Type
```

## Caching

- Data is refreshed daily via automated GitHub Actions workflows
- Responses include `Cache-Control` headers for client-side caching
- `Last-Modified` headers indicate when data was last updated

## Rate Limiting

No rate limiting is applied to the API. However, please be respectful of bandwidth and cache responses on the client side when possible.

## Examples

### JavaScript/Fetch

```javascript
// Get all packages
const response = await fetch('https://elbruno.github.io/nuget-repo-dashboard/api/packages');
const data = await response.json();
console.log(data.packages);
```

### cURL

```bash
curl https://elbruno.github.io/nuget-repo-dashboard/api/packages \
  -H "Accept: application/json"
```

### Python

```python
import requests

response = requests.get('https://elbruno.github.io/nuget-repo-dashboard/api/packages')
packages = response.json()['packages']
```

## Data Dictionary

### Package Metrics

- `id` - NuGet package ID
- `description` - Package description
- `latestVersion` - Latest version number
- `downloads` - Total download count
- `downloadsPctChange` - Percentage change in downloads vs previous period
- `projectUrl` - URL to package project/repository
- `licenseExpression` - License (SPDX identifier)
- `authors` - Package authors
- `tags` - Package tags for categorization

### Repository Metrics

- `fullName` - Full repository name (owner/repo)
- `name` - Repository name
- `stars` - Star count
- `forks` - Fork count
- `openIssues` - Number of open issues
- `openPullRequests` - Number of open pull requests
- `recentIssues` - Recent open issues (up to 20)
- `recentClosedIssues` - Recently closed issues
- `recentPullRequests` - Recent open pull requests
- `recentMergedPullRequests` - Recently merged pull requests
- `language` - Primary programming language
- `license` - License type
- `maintainability` - Health/maintainability score

## Updates

For the latest API documentation and updates, see:
- [API Documentation](https://github.com/elbruno/nuget-repo-dashboard/blob/main/docs/api.md)
- [GitHub Repository](https://github.com/elbruno/nuget-repo-dashboard)

## Support

Found a bug or have a feature request? Open an issue on GitHub:
- [Create an Issue](https://github.com/elbruno/nuget-repo-dashboard/issues)
