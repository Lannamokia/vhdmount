#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace VHDMounter
{
    internal static class VersionComparison
    {
        private static readonly Regex NumericTokenRegex = new Regex(@"\d+", RegexOptions.Compiled);

        public static int Compare(string? left, string? right)
        {
            var leftParts = Parse(left);
            var rightParts = Parse(right);
            var count = Math.Max(leftParts.Count, rightParts.Count);
            for (var i = 0; i < count; i++)
            {
                var l = i < leftParts.Count ? leftParts[i] : 0;
                var r = i < rightParts.Count ? rightParts[i] : 0;
                var cmp = l.CompareTo(r);
                if (cmp != 0)
                {
                    return cmp;
                }
            }

            return 0;
        }

        private static List<int> Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return new List<int>();
            }

            return NumericTokenRegex.Matches(value)
                .Select(match => int.TryParse(match.Value, out var parsed) ? parsed : 0)
                .ToList();
        }
    }
}
