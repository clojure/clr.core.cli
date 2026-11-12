using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Cljr;

internal static class ClojureVersion
{
    // The oldest clojure.main that this cljr's -X/-T exec path supports;
    // it tracks the bundled tools.deps, so re-check it when that changes
    public const string ExecFloor = "1.12.3-alpha3";

    private static readonly CljSemVer ExecFloorSemVer = CljSemVer.Create(ExecFloor);
    public static bool IsBelowExecFloor(string sv)
    {
        if (!CljSemVer.TryParse(sv, out CljSemVer? v))
        {
            throw new ArgumentException($"Invalid ClojureCLR version string: {sv}");
        }

        return v!.CompareTo(ExecFloorSemVer) < 0;
    }

    public static int Compare(string sv1, string sv2)
    {
        CljSemVer v1 = CljSemVer.Create(sv1);
        CljSemVer v2 = CljSemVer.Create(sv2);

        // v1 will be non-null because Create throws if the string is invalid
        return v1.CompareTo(v2);
    }


    // We create our own version of a SemVer to deal with the fact that ClojureCLR versions are not strictly SemVer-compliant.
    // In particular, in the early days (as in the first 18 years of ClojureCLR's existence),
    // I did not number prereleases according to the official SemVer pattern, namely,
    // I would number an alpha release something like 1.2.3-alpha12 instead of the prescribed 1.2.3-alpha.12
    // To deal with older versions, we have to make sure we make:
    //     alpha8 < alpha.9 < alpha9 < alpha10
    // To deal with this we will parse the prerelease part into segments separated by dots,
    // then split each segment into runs of alpha-only and numeric-only characters.
    // and then coalesce the runs into a list of strings and integers, so that we can compare them in order.
    // When comparing two sequences of string and integers,
    //   - two strings are compared using ordinal string comparison ignoring case
    //   - two integers are compared numerically
    //   - number < string  (SemVer rule).

    public class CljSemVer : IComparable<CljSemVer>
    {
        public int Major { get; init; }
        public int Minor { get; init; }
        public int Patch { get; init; }
        public object[] PreRelease { get; init; } = [];
        public string? Metadata { get; init; }

        public static CljSemVer Create(string s)
        {
            if (!TryParse(s, out CljSemVer? v))
            {
                throw new ArgumentException($"Invalid ClojureCLR version string: {s}");
            }
            return v!;
        }

        public static readonly Regex AllowedSegmentChars = new(@"^[0-9A-Za-z]+$", RegexOptions.Compiled);
        public static readonly Regex MetadataPattern = new(@"^[0-9A-Za-z.-]+$", RegexOptions.Compiled);

        public static bool TryParse(string s, out CljSemVer? v)
        {
            // Split off the metadata

            string? metadata = null;

            var parts = s.Split('+', 2);
            if (parts.Length == 2)
            {
                if (!MetadataPattern.IsMatch(parts[1]))
                {
                    v = null;
                    return false;
                }
                metadata = parts[1];
            }

            // Split off the prerelease

            var mainParts = parts[0].Split('-', 2);

            string? preRelease = null;
            if (mainParts.Length == 2)
            {
                if (string.IsNullOrEmpty(mainParts[1]))
                {
                    v = null;
                    return false;
                }
                preRelease = mainParts[1];
            }

            // Split the main part into major, minor, and patch
            string[] versionParts = mainParts[0].Split('.');

            // There must be exactly three parts, and they must all be integers.

            if (versionParts.Length != 3)
            {
                v = null;
                return false;
            }

            if (!int.TryParse(versionParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
                || !int.TryParse(versionParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor)
                || !int.TryParse(versionParts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int patch))
            {
                v = null;
                return false;
            }

            // Parse the prerelease segments
            object[] preReleaseSegments = [];
            if (preRelease != null)
            {
                var segments = preRelease.Split('.', '-');

                // no empty segments == no trailing dots/hyphens, no leading dots/hyphens, no consecutive dots/hyphens.
                if (segments.Any(seg => string.IsNullOrEmpty(seg)))
                {
                    v = null;
                    return false;
                }

                // only allow ASCII alphanumeric and hyphen characters in prerelease segments
                if (segments.Any(seg => !AllowedSegmentChars.IsMatch(seg)))
                {
                    v = null;
                    return false;
                }

                var list = new List<object>();
                foreach (var segment in segments)
                {
                    var segmentList = ParsePrereleaseSegment(segment).ToList(); ;
                    if (segmentList.Any(seg => seg is null))
                    {
                        v = null;
                        return false;
                    }
                    list.AddRange(segmentList!);
                }
                preReleaseSegments = list.ToArray();
            }

            v = new CljSemVer
            {
                Major = major,
                Minor = minor,
                Patch = patch,
                PreRelease = preReleaseSegments,
                Metadata = metadata
            };
            return true;
        }

        private static bool TryConvertSegment(bool isDigit, string segment, out object? result)
        {

            if (isDigit)
            {
                if (long.TryParse(segment, NumberStyles.None, CultureInfo.InvariantCulture, out long num))
                {
                    result = num;
                    return true;
                }
                else
                {
                    result = null;
                    return false;
                }
            }
            else
            {
                result = segment;
                return true;
            }
        }

        private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';

        private static IEnumerable<object?> ParsePrereleaseSegment(string segment)
        {
            var current = new StringBuilder();
            bool isDigit = IsAsciiDigit(segment[0]);
            foreach (var c in segment)
            {
                if (IsAsciiDigit(c) == isDigit)
                {
                    current.Append(c);
                }
                else
                {
                    yield return TryConvertSegment(isDigit, current.ToString(), out object? v1) ? v1! : null;
                    current.Clear();
                    current.Append(c);
                    isDigit = !isDigit;
                }
            }
            yield return TryConvertSegment(isDigit, current.ToString(), out object? v2) ? v2! : null;
        }

        public int CompareTo(CljSemVer? y)
        {
            if (y is null)
                return 1;

            int result = Major.CompareTo(y.Major);
            if (result != 0) return result;
            result = Minor.CompareTo(y.Minor);
            if (result != 0) return result;
            result = Patch.CompareTo(y.Patch);
            if (result != 0) return result;
            return ComparePrerelease(PreRelease, y.PreRelease);
        }

        private int ComparePrerelease(object[] preRelease1, object[] preRelease2)
        {
            if (preRelease1.Length == 0 && preRelease2.Length == 0)
                return 0;
            if (preRelease1.Length == 0)
                return 1; // no prerelease is greater than any prerelease
            if (preRelease2.Length == 0)
                return -1; // any prerelease is less than no prerelease

            // iterate through the segments of both prerelease arrays, through the length of the shorter one, comparing each segment in order.
            for (int i = 0; i < preRelease1.Length && i < preRelease2.Length; i++)
            {
                var seg1 = preRelease1[i];
                var seg2 = preRelease2[i];
                int result;
                if (seg1 is long long1 && seg2 is long long2)
                {
                    result = long1.CompareTo(long2);
                }
                else if (seg1 is string str1 && seg2 is string str2)
                {
                    var c = StringComparer.OrdinalIgnoreCase.Compare(str1, str2);
                    result = Math.Sign(c);
                }
                else
                {
                    // one is a string and the other is an int, so the int is less than the string.
                    result = seg1 is long ? -1 : 1;
                }
                if (result != 0) return result;
            }

            // If we get here, all segments compared equal, so the shorter prerelease is less than the longer one.
            return preRelease1.Length.CompareTo(preRelease2.Length);

        }


    }






}
