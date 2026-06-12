using System;
using System.Globalization;
using System.Threading;

namespace NewsImpactRanker.WinForms.Services
{
    public static class CostManager
    {
        private static long _geminiPromptTokens;
        private static long _geminiCompletionTokens;
        private static long _groqPromptTokens;
        private static long _groqCompletionTokens;

        private const decimal GeminiPromptCostPer1M = 0.075m;
        private const decimal GeminiCompletionCostPer1M = 0.30m;
        private const decimal GroqPromptCostPer1M = 0.05m;
        private const decimal GroqCompletionCostPer1M = 0.08m;

        public static void Reset()
        {
            Interlocked.Exchange(ref _geminiPromptTokens, 0);
            Interlocked.Exchange(ref _geminiCompletionTokens, 0);
            Interlocked.Exchange(ref _groqPromptTokens, 0);
            Interlocked.Exchange(ref _groqCompletionTokens, 0);
        }

        public static void AddGeminiUsage(int promptTokens, int completionTokens)
        {
            if (promptTokens > 0) Interlocked.Add(ref _geminiPromptTokens, promptTokens);
            if (completionTokens > 0) Interlocked.Add(ref _geminiCompletionTokens, completionTokens);
        }

        public static void AddGroqUsage(int promptTokens, int completionTokens)
        {
            if (promptTokens > 0) Interlocked.Add(ref _groqPromptTokens, promptTokens);
            if (completionTokens > 0) Interlocked.Add(ref _groqCompletionTokens, completionTokens);
        }

        public static string GetFormattedTotalCost()
        {
            var gemini = GetGeminiCost();
            var groq = GetGroqCost();
            var total = gemini + groq;

            return string.Format(
                CultureInfo.InvariantCulture,
                "Total: ${0:0.000000} (Gemini: ${1:0.000000} | Groq: ${2:0.000000})",
                total,
                gemini,
                groq);
        }

        public static string GetUsageSummary()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Gemini: {0} tokens | Groq: {1} tokens",
                GetGeminiTokens(),
                GetGroqTokens());
        }

        public static string GetDetailedReportLine()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "- Gemini: prompt={0}, completion={1}, total={2} tokens (Custo: ${3:0.000000}){4}- Groq: prompt={5}, completion={6}, total={7} tokens (Custo: ${8:0.000000}){4}- CUSTO TOTAL DA OPERAÇÃO: ${9:0.000000}",
                GetGeminiPromptTokens(),
                GetGeminiCompletionTokens(),
                GetGeminiTokens(),
                GetGeminiCost(),
                Environment.NewLine,
                GetGroqPromptTokens(),
                GetGroqCompletionTokens(),
                GetGroqTokens(),
                GetGroqCost(),
                GetGeminiCost() + GetGroqCost());
        }

        public static string GetSingleLineCostSummary()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "CUSTO IA acumulado: Total=${0:0.000000} | Gemini=${1:0.000000} | Groq=${2:0.000000} | Tokens Gemini={3} | Tokens Groq={4}",
                GetGeminiCost() + GetGroqCost(),
                GetGeminiCost(),
                GetGroqCost(),
                GetGeminiTokens(),
                GetGroqTokens());
        }

        public static long GetGeminiTokens()
        {
            return Interlocked.Read(ref _geminiPromptTokens) + Interlocked.Read(ref _geminiCompletionTokens);
        }

        public static long GetGroqTokens()
        {
            return Interlocked.Read(ref _groqPromptTokens) + Interlocked.Read(ref _groqCompletionTokens);
        }

        public static decimal GetGeminiCost()
        {
            return CalculateCost(GetGeminiPromptTokens(), GetGeminiCompletionTokens(), GeminiPromptCostPer1M, GeminiCompletionCostPer1M);
        }

        public static decimal GetGroqCost()
        {
            return CalculateCost(GetGroqPromptTokens(), GetGroqCompletionTokens(), GroqPromptCostPer1M, GroqCompletionCostPer1M);
        }

        public static long GetGeminiPromptTokens() => Interlocked.Read(ref _geminiPromptTokens);
        public static long GetGeminiCompletionTokens() => Interlocked.Read(ref _geminiCompletionTokens);
        public static long GetGroqPromptTokens() => Interlocked.Read(ref _groqPromptTokens);
        public static long GetGroqCompletionTokens() => Interlocked.Read(ref _groqCompletionTokens);

        private static decimal CalculateCost(long promptTokens, long completionTokens, decimal promptCostPer1M, decimal completionCostPer1M)
        {
            return (promptTokens * promptCostPer1M / 1000000m) + (completionTokens * completionCostPer1M / 1000000m);
        }
    }
}
