using System.Collections.Generic;

public class NewsScoresItem
{
    public string Url { get; set; }

    public string Title { get; set; }

    public Dictionary<string, int> Scores { get; set; }

    public int SourceOrder { get; set; }

    public string Summary { get; set; }

    public string AiProvider { get; set; }
}