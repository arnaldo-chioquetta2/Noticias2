using System.Collections.Generic;

namespace NewsImpactRanker.WinForms.Models
{
    public class TopicScoresResponse
    {
        public Dictionary<string, int> scores { get; set; }
    }

}