using System;

namespace NewsImpactRanker.WinForms.Models
{
    public class SummaryCacheItem
    {
        public string Summary { get; set; }
        public string NormalizedSummary { get; set; }
        public string Url { get; set; }
        public string Provider { get; set; }
        public int WordCount { get; set; }
        public string TopCategory { get; set; }
        public DateTime DateAdded { get; set; }
        public bool IsCanonical { get; set; }
    }
}
