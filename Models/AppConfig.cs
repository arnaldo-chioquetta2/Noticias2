using System;
namespace NewsImpactRanker.WinForms.Models
{
    public enum AiProvider { DeepSeek, Groq, Gemini, Mistral, Kimi }

    public class AppConfig
    {
        public string AiApiKey { get; set; }
        public string GeminiApiKey { get; set; }
        public string DeepSeekApiKey { get; set; }
        public string DeepSeekModel { get; set; } = "deepseek-chat";
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
        public string ProviderPriority { get; set; } = "DeepSeek>Groq>Gemini>Kimi";
        public string KimiApiKey { get; set; } = "";
        public string KimiBaseUrl { get; set; } = "https://servidorapi.duckdns.org/v1";
        public string KimiModel { get; set; } = "kimi-k2";
        public bool KimiEnableSearch { get; set; } = true;
        public bool KimiEnableThinking { get; set; } = true;
        public string MistralApiKey { get; set; }
        public string MistralModel { get; set; } = "open-mixtral-8x7b";
        public AiProvider SelectedProvider { get; set; } = AiProvider.DeepSeek;
        public string SelectedModel { get; set; } = "llama-3.1-8b-instant";
        public string PromptFilePath { get; set; }
        public string NewsFilePath { get; set; }
        public int SummaryWordCount { get; set; } = 10;
        public string GroqModel { get; set; } = "llama-3.1-8b-instant";
        public string GeminiModel { get; set; } = "gemini-2.0-flash";
    }
}
