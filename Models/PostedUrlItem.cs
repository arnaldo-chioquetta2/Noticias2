using System;

namespace NewsImpactRanker.WinForms.Models
{
    public class PostedUrlItem
    {
        public string Url { get; set; }
        public string NormalizedUrl { get; set; }
        public DateTime MarkedAt { get; set; }
        public string Summary { get; set; }
        public string Provider { get; set; }
        public string ApplicationVersion { get; set; }
    }
}