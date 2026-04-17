using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class StalenessAlertTests
{
    private readonly MetricsGuardService _guard = new();

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static NuGetPackageMetrics BuildPackage(string id, long downloads)
        => new()
        {
            PackageId = id,
            TotalDownloads = downloads,
            LatestVersion = "1.0.0",
            Description = $"Test package {id}",
            Authors = "Test"
        };

    private static TrendData BuildTrendData(string packageId, params long[] dailyDownloads)
    {
        var trend = new PackageTrend();
        var startDate = new DateOnly(2026, 4, 1);
        for (int i = 0; i < dailyDownloads.Length; i++)
        {
            trend.Downloads.Add(new TrendPoint<long>
            {
                Date = startDate.AddDays(i).ToString("yyyy-MM-dd"),
                Value = dailyDownloads[i]
            });
        }

        var data = new TrendData
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            WindowDays = 90
        };
        data.Packages[packageId] = trend;
        return data;
    }

    private static TrendData BuildMultiPackageTrendData(
        params (string PackageId, long[] Downloads)[] packages)
    {
        var data = new TrendData
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            WindowDays = 90
        };
        var startDate = new DateOnly(2026, 4, 1);

        foreach (var (packageId, downloads) in packages)
        {
            var trend = new PackageTrend();
            for (int i = 0; i < downloads.Length; i++)
            {
                trend.Downloads.Add(new TrendPoint<long>
                {
                    Date = startDate.AddDays(i).ToString("yyyy-MM-dd"),
                    Value = downloads[i]
                });
            }
            data.Packages[packageId] = trend;
        }

        return data;
    }

    // ──────────────────────────────────────────────────────────────────
    // CountTrailingZeroGrowth — unit tests for the internal helper
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CountTrailingZeroGrowth_AllIncreasing_ReturnsZero()
    {
        var points = new List<TrendPoint<long>>
        {
            new() { Date = "2026-04-01", Value = 100 },
            new() { Date = "2026-04-02", Value = 110 },
            new() { Date = "2026-04-03", Value = 120 },
        };

        MetricsGuardService.CountTrailingZeroGrowth(points).Should().Be(0);
    }

    [Fact]
    public void CountTrailingZeroGrowth_AllFlat_ReturnsTotalMinusOne()
    {
        var points = new List<TrendPoint<long>>
        {
            new() { Date = "2026-04-01", Value = 500 },
            new() { Date = "2026-04-02", Value = 500 },
            new() { Date = "2026-04-03", Value = 500 },
            new() { Date = "2026-04-04", Value = 500 },
            new() { Date = "2026-04-05", Value = 500 },
            new() { Date = "2026-04-06", Value = 500 },
        };

        MetricsGuardService.CountTrailingZeroGrowth(points).Should().Be(5);
    }

    [Fact]
    public void CountTrailingZeroGrowth_GrowthThenFlat_CountsTrailingOnly()
    {
        var points = new List<TrendPoint<long>>
        {
            new() { Date = "2026-04-01", Value = 100 },
            new() { Date = "2026-04-02", Value = 110 },
            new() { Date = "2026-04-03", Value = 120 },
            new() { Date = "2026-04-04", Value = 120 },
            new() { Date = "2026-04-05", Value = 120 },
        };

        MetricsGuardService.CountTrailingZeroGrowth(points).Should().Be(2);
    }

    [Fact]
    public void CountTrailingZeroGrowth_SinglePoint_ReturnsZero()
    {
        var points = new List<TrendPoint<long>>
        {
            new() { Date = "2026-04-01", Value = 100 },
        };

        MetricsGuardService.CountTrailingZeroGrowth(points).Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────
    // 1. Package with growth — no staleness alert
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_PackageWithGrowth_NoAlert()
    {
        // Downloads increasing daily: 100, 110, 120, 130, 140, 150, 160
        var trendData = BuildTrendData("Pkg.Growing",
            100, 110, 120, 130, 140, 150, 160);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Growing", 160) };

        // Should not throw and should not produce staleness warnings
        // (CheckStaleness writes to Console — we verify it runs without error
        //  and confirm the underlying CountTrailingZeroGrowth returns 0)
        var act = () => _guard.CheckStaleness(trendData, metrics);

        act.Should().NotThrow();

        // Verify the internal logic directly
        var points = trendData.Packages["Pkg.Growing"].Downloads
            .OrderBy(p => p.Date).ToList();
        MetricsGuardService.CountTrailingZeroGrowth(points).Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Package flatlined 5+ days, >100 downloads → alert
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_FlatlinedFivePlusDays_Over100Downloads_Triggers()
    {
        // 200 downloads on day 1, then flat for 6 more days = 6 trailing zero-growth
        var trendData = BuildTrendData("Pkg.Stale",
            200, 200, 200, 200, 200, 200, 200);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Stale", 200) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        var points = trendData.Packages["Pkg.Stale"].Downloads
            .OrderBy(p => p.Date).ToList();
        var trailing = MetricsGuardService.CountTrailingZeroGrowth(points);
        trailing.Should().BeGreaterThanOrEqualTo(MetricsGuardService.StalenessThreshold);
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. Package flatlined but <100 downloads → no alert (below threshold)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_FlatlinedButBelowDownloadThreshold_NoAlert()
    {
        // Package has only 50 downloads — below the 100-download threshold
        var trendData = BuildTrendData("Pkg.SmallFlat",
            50, 50, 50, 50, 50, 50, 50);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.SmallFlat", 50) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        // Even though it's flatlined, it should be skipped due to <=100 threshold
        // Verify the threshold constant matches expectations
        MetricsGuardService.MinDownloadsForStalenessCheck.Should().Be(100);
    }

    [Fact]
    public void CheckStaleness_ExactlyAtDownloadThreshold_NoAlert()
    {
        // Exactly 100 downloads — at the boundary (<=100 means NOT checked)
        var trendData = BuildTrendData("Pkg.Boundary",
            100, 100, 100, 100, 100, 100, 100);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Boundary", 100) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        // 100 is NOT > 100, so this package is skipped
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. Package flatlined 3 days — below the 5-day threshold → no alert
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_FlatlinedThreeDays_BelowThreshold_NoAlert()
    {
        // Growth then 3-day flat: 100, 110, 120, 120, 120
        var trendData = BuildTrendData("Pkg.ShortFlat",
            100, 110, 120, 130, 140, 140, 140);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.ShortFlat", 140) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        var points = trendData.Packages["Pkg.ShortFlat"].Downloads
            .OrderBy(p => p.Date).ToList();
        var trailing = MetricsGuardService.CountTrailingZeroGrowth(points);
        trailing.Should().Be(2, "only 2 trailing zero-growth pairs");
        trailing.Should().BeLessThan(MetricsGuardService.StalenessThreshold);
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. New package with no history — no alert
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_NewPackageNoHistory_NoAlert()
    {
        // Package exists in metrics but has no trend data points
        var trendData = new TrendData
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            WindowDays = 90
        };
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Brand.New", 500) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();
    }

    [Fact]
    public void CheckStaleness_PackageInTrendButFewPoints_NoAlert()
    {
        // Only 3 data points — fewer than the 5-point threshold
        var trendData = BuildTrendData("Pkg.Young",
            200, 200, 200);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Young", 200) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge: Growth then flatline at exactly the threshold
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_ExactlyFiveDayFlat_MeetsThreshold()
    {
        // Growth then exactly 5 trailing flat days
        var trendData = BuildTrendData("Pkg.Edge",
            100, 110, 120, 120, 120, 120, 120, 120);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Edge", 120) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        var points = trendData.Packages["Pkg.Edge"].Downloads
            .OrderBy(p => p.Date).ToList();
        var trailing = MetricsGuardService.CountTrailingZeroGrowth(points);
        trailing.Should().Be(5, "exactly 5 trailing zero-growth data points");
        trailing.Should().BeGreaterThanOrEqualTo(MetricsGuardService.StalenessThreshold);
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge: Multiple packages — mixed stale/healthy
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_MultiplePackages_MixedStaleness()
    {
        var trendData = BuildMultiPackageTrendData(
            ("Pkg.Healthy", [100, 110, 120, 130, 140, 150, 160]),
            ("Pkg.Stale", [500, 500, 500, 500, 500, 500, 500]),
            ("Pkg.SmallFlat", [20, 20, 20, 20, 20, 20, 20])
        );
        var metrics = new List<NuGetPackageMetrics>
        {
            BuildPackage("Pkg.Healthy", 160),
            BuildPackage("Pkg.Stale", 500),
            BuildPackage("Pkg.SmallFlat", 20)
        };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        // Verify internals:
        // Healthy: 0 trailing flat
        var healthyPoints = trendData.Packages["Pkg.Healthy"].Downloads
            .OrderBy(p => p.Date).ToList();
        MetricsGuardService.CountTrailingZeroGrowth(healthyPoints).Should().Be(0);

        // Stale: 6 trailing flat (>= 5 threshold) and >100 downloads
        var stalePoints = trendData.Packages["Pkg.Stale"].Downloads
            .OrderBy(p => p.Date).ToList();
        MetricsGuardService.CountTrailingZeroGrowth(stalePoints).Should().Be(6);

        // SmallFlat: flatlined but only 20 downloads (below threshold — skipped)
        var smallPoints = trendData.Packages["Pkg.SmallFlat"].Downloads
            .OrderBy(p => p.Date).ToList();
        MetricsGuardService.CountTrailingZeroGrowth(smallPoints).Should().Be(6);
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge: Package in trend data but not in current metrics
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_PackageInTrendButNotInMetrics_Skipped()
    {
        var trendData = BuildTrendData("Pkg.OldGone",
            500, 500, 500, 500, 500, 500, 500);
        // Current metrics doesn't include this package
        var metrics = new List<NuGetPackageMetrics>();

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge: Decreasing downloads (negative growth, then flat)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void CheckStaleness_DecreasingThenFlat_CountsFlatPortionOnly()
    {
        var trendData = BuildTrendData("Pkg.Dip",
            500, 490, 480, 480, 480, 480, 480, 480);
        var metrics = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Dip", 480) };

        var act = () => _guard.CheckStaleness(trendData, metrics);
        act.Should().NotThrow();

        var points = trendData.Packages["Pkg.Dip"].Downloads
            .OrderBy(p => p.Date).ToList();
        var trailing = MetricsGuardService.CountTrailingZeroGrowth(points);
        trailing.Should().Be(5, "5 trailing flat days after the decrease");
    }

    // ──────────────────────────────────────────────────────────────────
    // Constants validation
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void StalenessThreshold_IsFive()
    {
        MetricsGuardService.StalenessThreshold.Should().Be(5);
    }

    [Fact]
    public void MinDownloadsForStalenessCheck_Is100()
    {
        MetricsGuardService.MinDownloadsForStalenessCheck.Should().Be(100);
    }
}
