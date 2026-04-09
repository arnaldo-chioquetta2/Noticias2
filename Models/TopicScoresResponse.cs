using Newtonsoft.Json;
using System.Collections.Generic;

namespace NewsImpactRanker.WinForms.Models
{
    public class TopicScoresResponse
    {
        // Note o 'S' maiúsculo aqui para o C# encontrar a propriedade
        [JsonProperty("summary")]
        public string Summary { get; set; }

        [JsonProperty("scores")]
        public Dictionary<string, int> Scores { get; set; }
    }

}