using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using HeliosAI.Broadcasting;
using NLog;

namespace HeliosAI.Phrases
{
    /// <summary>
    /// Loads and manages NationPhrasePack resources from disk.
    /// </summary>
    public class NationPhrasePackLoader
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationPhrasePackLoader");
        public static readonly Dictionary<string, NationPhrasePack> PhrasePacks = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Loads all phrase packs from the specified directory.
        /// </summary>
        public void LoadAll(string directory)
        {
            PhrasePacks.Clear();

            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                Logger.Warn($"Phrase pack directory did not exist, created: {directory}");
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
                        Logger.Info($"Loaded phrase pack for nation: {pack.Nation}");
                    }
                    else
                    {
                        Logger.Warn($"Invalid or empty phrase pack: {file}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to load phrase pack '{file}'");
                }
            }
        }

        /// <summary>
        /// Gets the phrase pack for a given nation.
        /// </summary>
        public static NationPhrasePack GetPhrasePack(string nation)
        {
            if (string.IsNullOrWhiteSpace(nation))
            {
                Logger.Warn("Attempted to get phrase pack with empty nation identifier.");
                return null;
            }

            if (PhrasePacks.TryGetValue(nation, out var pack))
                return pack;

            Logger.Debug($"Phrase pack not found for nation: {nation}");
            return null;
        }
    }
}