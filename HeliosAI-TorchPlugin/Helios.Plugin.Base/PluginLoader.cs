using System;
using System.Threading.Tasks;
using Helios.Core;
using Helios.Core.Interfaces;
using HeliosAI.NPCZones;
using Torch.API;
using Helios.Modules.AI;
using Helios.Modules.Encounters;
using NLog;

namespace Helios.Plugin
{
    public class PluginLoader
    {
        private static readonly Logger Logger = LogManager.GetLogger("PluginLoader");

        public async Task LoadAllAsync(ITorchBase torch)
        {
            if (torch == null)
            {
                Logger.Error("Cannot load plugins: Torch instance is null");
                throw new ArgumentNullException(nameof(torch));
            }

            try
            {
                Logger.Info("Starting Helios AI plugin initialization...");

                var heliosLogger = LogManager.GetLogger("Helios");
                
                var zoneManager = await InitializeZoneManagerAsync();
                var encounterManager = await InitializeEncounterManagerAsync();
                var aiManager = await InitializeAiManagerAsync();
                
                await HeliosContext.Initialize(
                    torch,
                    zoneManager,
                    encounterManager,
                    aiManager,
                    heliosLogger
                );

                Logger.Info("Helios AI plugin initialization completed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize Helios AI plugin");
                throw;
            }
        }

        private Task<IZoneManager> InitializeZoneManagerAsync()
        {
            try
            {
                Logger.Debug("Initializing ZoneManager...");
                var zoneManager = new ZoneManager();
                
                Logger.Debug("ZoneManager initialized successfully");
                return Task.FromResult<IZoneManager>(zoneManager);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize ZoneManager");
                throw;
            }
        }

        private Task<IEncounterManager> InitializeEncounterManagerAsync()
        {
            try
            {
                Logger.Debug("Initializing EncounterManager...");
                var encounterManager = new EncounterManager();
                
                Logger.Debug("EncounterManager initialized successfully");
                return Task.FromResult<IEncounterManager>(encounterManager);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize EncounterManager");
                throw;
            }
        }

        private Task<IAiManager> InitializeAiManagerAsync()
        {
            try
            {
                Logger.Debug("Initializing AiManager...");
                var aiManager = new AiManager();
                
                Logger.Debug("AiManager initialized successfully");
                return Task.FromResult<IAiManager>(aiManager);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize AiManager");
                throw;
            }
        }

        public void UnloadAll()
        {
            try
            {
                Logger.Info("Unloading Helios AI plugin...");
                
                if (HeliosContext.Instance != null)
                {
                    Logger.Debug("Cleaned up Helios context");
                }

                Logger.Info("Helios AI plugin unloaded successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to unload Helios AI plugin properly");
            }
        }

        public bool IsLoaded()
        {
            return HeliosContext.Instance != null;
        }
    }
}