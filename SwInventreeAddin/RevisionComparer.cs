using System;
using System.Text.RegularExpressions;

namespace SwInventreeAddin
{
    /// <summary>
    /// Outcome of comparing two revision strings.
    /// </summary>
    public enum RevisionOrder
    {
        Equal,
        SwIsNewer,
        ItIsNewer,

        /// <summary>
        /// Schemes differ, one or both are blank in an unresolvable way,
        /// or the strings cannot be parsed. Caller should hard-block.
        /// </summary>
        Ambiguous
    }

    /// <summary>
    /// Compares two revision strings by auto-detecting the scheme in use.
    ///
    /// Supported schemes (both sides must match the same scheme):
    ///   Numeric          — 1, 2, 10
    ///   Dot-numeric      — 1.0, 1.1, 2.0
    ///   Alpha/Alphanumeric (unified) — A, A1, A2, B, Z, AA   (A=1, A1=1.1, B=2 …)
    ///
    /// If the schemes differ, or the strings are unparseable, returns Ambiguous.
    /// </summary>
    public static class RevisionComparer
    {
        private static readonly Regex AlphaNumeric =
            new Regex(@"^([A-Za-z]+)(\d*)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static RevisionOrder Compare(string swRev, string itRev)
        {
            var sw = (swRev ?? string.Empty).Trim();
            var it = (itRev ?? string.Empty).Trim();

            // Both blank → equal (unversioned vs unversioned)
            if (sw.Length == 0 && it.Length == 0)
                return RevisionOrder.Equal;

            // SW blank, IT has value → can't determine which is newer
            if (sw.Length == 0)
                return RevisionOrder.Ambiguous;

            // SW has value, IT blank → SW is ahead (e.g. newly assigned rev, not yet pushed)
            if (it.Length == 0)
                return RevisionOrder.SwIsNewer;

            // --- Scheme 1: pure integer ---
            if (int.TryParse(sw, out int swInt) && int.TryParse(it, out int itInt))
                return CompareInts(swInt, itInt);

            // --- Scheme 2: dot-numeric ---
            if (TryParseDotNumeric(sw, out int[] swSegs) && TryParseDotNumeric(it, out int[] itSegs))
                return CompareDotNumeric(swSegs, itSegs);

            // --- Scheme 3: alpha / alphanumeric (unified) ---
            if (TryParseAlpha(sw, out string swLetters, out int swNum) &&
                TryParseAlpha(it, out string itLetters, out int itNum))
            {
                int lc = CompareLetterParts(swLetters, itLetters);
                if (lc != 0)
                    return lc > 0 ? RevisionOrder.SwIsNewer : RevisionOrder.ItIsNewer;
                return CompareInts(swNum, itNum);
            }

            return RevisionOrder.Ambiguous;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static RevisionOrder CompareInts(int sw, int it)
        {
            if (sw == it) return RevisionOrder.Equal;
            return sw > it ? RevisionOrder.SwIsNewer : RevisionOrder.ItIsNewer;
        }

        private static bool TryParseDotNumeric(string s, out int[] segments)
        {
            // Must contain at least one dot to be treated as dot-numeric
            // (plain integers are already handled by the integer branch above)
            segments = Array.Empty<int>();
            if (s.IndexOf('.') < 0) return false;

            var parts = s.Split('.');
            var result = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out result[i]))
                {
                    segments = Array.Empty<int>();
                    return false;
                }
            }
            segments = result;
            return true;
        }

        private static RevisionOrder CompareDotNumeric(int[] sw, int[] it)
        {
            int len = Math.Max(sw.Length, it.Length);
            for (int i = 0; i < len; i++)
            {
                int s = i < sw.Length ? sw[i] : 0;
                int t = i < it.Length ? it[i] : 0;
                if (s != t)
                    return s > t ? RevisionOrder.SwIsNewer : RevisionOrder.ItIsNewer;
            }
            return RevisionOrder.Equal;
        }

        private static bool TryParseAlpha(string s, out string letters, out int number)
        {
            letters = string.Empty;
            number  = 0;
            var m = AlphaNumeric.Match(s);
            if (!m.Success) return false;
            letters = m.Groups[1].Value.ToUpperInvariant();
            var numStr = m.Groups[2].Value;
            number = numStr.Length == 0 ? 0 : int.Parse(numStr);
            return true;
        }

        /// <summary>
        /// Letter comparison: shorter string is earlier (A…Z before AA…).
        /// Same length: lexicographic (case-insensitive).
        /// </summary>
        private static int CompareLetterParts(string sw, string it)
        {
            if (sw.Length != it.Length)
                return sw.Length.CompareTo(it.Length);
            return string.Compare(sw, it, StringComparison.OrdinalIgnoreCase);
        }
    }
}
