namespace NewsImpactRanker.WinForms.Models
{
    public class TopicResult
    {
        public string Topic { get; set; }
        public int Score { get; set; }
        public string Url { get; set; }
        public string Title { get; set; }

        public bool IsClicked { get; set; } = false;

        public string Summary { get; set; }

        public string AiProvider { get; set; }

    }
}