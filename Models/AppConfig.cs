namespace NewsImpactRanker.WinForms.Models
{
    public enum AiProvider { Groq, Gemini }

    public class AppConfig
    {
        // Chaves de API
        public string AiApiKey { get; set; }      // Para o Groq
        public string GeminiApiKey { get; set; }  // Para o Gemini

        // Preferências
        public AiProvider SelectedProvider { get; set; } = AiProvider.Groq;

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