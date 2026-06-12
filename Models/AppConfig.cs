namespace NewsImpactRanker.WinForms.Models
{
    public enum AiProvider { DeepSeek, Groq, Gemini, Mistral }

    public class AppConfig
    {
        // Chaves de API
        public string AiApiKey { get; set; }      // Para o Groq
        public string GeminiApiKey { get; set; }  // Para o Gemini
        public string DeepSeekApiKey { get; set; }
        public string DeepSeekModel { get; set; } = "deepseek-chat";
        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";
        public string ProviderPriority { get; set; } = "DeepSeek>Groq>Gemini";
        public string MistralApiKey { get; set; }
        public string MistralModel { get; set; } = "open-mixtral-8x7b";

        // Preferências
        public AiProvider SelectedProvider { get; set; } = AiProvider.DeepSeek;

        // Aqui está a correção: mudei para SelectedModel para ficar claro
        public string SelectedModel { get; set; } = "llama-3.1-8b-instant";

        // Caminhos de arquivos
        public string PromptFilePath { get; set; }
        public string NewsFilePath { get; set; }
        public int SummaryWordCount { get; set; } = 10;
        public string GroqModel { get; set; } = "llama-3.1-8b-instant";
        public string GeminiModel { get; set; } = "gemini-2.0-flash";
    }
}
