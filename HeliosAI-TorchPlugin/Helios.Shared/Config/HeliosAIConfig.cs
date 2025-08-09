using System;
using System.Collections.Generic;
using Helios.Modules.AI;
using Shared.Config;
using Torch;
using Torch.Views;
using NLog;

namespace HeliosAI
{
    [Serializable]
    public class HeliosAIConfig : ViewModel, IPluginConfig
    {
        private static readonly Logger Logger = LogManager.GetLogger("HeliosAIConfig");
        
        private bool _enabled = true;
        private bool _detectCodeChanges = true;
        private string _defaultPrefab = "SpaceCargoShip";
        private int _maxNpcs = 10;
        private int _updateInterval = 1000;
        private double _spawnRange = 5000.0;
        private double _despawnRange = 10000.0;
        private bool _enableWeaponCore = true;
        private bool _enableNexusIntegration = true;
        private int _maxEncounters = 5;
        private bool _debugLogging = false;

        public List<NpcEntity.NpcData> NpcStates { get; set; } = new List<NpcEntity.NpcData>();

        [Display(Order = 1, GroupName = "General", Name = "Enable plugin", Description = "Enable the plugin")]
        public bool Enabled
        {
            get => _enabled;
            set => SetValue(ref _enabled, value);
        }

        [Display(Order = 2, GroupName = "General", Name = "Detect code changes", Description = "Disable the plugin if any changes to the game code are detected before patching")]
        public bool DetectCodeChanges
        {
            get => _detectCodeChanges;
            set => SetValue(ref _detectCodeChanges, value);
        }

        [Display(Order = 3, GroupName = "General", Name = "Debug Logging", Description = "Enable detailed debug logging")]
        public bool DebugLogging
        {
            get => _debugLogging;
            set => SetValue(ref _debugLogging, value);
        }

        [Display(Order = 10, GroupName = "NPCs", Name = "Default Prefab", Description = "Default prefab to spawn for NPCs")]
        public string DefaultPrefab
        {
            get => _defaultPrefab;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Warn("Attempted to set empty default prefab, using fallback");
                    value = "SpaceCargoShip";
                }
                SetValue(ref _defaultPrefab, value);
            }
        }

        [Display(Order = 11, GroupName = "NPCs", Name = "Max Active NPCs", Description = "Maximum number of active NPCs")]
        public int MaxNpcs
        {
            get => _maxNpcs;
            set
            {
                if (value < 0)
                {
                    Logger.Warn($"Invalid max NPCs value: {value}, setting to 0");
                    value = 0;
                }
                else if (value > 100)
                {
                    Logger.Warn($"Max NPCs value too high: {value}, capping at 100");
                    value = 100;
                }
                SetValue(ref _maxNpcs, value);
            }
        }

        [Display(Order = 12, GroupName = "NPCs", Name = "AI Update Interval (ms)", Description = "How often AI logic updates (milliseconds)")]
        public int UpdateInterval
        {
            get => _updateInterval;
            set
            {
                if (value < 100)
                {
                    Logger.Warn($"Update interval too low: {value}ms, setting to 100ms");
                    value = 100;
                }
                else if (value > 60000)
                {
                    Logger.Warn($"Update interval too high: {value}ms, setting to 60000ms");
                    value = 60000;
                }
                SetValue(ref _updateInterval, value);
            }
        }

        [Display(Order = 13, GroupName = "NPCs", Name = "Spawn Range", Description = "Distance from players to spawn NPCs (meters)")]
        public double SpawnRange
        {
            get => _spawnRange;
            set
            {
                if (value < 1000.0)
                {
                    Logger.Warn($"Spawn range too low: {value}m, setting to 1000m");
                    value = 1000.0;
                }
                else if (value > 50000.0)
                {
                    Logger.Warn($"Spawn range too high: {value}m, setting to 50000m");
                    value = 50000.0;
                }
                SetValue(ref _spawnRange, value);
            }
        }

        [Display(Order = 14, GroupName = "NPCs", Name = "Despawn Range", Description = "Distance from players to despawn NPCs (meters)")]
        public double DespawnRange
        {
            get => _despawnRange;
            set
            {
                if (value < _spawnRange)
                {
                    Logger.Warn($"Despawn range ({value}m) less than spawn range ({_spawnRange}m), adjusting");
                    value = _spawnRange * 1.5;
                }
                else if (value > 100000.0)
                {
                    Logger.Warn($"Despawn range too high: {value}m, setting to 100000m");
                    value = 100000.0;
                }
                SetValue(ref _despawnRange, value);
            }
        }

        [Display(Order = 20, GroupName = "Encounters", Name = "Max Encounters", Description = "Maximum number of active encounters")]
        public int MaxEncounters
        {
            get => _maxEncounters;
            set
            {
                if (value < 0)
                {
                    Logger.Warn($"Invalid max encounters value: {value}, setting to 0");
                    value = 0;
                }
                else if (value > 50)
                {
                    Logger.Warn($"Max encounters value too high: {value}, capping at 50");
                    value = 50;
                }
                SetValue(ref _maxEncounters, value);
            }
        }

        [Display(Order = 30, GroupName = "Integration", Name = "Enable WeaponCore", Description = "Enable WeaponCore integration if available")]
        public bool EnableWeaponCore
        {
            get => _enableWeaponCore;
            set => SetValue(ref _enableWeaponCore, value);
        }

        [Display(Order = 31, GroupName = "Integration", Name = "Enable Nexus Integration", Description = "Enable Nexus multi-server integration if available")]
        public bool EnableNexusIntegration
        {
            get => _enableNexusIntegration;
            set => SetValue(ref _enableNexusIntegration, value);
        }

        /// <summary>
        /// Validates the configuration and logs any issues
        /// </summary>
        public bool Validate()
        {
            try
            {
                var isValid = true;

                if (string.IsNullOrWhiteSpace(DefaultPrefab))
                {
                    Logger.Error("Default prefab cannot be empty");
                    isValid = false;
                }

                if (MaxNpcs < 0)
                {
                    Logger.Error($"Max NPCs cannot be negative: {MaxNpcs}");
                    isValid = false;
                }

                if (UpdateInterval < 100)
                {
                    Logger.Error($"Update interval too low: {UpdateInterval}ms");
                    isValid = false;
                }

                if (SpawnRange <= 0)
                {
                    Logger.Error($"Spawn range must be positive: {SpawnRange}");
                    isValid = false;
                }

                if (DespawnRange < SpawnRange)
                {
                    Logger.Error($"Despawn range ({DespawnRange}) must be >= spawn range ({SpawnRange})");
                    isValid = false;
                }

                if (isValid)
                {
                    Logger.Debug("Configuration validation passed");
                }

                return isValid;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to validate configuration");
                return false;
            }
        }

        /// <summary>
        /// Resets configuration to default values
        /// </summary>
        public void ResetToDefaults()
        {
            try
            {
                Logger.Info("Resetting configuration to defaults");
                
                Enabled = true;
                DetectCodeChanges = true;
                DefaultPrefab = "SpaceCargoShip";
                MaxNpcs = 10;
                UpdateInterval = 1000;
                SpawnRange = 5000.0;
                DespawnRange = 10000.0;
                MaxEncounters = 5;
                EnableWeaponCore = true;
                EnableNexusIntegration = true;
                DebugLogging = false;
                
                NpcStates?.Clear();
                
                Logger.Info("Configuration reset to defaults completed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to reset configuration to defaults");
            }
        }

        /// <summary>
        /// Gets a summary of the current configuration
        /// </summary>
        public string GetConfigSummary()
        {
            try
            {
                return $"HeliosAI Config - Enabled: {Enabled}, Max NPCs: {MaxNpcs}, " +
                       $"Update Interval: {UpdateInterval}ms, Spawn Range: {SpawnRange}m, " +
                       $"WeaponCore: {EnableWeaponCore}, Nexus: {EnableNexusIntegration}";
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to generate config summary");
                return "Config summary unavailable";
            }
        }
    }
}