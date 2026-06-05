#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Setup script for NuGet Dashboard REST API
    
.DESCRIPTION
    Creates API endpoint structure and copies data files to the appropriate locations.
    This enables the API endpoints to serve package, repository, and trend data.
    
.EXAMPLE
    .\setup-api.ps1
#>

param(
    [string]$SourceDir = "data/latest",
    [string]$ApiDir = "site/api"
)

Write-Host "Setting up NuGet Dashboard REST API..."
Write-Host "Source directory: $SourceDir"
Write-Host "API directory: $ApiDir"

# Create API directory structure
$apiDirs = @(
    $ApiDir,
    "$ApiDir/packages",
    "$ApiDir/repositories",
    "$ApiDir/trends"
)

foreach ($dir in $apiDirs) {
    if (!(Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
        Write-Host "Created: $dir"
    }
}

# Create symbolic links or copies of data files
$dataFiles = @(
    @{ source = "$SourceDir/data.nuget.json"; dest = "$ApiDir/packages/index.json" },
    @{ source = "$SourceDir/data.repositories.json"; dest = "$ApiDir/repositories/index.json" },
    @{ source = "$SourceDir/data.trends.json"; dest = "$ApiDir/trends/index.json" },
    @{ source = "$SourceDir/data.metadata.json"; dest = "$ApiDir/metadata.json" }
)

foreach ($file in $dataFiles) {
    if (Test-Path $file.source) {
        Write-Host "Copying: $($file.source) -> $($file.dest)"
        Copy-Item -Path $file.source -Destination $file.dest -Force
    } else {
        Write-Host "Warning: Source file not found: $($file.source)"
    }
}

Write-Host ""
Write-Host "API Setup complete!"
Write-Host ""
Write-Host "API Endpoints are available at:"
Write-Host "  GET /api/packages/index.json - All packages"
Write-Host "  GET /api/repositories/index.json - All repositories"
Write-Host "  GET /api/trends/index.json - Historical trends"
Write-Host "  GET /api/metadata.json - Metadata"
Write-Host ""
Write-Host "For more information, see: docs/api.md"
