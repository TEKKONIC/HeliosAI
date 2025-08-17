using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Helios.Core;
using Helios.Modules.AI;
using Helios.Modules.AI.Ai.Control;
using Helios.Modules.AI.Behaviors;
using Helios.Modules.AI.Combat;
using Helios.Modules.AICommunication;
using Helios.Modules.Encounters;
using Helios.Modules.Nations;
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
using Helios.Modules.API; // <-- Add this for APIManager
using ICustomGridManager = Helios.Core.Interfaces.ICustomGridManager;

namespace HeliosAI
{
    public class HeliosAIPlugin : TorchPluginBase, IWpfPlugin, ICommonPlugin
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetLogger("HeliosAI");
        public const string PluginName = "HeliosAi";
        public static HeliosAIPlugin Instance { get; private set; }
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
        private AdaptiveBehaviorEngine _behaviorEngine;
        private PredictiveAnalyzer _predictiveAnalyzer;
        private Timer _aiUpdateTimer;

        private const string NpcStateFile = "HeliosAI_Npcs.xml";

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public override async void Init(ITorchBase torch)
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

                if (config?.Data?.Enabled == false)
                {
                    Logger.Info("Plugin is disabled in configuration");
                    return;
                }

                if (!InitializeConfiguration()) return;
                if (!await InitializeManagers(torch)) return; 
                if (!await InitializeGameIntegration()) return;
                
                _behaviorEngine = new AdaptiveBehaviorEngine();
                _predictiveAnalyzer = new PredictiveAnalyzer();

                NationHelper.InitializeRelationships();
                Logger.Info("Nation system initialized");

                // Update AI every 5 seconds (adjust as needed)
                _aiUpdateTimer = new Timer(UpdateAI, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                
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

        private bool InitializeConfiguration()
        {
            try
            {
                Logger.Debug("Loading configuration...");
                var configPath = Path.Combine(StoragePath, ConfigFileName);
                config = PersistentConfig<HeliosAIConfig>.Load(null, configPath);
                
                if (config?.Data == null)
                {
                    Logger.Warn("Configuration is null, creating default");
                    config = new PersistentConfig<HeliosAIConfig>(configPath, new HeliosAIConfig());
                }
                
                if (!config.Data.Validate())
                {
                    Logger.Warn("Configuration validation failed, resetting to defaults");
                    config.Data.ResetToDefaults();
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize configuration");
                failed = true;
                return false;
            }
        }

        private async Task<bool> InitializeManagers(ITorchBase torch)
        {
            try
            {
                Logger.Debug("Initializing ZoneManager...");
                ZoneManager = new ZoneManager();
                
                Logger.Debug("Initializing EncounterManager...");
                EncounterMgr = new EncounterManager();
                
                Logger.Debug("Initializing AiManager with configuration...");
                var aiManager = new AiManager(config.Data);

                Logger.Debug("Initializing HeliosContext...");
                await HeliosContext.Initialize(torch, ZoneManager, EncounterMgr, aiManager, Logger);
                
                if (config?.Data?.NpcStates != null)
                {
                    Logger.Info($"Loading {config.Data.NpcStates.Count} saved NPC states...");
                    foreach (var data in config.Data.NpcStates)
                    {
                        try
                        {
                            await aiManager.SpawnNpcAsync(data.Position, data.Prefab, data.Mood);
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Failed to spawn NPC from saved data: {data.Prefab}");
                        }
                    }
                }
                AiManager.Instance.LoadNpcStates(NpcStateFile);

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize managers");
                failed = true;
                return false;
            }
        }

        private Task<bool> InitializeGameIntegration()
        {
            try
            {
                var gameVersionNumber = MyPerGameSettings.BasicGameInfo.GameVersion ?? 0;
                var gameVersion = new StringBuilder(MyBuildNumbers.ConvertBuildNumberFromIntToString(gameVersionNumber)).ToString();
                Common.SetPlugin(this, gameVersion, StoragePath);

                #if USE_HARMONY
                Logger.Debug("Applying Harmony patches...");
                if (!PatchHelpers.HarmonyPatchAll(Log, new Harmony(Name)))
                 {
                     Logger.Error("Failed to apply Harmony patches");
                     failed = true;
                     return false;
                 }
                #endif
                
                sessionManager = Torch.Managers?.GetManager<TorchSessionManager>();
                if (sessionManager == null)
                {
                    Logger.Error("TorchSessionManager not found! Plugin cannot continue.");
                    failed = true;
                    return Task.FromResult(false);
                }
                sessionManager.SessionStateChanged += SessionStateChanged;

                var customGridManager = new CustomGridManager();
                HeliosContext.Instance.CustomGridManager = (ICustomGridManager)customGridManager;

                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize game integration");
                failed = true;
                return Task.FromResult(false);
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
                        Logger.Debug("Session loaded - initializing external APIs...");
                        APIManager.RegisterAPIs(0); // Nexus, etc.
                        APIManager.RegisterAPIs(2); // WeaponCore, Shields, etc.
                        break;

                    case TorchSessionState.Loading:
                        Logger.Debug("Session loading...");
                        break;

                    case TorchSessionState.Unloading:
                        Logger.Debug("Session unloading...");
                        OnSessionUnloading();
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
                    
                    AiManager.Instance.SaveNpcStates(NpcStateFile);
                    SaveNpcStates();
                    CleanupResources();
                    _aiUpdateTimer?.Dispose();

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
                Logger.Debug("Saving NPC states...");
                
                var aiManager = HeliosContext.Instance?.AiManager;
                if (aiManager.ActiveNpcs != null)
                {
                    foreach (var npc in aiManager.ActiveNpcs)
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
                // No need to null WeaponCoreManager, WCAPI, NexusApi here; handled by APIManager
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
                
                if (HeliosContext.Instance?.AiManager != null)
                {
                    HeliosContext.Instance.AiManager.CustomUpdate();
                }
                
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
            if (APIManager.WeaponCoreApiLoaded && APIManager.WeaponCoreManager != null)
            {
                // WeaponCore-specific logic can be added here
            }
        }

        private void UpdateNexusIntegration()
        {
            try
            {
                var aiManager = HeliosContext.Instance?.AiManager;
                if (aiManager?.ActiveNpcs == null) 
                    return;

                foreach (var npc in aiManager.ActiveNpcs)
                {
                    if (npc?.Grid == null) 
                        continue;

                    var zone = NexusZoneManager.GetZoneForPosition(((IMyEntity)npc.Grid).GetPosition());
                    var newMood = zone switch
                    {
                        NexusZone.Combat => NpcEntity.AiMood.Aggressive,
                        NexusZone.Safe => NpcEntity.AiMood.Passive,
                        NexusZone.Trade => NpcEntity.AiMood.Guard,
                        _ => NpcEntity.AiMood.Passive
                    };
                    
                    aiManager.SetNpcMood(npc, newMood);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating Nexus integration");
            }
        }

        /// <summary>
        /// Handles configuration changes at runtime
        /// </summary>
        public void OnConfigurationChanged()
        {
            try
            {
                Logger.Info("Configuration changed, applying hot-reload...");
                
                if (config?.Data != null)
                {
                    config.Data.ApplyHotReload();
                    config.Save();
                    Logger.Info("Configuration hot-reload completed");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply configuration hot-reload");
            }
        }

        private void UpdateAI(object state)
        {
            try
            {
                EncounterMgr?.UpdateEncounters(); 
                ZoneManager?.Tick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating AI");
            }
        }

        private void OnSessionUnloading()
        {
            Logger.Info("Torch session unloading, saving HeliosAI NPC states...");
            AiManager.Instance.SaveNpcStates(NpcStateFile);
            SaveNpcStates();
        }
    }
}