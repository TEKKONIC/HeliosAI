using System;
using System.IO;
using System.Threading.Tasks;
using Helios.Core.Interfaces;
using Torch.API;
using Helios.Modules.AI;
using Helios.Modules.AI.Ai.Control;
using HeliosAI.Phrases;
using ICustomGridManager = Helios.Core.Interfaces.ICustomGridManager;
using ILogger = NLog.ILogger;

namespace Helios.Core
{
    public class HeliosContext : IDisposable
    {
        private static readonly object _lock = new object();
        private static HeliosContext _instance;

        public static HeliosContext Instance
        {
            get
            {
                lock (_lock)
                {
                    return _instance;
                }
            }
            private set
            {
                lock (_lock)
                {
                    _instance = value;
                }
            }
        }

        public NationPhrasePackLoader PhraseLoader { get; private set; }
        public BroadcastManager BroadcastManager { get; private set; }

        public ITorchBase Torch { get; }
        public IZoneManager ZoneManager { get; }
        public IEncounterManager EncounterManager { get; }
        public IAiManager AiManager { get; }
        public ILogger Log { get; }
        public ICustomGridManager CustomGridManager { get; set; }

        private HeliosContext(ITorchBase torch, IZoneManager zoneManager, IEncounterManager encounterManager, IAiManager aiManager, ILogger logger)
        {
            Torch = torch ?? throw new ArgumentNullException(nameof(torch));
            ZoneManager = zoneManager ?? throw new ArgumentNullException(nameof(zoneManager));
            EncounterManager = encounterManager ?? throw new ArgumentNullException(nameof(encounterManager));
            AiManager = aiManager ?? throw new ArgumentNullException(nameof(aiManager));
            Log = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public static async Task Initialize(ITorchBase torch, IZoneManager zoneManager, IEncounterManager encounterManager, IAiManager aiManager, ILogger logger)
        {
            if (torch == null) throw new ArgumentNullException(nameof(torch));
            if (zoneManager == null) throw new ArgumentNullException(nameof(zoneManager));
            if (encounterManager == null) throw new ArgumentNullException(nameof(encounterManager));
            if (aiManager == null) throw new ArgumentNullException(nameof(aiManager));
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            lock (_lock)
            {
                if (_instance != null)
                    throw new InvalidOperationException("HeliosContext is already initialized.");
                _instance = new HeliosContext(torch, zoneManager, encounterManager, aiManager, logger);
            }

            Instance.PhraseLoader = new NationPhrasePackLoader();
            Instance.BroadcastManager = new BroadcastManager();

            try
            {
                var phrasePath = Path.Combine(torch.Config.InstancePath, "HeliosAI", "Phrases");
                Instance.PhraseLoader.LoadAll(phrasePath);
                logger.Info($"Loaded phrase packs from: {phrasePath}");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to load phrase packs.");
            }

            await zoneManager.InitializeAsync(torch);
            await encounterManager.InitializeAsync(torch);
            logger.Info("HeliosContext initialized.");
        }

        public void Dispose()
        {
            (PhraseLoader as IDisposable)?.Dispose();
            (BroadcastManager as IDisposable)?.Dispose();
        }
    }
}