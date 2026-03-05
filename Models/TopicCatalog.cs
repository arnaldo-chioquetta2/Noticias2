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
            {"QC","Quantum Computing"},
            {"QP","Quantum Physics / Multiverse"},
            {"CP","Computing"},
            {"HW","Hardware"},
            {"AB","AI for Business"},
            {"AE","AI and Education"},
            {"GA","Generative AI"},
            {"EB","Energy / Batteries"},
            {"NT","Nanotechnology"},
            {"SP","Space"},
            {"AU","AI Automation"},
            {"MP","Medicinal Plants"},
            {"CS","Controversial Science"},
            {"IS","Innovative Science"},
            {"BS","Brain Science"},
            {"LG","Longevity"},
            {"PR","Programming"},
            {"SE","Information Security"},
            {"SM","Smartphones"},
            {"SO","Sensory Organs"},
            {"HC","Human Consciousness"},
            {"AT","Autism"},
            {"AH","ADHD"},
            {"OC","OCD"},
            {"MM","Metamaterials"},
            {"EC","Ecology"},
        };
    }
}