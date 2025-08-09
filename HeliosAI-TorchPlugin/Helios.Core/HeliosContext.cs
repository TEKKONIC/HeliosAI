using System;
using System.IO;
using System.Threading.Tasks;
using Helios.Core.Interfaces;
using Torch.API;
using Helios.Modules.AI;
using HeliosAI.Phrases;
using ILogger = NLog.ILogger;

namespace Helios.Core
{
    public class HeliosContext
    {
        public static HeliosContext Instance { get; private set; }
        public NationPhrasePackLoader PhraseLoader { get; private set; }
        public BroadcastManager BroadcastManager { get; private set; }

        public static ITorchBase Torch { get; set; }
        public IZoneManager ZoneManager { get; }
        public IEncounterManager EncounterManager { get; }
        public IAiManager AiManager { get; }
        public ILogger Log { get; }

        private HeliosContext(ITorchBase torch, IZoneManager zoneManager, IEncounterManager encounterManager, IAiManager aiManager, NLog.ILogger logger)
        {
            Torch = torch;
            ZoneManager = zoneManager;
            EncounterManager = encounterManager;
            AiManager = aiManager;
            Log = logger;
        }

        public static async Task Initialize(ITorchBase torch, IZoneManager zoneManager, IEncounterManager encounterManager, IAiManager aiManager, ILogger logger)
        {
            if (torch == null) throw new ArgumentNullException(nameof(torch));
            if (zoneManager == null) throw new ArgumentNullException(nameof(zoneManager));
            if (encounterManager == null) throw new ArgumentNullException(nameof(encounterManager));
            if (aiManager == null) throw new ArgumentNullException(nameof(aiManager));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
    
            Instance = new HeliosContext(torch, zoneManager, encounterManager, aiManager, logger);

            Instance.PhraseLoader = new NationPhrasePackLoader();
            Instance.BroadcastManager = new BroadcastManager();

            Instance.PhraseLoader.LoadAll(Path.Combine(Torch.Config.InstancePath, "HeliosAI", "Phrases"));

            await zoneManager.InitializeAsync(torch);
            await encounterManager.InitializeAsync(torch);
            logger.Info("HeliosContext initialized.");
        }
    }
}