using Newtonsoft.Json;
using System.Collections.Generic;

public class AiClassificationResponse
{
    [JsonProperty("summary")]
    public string Summary { get; set; } // 👉 NOVO CAMPO AQUI

    [JsonProperty("scores")]
    public Dictionary<string, int> Scores { get; set; }
}