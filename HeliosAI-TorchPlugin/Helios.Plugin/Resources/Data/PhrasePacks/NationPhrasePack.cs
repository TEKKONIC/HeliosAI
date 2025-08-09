using System.Collections.Generic;

namespace HeliosAI.Broadcasting
{
    public class NationPhrasePack
    {
        public string Nation { get; set; }
        public Dictionary<string, List<string>> Phrases { get; set; } = new();
        public Dictionary<string, List<string>> Triggers { get; set; } = new();
    }
}