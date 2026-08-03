using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NewsImpactRanker.WinForms.Utils
{
    public static class CanonicalSummaryMatcher
    {
        public static string Normalize(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
            string decomposed = summary.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var chars = decomposed
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .Select(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c) ? c : ' ')
                .ToArray();

            return string.Join(" ", new string(chars)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeToken)
                .Select(CanonicalEquivalent));
        }

        public static double Similarity(string left, string right)
        {
            var leftTokens = Tokens(left);
            var rightTokens = Tokens(right);
            if (leftTokens.Count == 0 || rightTokens.Count == 0) return 0;
            int intersection = leftTokens.Intersect(rightTokens).Count();
            int union = leftTokens.Union(rightTokens).Count();
            return union == 0 ? 0 : (double)intersection / union;
        }

        public static int SharedTokenCount(string left, string right)
        {
            return Tokens(left).Intersect(Tokens(right)).Count();
        }

        public static bool IsMatch(string left, string right, out string reason)
        {
            string normalizedLeft = Normalize(left);
            string normalizedRight = Normalize(right);
            if (string.Equals(normalizedLeft, normalizedRight, StringComparison.Ordinal))
            {
                reason = "igualdade exata do resumo normalizado";
                return true;
            }

            int shared = SharedTokenCount(normalizedLeft, normalizedRight);
            double similarity = Similarity(normalizedLeft, normalizedRight);
            if (shared >= 3 && similarity >= 0.40)
            {
                reason = $"equivalência lexical controlada ({shared} tokens compartilhados)";
                return true;
            }

            reason = $"similaridade {similarity:0.00}; tokens compartilhados {shared}";
            return false;
        }

        private static HashSet<string> Tokens(string value)
        {
            return new HashSet<string>(
                Normalize(value).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);
        }

        private static string NormalizeToken(string token)
        {
            if (token.Length > 4 && token.EndsWith("s", StringComparison.Ordinal))
                token = token.Substring(0, token.Length - 1);
            return token;
        }

        private static string CanonicalEquivalent(string token)
        {
            switch (token)
            {
                case "reaproveitavel": return "reutilizavel";
                case "chines": return "china";
                case "pouso":
                case "pousar":
                case "pousou": return "pousar";
                case "envelhecimento":
                case "envelhece":
                case "envelhecer": return "envelhecer";
                case "cerebral":
                case "cerebro": return "cerebro";
                default: return token;
            }
        }
    }
}
