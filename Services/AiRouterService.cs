using System.Collections.Generic;
using System.Threading.Tasks;
using NewsImpactRanker.WinForms.Models;

namespace NewsImpactRanker.WinForms.Services
{
    public class AiRouterService
    {
        private readonly List<IAiProvider> _providers;

        public AiRouterService(IEnumerable<IAiProvider> providers)
        {
            _providers = new List<IAiProvider>(providers);
        }

        public async Task<(bool Success, TopicScoresResponse Scores, string ProviderName, string ErrorMessage)> ClassifyAsync(string url, string text, string prompt)
        {
            string lastError = null;

            foreach (var provider in _providers)
            {
                LogService.Info($"[{provider.Name}] Classificando URL: {url}");
                var result = await provider.ClassifyAsync(text, prompt);

                if (result.Success && result.Data != null)
                {
                    LogService.Info($"[{provider.Name}] Provedor final da URL: {url}");
                    return (true, result.Data, provider.Name, null);
                }

                lastError = result.ErrorMessage ?? "Erro desconhecido";
                LogService.Warn($"[{provider.Name}] Falhou para {url}: {lastError}");
                LogService.Warn($"[FALLBACK] Motivo: {lastError}");
            }

            return (false, null, null, lastError ?? "Todos os provedores falharam.");
        }
    }
}
