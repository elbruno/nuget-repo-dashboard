using FluentAssertions;
using NuGetDashboard.Collector.Models;
using NuGetDashboard.Collector.Services;

namespace Collector.Tests;

public class MonotonicityGuardTests
{
    private readonly MetricsGuardService _guard = new();

    // ──────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────

    private static NuGetPackageMetrics BuildPackage(string id, long downloads, string version = "1.0.0")
        => new()
        {
            PackageId = id,
            TotalDownloads = downloads,
            LatestVersion = version,
            Description = $"Test package {id}",
            Authors = "Test"
        };

    // ──────────────────────────────────────────────────────────────────
    // 1. Fresh data higher — new downloads > previous → use new value
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_FreshHigher_UsesNewValue()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 500) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 400) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().HaveCount(1);
        result[0].TotalDownloads.Should().Be(500);
    }

    // ──────────────────────────────────────────────────────────────────
    // 2. Fresh data lower (guard activates) — keeps previous value
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_FreshLower_GuardActivates_KeepsPreviousValue()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 300) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 500) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().HaveCount(1);
        result[0].TotalDownloads.Should().Be(500, "guard should replace lower fresh value with previous");
    }

    // ──────────────────────────────────────────────────────────────────
    // 3. No previous data — first run → use fresh data as-is
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_NoPreviousData_Null_UsesFreshAsIs()
    {
        var fresh = new List<NuGetPackageMetrics>
        {
            BuildPackage("Pkg.A", 100),
            BuildPackage("Pkg.B", 200)
        };

        var result = _guard.ApplyMonotonicityGuard(fresh, null);

        result.Should().BeSameAs(fresh);
        result[0].TotalDownloads.Should().Be(100);
        result[1].TotalDownloads.Should().Be(200);
    }

    [Fact]
    public void ApplyMonotonicityGuard_NoPreviousData_EmptyList_UsesFreshAsIs()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 100) };
        var previous = new List<NuGetPackageMetrics>();

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().BeSameAs(fresh);
        result[0].TotalDownloads.Should().Be(100);
    }

    // ──────────────────────────────────────────────────────────────────
    // 4. New package not in previous — use fresh data
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_NewPackageNotInPrevious_UsesFreshValue()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.New", 50) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Old", 300) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().HaveCount(1);
        result[0].PackageId.Should().Be("Pkg.New");
        result[0].TotalDownloads.Should().Be(50, "no previous data for this package — use fresh");
    }

    // ──────────────────────────────────────────────────────────────────
    // 5. Package removed from API — was in previous, not in fresh
    //    (Package drops off — this is fine, only guard packages that ARE collected)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_PackageRemovedFromApi_DropsOff()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 100) };
        var previous = new List<NuGetPackageMetrics>
        {
            BuildPackage("Pkg.A", 80),
            BuildPackage("Pkg.Removed", 999)
        };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().HaveCount(1, "removed package should not reappear");
        result[0].PackageId.Should().Be("Pkg.A");
    }

    // ──────────────────────────────────────────────────────────────────
    // 6. Multiple packages mixed — some higher, some lower, some new
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_MixedPackages_CorrectPerPackageBehavior()
    {
        var fresh = new List<NuGetPackageMetrics>
        {
            BuildPackage("Pkg.Higher", 600),   // grew
            BuildPackage("Pkg.Lower", 100),    // dropped (guard)
            BuildPackage("Pkg.New", 10),       // brand new
            BuildPackage("Pkg.Same", 300),     // unchanged
        };
        var previous = new List<NuGetPackageMetrics>
        {
            BuildPackage("Pkg.Higher", 400),
            BuildPackage("Pkg.Lower", 500),
            BuildPackage("Pkg.Same", 300),
            BuildPackage("Pkg.Gone", 999),     // removed from API
        };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().HaveCount(4);
        result.Single(p => p.PackageId == "Pkg.Higher").TotalDownloads.Should().Be(600);
        result.Single(p => p.PackageId == "Pkg.Lower").TotalDownloads.Should().Be(500, "guard replaces lower value");
        result.Single(p => p.PackageId == "Pkg.New").TotalDownloads.Should().Be(10);
        result.Single(p => p.PackageId == "Pkg.Same").TotalDownloads.Should().Be(300);
    }

    // ──────────────────────────────────────────────────────────────────
    // 7. Equal values — no change (guard doesn't activate)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_EqualValues_NoChange()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 1000) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 1000) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result[0].TotalDownloads.Should().Be(1000);
    }

    // ──────────────────────────────────────────────────────────────────
    // 8. Zero downloads in both — stays 0
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_ZeroDownloadsInBoth_StaysZero()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Zero", 0) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.Zero", 0) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result[0].TotalDownloads.Should().Be(0);
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge case: Case-insensitive package ID matching
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_CaseInsensitiveMatch()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("elbruno.QRCode", 100) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("ElBruno.QRCode", 500) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result[0].TotalDownloads.Should().Be(500, "case-insensitive lookup should match and guard");
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge case: Guard mutates the fresh list in-place (returns same ref)
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_MutatesInPlace_ReturnsSameList()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 100) };
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 500) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().BeSameAs(fresh, "method modifies fresh metrics in-place");
        fresh[0].TotalDownloads.Should().Be(500);
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge case: Empty fresh list with non-empty previous
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_EmptyFreshList_ReturnsEmpty()
    {
        var fresh = new List<NuGetPackageMetrics>();
        var previous = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 500) };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────
    // Edge case: Previous entry with blank PackageId is skipped
    // ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMonotonicityGuard_PreviousWithBlankId_IsIgnored()
    {
        var fresh = new List<NuGetPackageMetrics> { BuildPackage("Pkg.A", 50) };
        var previous = new List<NuGetPackageMetrics>
        {
            BuildPackage("", 9999),
            BuildPackage("Pkg.A", 40),
        };

        var result = _guard.ApplyMonotonicityGuard(fresh, previous);

        result[0].TotalDownloads.Should().Be(50, "fresh is higher than previous 40");
    }
}
