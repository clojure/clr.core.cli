namespace Cljr.Tests;

public class ClojureVersionTests
{
    [Theory]
    [InlineData("1.12.3-alpha3", "1.12.3-alpha3", 0)]      // identical
    [InlineData("1.12.3-alpha2", "1.12.3-alpha3", -1)]     // differing in prerelease numeric segment
    [InlineData("1.12.3-alpha2", "1.12.3-alpha9", -1)]     // differing in prerelease numeric segment, non-consecutive
    [InlineData("1.12.3-alpha3", "1.12.3", -1)]            // prerelease vs no prerelease
    [InlineData("1.12.3", "1.12.6", -1)]                   // differing in patch version
    [InlineData("1.12.6", "1.12.3", 1)]                    // works in reverse order too
    [InlineData("1.12.6-beta1", "1.12.6", -1)]             // prerelease vs no prerelease, but beta
    [InlineData("1.12.6-alpha", "1.12.6-alpha8", -1)]      // prerelease with no numeric segment vs prerelease with numeric segment (shorter is less than longer)
    [InlineData("1.12.6-alpha8", "1.12.6-alpha.9", -1)]    // prerelease with numeric segment vs prerelease with numeric segment, but with dot separator
    [InlineData("1.12.6-alpha.9", "1.12.6-alpha9", 0)]     // prerelease with numeric segment vs prerelease with numeric segment, but with dot separator, but they are equivalent
    [InlineData("1.12.6-alpha9", "1.12.6-alpha10", -1)]    // 9 comes before 10 (numeric comparison, not string comparison)
    [InlineData("1.12.6-alpha.9", "1.12.6-alpha.10", -1)]  // 9 comes before 10 (numeric comparison, not string comparison), with dot separator
    [InlineData("1.12.6-alpha10", "1.12.6-beta1", -1)]     // alpha comes before beta (string comparison)
    [InlineData("1.12.6-beta1", "1.12.6-rc.1", -1)]        // beta comes before rc (string comparison)
    [InlineData("1.12.6-rc.1", "1.12.6", -1)]              // prerelease vs no prerelease
    [InlineData("1.12.6-ALPHA8", "1.12.6-alpha8", 0)]      // case-insensitive comparison of prerelease segments
    public void CompareTests(string sv1, string sv2, int expected)
    {
        int actual = ClojureVersion.Compare(sv1, sv2);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("1.12")]              // Missing patch version
    [InlineData("1.12.3.1")]          // Extra version segment
    [InlineData(" 1.12.3")]           // leading space not allowed
    [InlineData("1.12.3-")]           // missing prerelease segment not allowed
    [InlineData("v1.12.3")]           // only numeric segments allowed in major.minor.patch
    [InlineData("latest")]            // Non-numeric major.minor.patch segments not allowed
    [InlineData("")]                  // Empty string not allowed
    [InlineData("1.12.3-١")]          // Only ASCII digits allowed in prerelease segments
    [InlineData("1.12.3-alpha_1")]    // underscore not allowed in prerelease segments
    [InlineData("1.12.3-alpha..1")]   // no ..
    [InlineData("1.12.3+")]           // bad metadata segment
    [InlineData("1.12.3+ x y")]       // bad metadata segment
    [InlineData("1.12.3-alpha99999999999999999999999999")]   // overflow -- seriously, you should be at least beta by now
    public void BadDataThrows(string sv)
    {
        Assert.Throws<ArgumentException>(() => ClojureVersion.Compare(sv, "1.12.3"));
    }

    [Theory]
    [InlineData("1.12.3-alpha2", true)]
    [InlineData("1.12.3-alpha3", false)]
    public void IsBelowExecFloorTests(string sv, bool expected)
    {
        bool actual = ClojureVersion.IsBelowExecFloor(sv);
        Assert.Equal(expected, actual);
    }
}
