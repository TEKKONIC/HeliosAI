using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using Helios.Core;
using Helios.Modules.AI;
using Helios.Modules.AICommunication;
using Helios.Modules.Encounters;
using HeliosAI.Nexus;
using HeliosAI.NPCZones;
using Sandbox.Game;
using Shared.Config;
using Shared.Logging;
using Shared.Patches;
using Shared.Plugin;
using Torch;
using Torch.API;
using Torch.API.Managers;
using Torch.API.Plugins;
using Torch.API.Session;
using Torch.Session;
using VRage.Game.ModAPI.Ingame;
using VRage.Utils;
using NexusModAPI;
using NLog;
using Sandbox.ModAPI;

namespace HeliosAI
{
    public class HeliosAIPlugin : TorchPluginBase, IWpfPlugin, ICommonPlugin
    {
        private static readonly Logger Logger = LogManager.GetLogger("HeliosAI");
        public const string PluginName = "HeliosAi";
        public static HeliosAIPlugin Instance { get; private set; }
        public NexusAPI NexusApi { get; private set; }
        public static bool NexusInstalled { get; private set; }
        public static WeaponCoreAdvancedAPI WCAPI { get; private set; } = new WeaponCoreAdvancedAPI();
        public static WeaponCoreGridManager WeaponCoreManager { get; private set; }
        public AiCommunicationManager CommunicationManager { get; private set; }
        public static EncounterManager EncounterMgr { get; private set; }
        public static List<NpcEntity> AiEntities { get; set; } = new();
        public static ZoneManager ZoneManager { get; private set; }
        public long Tick { get; private set; }
        public IPluginLogger Log { get; set; }
        public IPluginConfig Config => config?.Data;
        private PersistentConfig<HeliosAIConfig> config;
        private static readonly string ConfigFileName = $"{PluginName}.cfg";
        public UserControl GetControl() => control ??= new ConfigView();
        private ConfigView control;
        private TorchSessionManager sessionManager;
        
        private bool initialized;
        private bool failed;

        private readonly Commands commands = new Commands();

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override void Init(ITorchBase torch)
        {
            try
            {
                base.Init(torch);
                Instance = this;
                Thread.Sleep(100);
                
                Logger.Info($"HeliosAI Plugin v{Version} Starting initialization...");

                if (torch == null)
                {
                    Logger.Error("Torch instance is null during initialization");
                    failed = true;
                    return;
                }

                // Initialize managers
                Logger.Debug("Initializing ZoneManager...");
                ZoneManager = new ZoneManager();
                
                Logger.Debug("Initializing EncounterManager...");
                EncounterMgr = new EncounterManager();

                Logger.Debug("Initializing HeliosContext...");
                HeliosContext.Initialize(torch, ZoneManager, EncounterMgr, new AiManager(), Logger);

                Logger.Debug("Initializing CommunicationManager...");
                CommunicationManager = new AiCommunicationManager();

                // Load configuration
                Logger.Debug("Loading configuration...");
                var configPath = Path.Combine(StoragePath, ConfigFileName);
                config = PersistentConfig<HeliosAIConfig>.Load(null, configPath);

                // Load saved NPC states
                if (config?.Data?.NpcStates != null)
                {
                    Logger.Info($"Loading {config.Data.NpcStates.Count} saved NPC states...");
                    foreach (var data in config.Data.NpcStates)
                    {
                        try
                        {
                            AiManager.Instance.SpawnNpc(data.Position, data.Prefab, data.Mood);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Failed to spawn NPC from saved data: {data.Prefab}");
                        }
                    }
                }

                // Initialize game version info
                var gameVersionNumber = MyPerGameSettings.BasicGameInfo.GameVersion ?? 0;
                var gameVersion = new StringBuilder(MyBuildNumbers.ConvertBuildNumberFromIntToString(gameVersionNumber)).ToString();
                Common.SetPlugin(this, gameVersion, StoragePath);

#if USE_HARMONY
                Logger.Debug("Applying Harmony patches...");
                if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
                {
                    Logger.Error("Failed to apply Harmony patches");
                    failed = true;
                    return;
                }
#endif

                // Initialize session manager
                sessionManager = torch.Managers?.GetManager<TorchSessionManager>();
                if (sessionManager == null)
                {
                    Logger.Error("TorchSessionManager not found! Plugin cannot continue.");
                    failed = true;
                    return;
                }
                sessionManager.SessionStateChanged += SessionStateChanged;

                initialized = true;
                Logger.Info("HeliosAI Plugin initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize HeliosAI Plugin");
                failed = true;
                throw;
            }
        }

        private void SessionStateChanged(ITorchSession session, TorchSessionState newState)
        {
            if (session == null)
            {
                Logger.Warn("SessionStateChanged called with null session");
                return;
            }

            Logger.Info($"Session state changed to: {newState}");

            try
            {
                switch (newState)
                {
                    case TorchSessionState.Loaded:
                        Logger.Debug("Session loaded - initializing APIs...");
                        InitializeWeaponCoreAPI();
                        InitializeNexusAPI();
                        break;

                    case TorchSessionState.Loading:
                        Logger.Debug("Session loading...");
                        break;

                    case TorchSessionState.Unloading:
                        Logger.Debug("Session unloading...");
                        break;

                    case TorchSessionState.Unloaded:
                        Logger.Debug("Session unloaded");
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling session state change to: {newState}");
            }
        }

        private void InitializeWeaponCoreAPI()
        {
            try
            {
                if (WCAPI == null)
                {
                    Logger.Debug("WCAPI is null - skipping WeaponCore integration");
                    return;
                }

                var wcMod = MyAPIGateway.Session?.Mods?.FirstOrDefault(m => 
                    m.PublishedFileId == 1918681825 || 
                    m.Name.Contains("WeaponCore"));

                if (wcMod == null)
                {
                    Logger.Info("WeaponCore mod not found - skipping WeaponCore integration");
                    return;
                }

                Logger.Info("WeaponCore mod detected, attempting to load API...");
                WCAPI.LoadWeaponCoreAPI();

                if (WCAPI.IsReady)
                {
                    WeaponCoreManager = new WeaponCoreGridManager(WCAPI);
                    Logger.Info("WeaponCore API loaded successfully");
                }
                else
                {
                    Logger.Warn("WeaponCore API failed to initialize properly");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load WeaponCore API - WeaponCore integration will be disabled");
                WCAPI = null;
            }
        }

        private void InitializeNexusAPI()
        {
            try
            {
                if (NexusApi != null)
                {
                    Logger.Debug("NexusAPI already initialized");
                    return;
                }

                NexusApi = new NexusAPI(OnNexusEnabled);
                NexusInstalled = true;
                Logger.Info("NexusAPI initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize NexusAPI - Nexus integration will be disabled");
                NexusApi = null;
                NexusInstalled = false;
            }
        }

        private static void OnNexusEnabled()
        {
            Logger.Info("Nexus API enabled! Clusters and servers are now available.");
        }

        public override void Dispose()
        {
            try
            {
                if (initialized)
                {
                    Logger.Info("Disposing HeliosAI Plugin...");

                    if (sessionManager != null)
                    {
                        sessionManager.SessionStateChanged -= SessionStateChanged;
                        sessionManager = null;
                    }

                    SaveNpcStates();
                    CleanupResources();

                    Logger.Info("HeliosAI Plugin disposed successfully");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during plugin disposal");
            }
            finally
            {
                Instance = null;
                base.Dispose();
            }
        }

        private void SaveNpcStates()
        {
            try
            {
                if (config?.Data == null)
                {
                    Logger.Debug("No config data to save");
                    return;
                }

                config.Data.NpcStates.Clear();

                if (AiManager.Instance?.ActiveNpcs != null)
                {
                    foreach (var npc in AiManager.Instance.ActiveNpcs)
                    {
                        try
                        {
                            config.Data.NpcStates.Add(new NpcEntity.NpcData
                            {
                                Prefab = npc.SpawnedPrefab,
                                Position = ((IMyEntity)npc.Grid).GetPosition(),
                                Mood = npc.Mood
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Failed to save NPC state: {npc.SpawnedPrefab}");
                        }
                    }

                    Logger.Info($"Saved {config.Data.NpcStates.Count} NPC states");
                }

                config.Save();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save NPC states");
            }
        }

        private void CleanupResources()
        {
            try
            {
                CommunicationManager?.Dispose();
                WeaponCoreManager = null;
                WCAPI = null;
                NexusApi = null;
                Logger.Debug("Resources cleaned up successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during resource cleanup");
            }
        }

        public override void Update()
        {
            if (failed)
                return;

            try
            {
                ZoneManager?.Tick();
                CustomUpdate();
                Tick++;
            }
            catch (Exception ex)
            {
                Logger.Fatal(ex, "Update failed");
                failed = true;
            }
        }

        private void CustomUpdate()
        {
            try
            {
                PatchHelpers.PatchUpdates();
                UpdateWeaponCoreIntegration();
                UpdateNexusIntegration();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in custom update");
            }
        }

        private void UpdateWeaponCoreIntegration()
        {
            if (WCAPI?.IsReady == true && WeaponCoreManager != null)
            {
                // WeaponCore-specific logic can be added here
            }
        }

        private void UpdateNexusIntegration()
        {
            try
            {
                if (AiManager.Instance?.ActiveNpcs == null) 
                    return;

                foreach (var npc in AiManager.Instance.ActiveNpcs)
                {
                    if (npc?.Grid == null) 
                        continue;

                    var zone = NexusZoneManager.GetZoneForPosition(((IMyEntity)npc.Grid).GetPosition());
                    npc.Mood = zone switch
                    {
                        NexusZone.Combat => NpcEntity.AiMood.Aggressive,
                        NexusZone.Safe => NpcEntity.AiMood.Passive,
                        NexusZone.Trade => NpcEntity.AiMood.Guard,
                        _ => NpcEntity.AiMood.Passive
                    };
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating Nexus integration");
            }
        }
    }
}