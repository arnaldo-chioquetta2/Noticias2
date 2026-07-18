using System;

namespace NewsImpactRanker.WinForms.Models
{
    public class SummaryCacheItem
    {
        // O resumo de exatas 10 palavras gerado pela IA
        public string Summary { get; set; }

        // A categoria que teve a maior pontuação (ex: "Hardware", "Ecologia")
        public string TopCategory { get; set; }

        // A data em que o resumo foi salvo (útil para limpar o cache depois de uns 30 dias)
        public DateTime DateAdded { get; set; }

        // Apenas chaves geradas pelo fluxo pré-avaliação podem decidir duplicidade.
        public bool IsCanonical { get; set; }
    }
}
