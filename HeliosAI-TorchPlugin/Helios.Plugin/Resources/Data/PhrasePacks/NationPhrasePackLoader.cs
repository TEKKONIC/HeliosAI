using System;
using System.Collections.Generic;
using System.IO;
using Helios.Core;
using Newtonsoft.Json;
using HeliosAI.Broadcasting;

namespace HeliosAI.Phrases
{
    public class NationPhrasePackLoader
    {
        public static readonly Dictionary<string, NationPhrasePack> PhrasePacks = new(StringComparer.OrdinalIgnoreCase);

        public void LoadAll(string directory)
        {
            PhrasePacks.Clear();

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                return;
            }

            foreach (var file in Directory.GetFiles(directory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var pack = JsonConvert.DeserializeObject<NationPhrasePack>(json);

                    if (pack != null && !string.IsNullOrWhiteSpace(pack.Nation))
                    {
                        PhrasePacks[pack.Nation] = pack;
                        HeliosContext.Instance.Log.Info($"Loaded phrase pack for nation: {pack.Nation}");
                    }
                    else
                    {
                        HeliosContext.Instance.Log.Warn($"Invalid or empty phrase pack: {file}");
                    }
                }
                catch (Exception ex)
                {
                    HeliosContext.Instance.Log?.Error($"Failed to load phrase pack '{file}': {ex.Message}");
                }
            }
        }

        public static NationPhrasePack GetPhrasePack(string nation)
        {
            if (PhrasePacks.TryGetValue(nation, out var pack))
                return pack;

            return null;
        }
    }
}