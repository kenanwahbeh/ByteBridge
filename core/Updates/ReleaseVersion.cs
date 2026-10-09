namespace ByteBridge.Updates;

/*
 * A semantic version, enough of it to answer "is that one newer?".
 *
 * Parsed by hand rather than with System.Version because release tags
 * are semantic (3.3.0-beta.1), which System.Version cannot hold, and
 * because the ordering has one rule that is easy to get backwards: a
 * pre-release is OLDER than the release it leads up to.
 */
public readonly record struct ReleaseVersion(
    int Major,
    int Minor,
    int Patch,
    string PreRelease = "") : IComparable<ReleaseVersion>
{
    public bool IsPreRelease => PreRelease.Length > 0;

    /*
     * Accepts "3.2.0", "v3.2.0", "3.2.0-beta.1" and "3.2.0+abc123"; the
     * last is what the SDK appends to the informational version. Anything
     * else, including "3.2" and "latest", is refused rather than guessed
     * at: a release tag that does not parse is not one to offer.
     */
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.Trim();

        if (span.StartsWith('v') || span.StartsWith('V'))
        {
            span = span[1..];
        }

        var plus = span.IndexOf('+');

        if (plus >= 0)
        {
            span = span[..plus];
        }

        var pre = "";
        var dash = span.IndexOf('-');

        if (dash >= 0)
        {
            pre = span[(dash + 1)..];
            span = span[..dash];

            if (!ValidPreRelease(pre))
            {
                return false;
            }
        }

        var parts = span.Split('.');

        if (parts.Length != 3
            || !Number(parts[0], out var major)
            || !Number(parts[1], out var minor)
            || !Number(parts[2], out var patch))
        {
            return false;
        }

        version = new ReleaseVersion(major, minor, patch, pre);
        return true;
    }

    private static bool Number(string text, out int value)
    {
        value = 0;

        /*
         * Digits only, no sign, no leading zero, and short enough to fit:
         * int.TryParse alone would take "+1" and " 1", and a nine-digit
         * cap keeps a hostile tag from overflowing.
         */
        if (text.Length is 0 or > 9
            || (text.Length > 1 && text[0] == '0'))
        {
            return false;
        }

        foreach (var c in text)
        {
            if (c is < '0' or > '9')
            {
                return false;
            }
        }

        value = int.Parse(text);
        return true;
    }

    private static bool ValidPreRelease(string text)
    {
        if (text.Length is 0 or > 64)
        {
            return false;
        }

        foreach (var identifier in text.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            foreach (var c in identifier)
            {
                if (!(char.IsAsciiLetterOrDigit(c) || c == '-'))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public int CompareTo(ReleaseVersion other)
    {
        var order = Major.CompareTo(other.Major);

        if (order != 0)
        {
            return order;
        }

        order = Minor.CompareTo(other.Minor);

        if (order != 0)
        {
            return order;
        }

        order = Patch.CompareTo(other.Patch);

        if (order != 0)
        {
            return order;
        }

        // 3.3.0 is newer than 3.3.0-beta.1.
        if (!IsPreRelease || !other.IsPreRelease)
        {
            return other.IsPreRelease.CompareTo(IsPreRelease);
        }

        var mine = PreRelease.Split('.');
        var theirs = other.PreRelease.Split('.');

        for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            order = CompareIdentifier(mine[i], theirs[i]);

            if (order != 0)
            {
                return order;
            }
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNumeric = a.All(char.IsAsciiDigit);
        var bNumeric = b.All(char.IsAsciiDigit);

        if (aNumeric && bNumeric)
        {
            // Compared as numbers, however long: "10" is after "9".
            var left = a.TrimStart('0');
            var right = b.TrimStart('0');

            var byLength = left.Length.CompareTo(right.Length);

            return byLength != 0
                ? byLength
                : string.CompareOrdinal(left, right);
        }

        // Numbers sort before words.
        if (aNumeric != bNumeric)
        {
            return aNumeric ? -1 : 1;
        }

        return string.CompareOrdinal(a, b);
    }

    public static bool operator <(ReleaseVersion a, ReleaseVersion b) =>
        a.CompareTo(b) < 0;

    public static bool operator >(ReleaseVersion a, ReleaseVersion b) =>
        a.CompareTo(b) > 0;

    public static bool operator <=(ReleaseVersion a, ReleaseVersion b) =>
        a.CompareTo(b) <= 0;

    public static bool operator >=(ReleaseVersion a, ReleaseVersion b) =>
        a.CompareTo(b) >= 0;

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}" + (IsPreRelease ? "-" + PreRelease : "");
}
