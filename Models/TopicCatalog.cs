using System.Collections.Generic;

namespace NewsImpactRanker.WinForms.Models
{
    public static class TopicCatalog
    {
        // Ordem EXATA do prompt
        public static readonly string[] Codes =
        {
            "QC","QP","CP","HW","AB","AE","GA","EB","NT","SP","AU","MP","CS","IS","BS","LG","PR","SE","SM","SO","HC","AT","AH","OC","MM","EC"
        };

        public static readonly Dictionary<string, string> CodeToName = new Dictionary<string, string>
        {
            { "QC", "Computação Quântica" },
            { "QP", "Física Quântica / Multiverso" },
            { "CP", "Computação" },
            { "HW", "Hardware" },
            { "AB", "IA para Negócios" },
            { "AE", "IA e Educação" },
            { "GA", "IA Generativa" },
            { "EB", "Energia / Baterias" },
            { "NT", "Nanotecnologia" },
            { "SP", "Espaço" },
            { "AU", "Automação com IA" },
            { "MP", "Plantas Medicinais" },
            { "CS", "Ciência Controversa" },
            { "IS", "Ciência Inovadora" },
            { "BS", "Ciência do Cérebro" },
            { "LG", "Longevidade" },
            { "PR", "Programação" },
            { "SE", "Segurança da Informação" },
            { "SM", "Smartphones" },
            { "SO", "Órgãos Sensoriais" },
            { "HC", "Consciência Humana" },
            { "AT", "Autismo" },
            { "AH", "TDAH" }, // Transtorno do Déficit de Atenção com Hiperatividade
            { "OC", "TOC" },  // Transtorno Obsessivo-Compulsivo
            { "MM", "Metamateriais" },
            { "EC", "Ecologia" }
        };
    }
}