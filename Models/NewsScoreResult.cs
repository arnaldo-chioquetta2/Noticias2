using System.Collections.Generic;

namespace NewsImpactRanker.WinForms.Models
{
    public class NewsScoreResult
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public Dictionary<string, int> Scores { get; set; }
    }
}