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
        
        [Display(Order = 15, GroupName = "NPCs", Name = "Max Speed", Description = "Maximum speed for NPC movement")]
        public float MaxSpeed
        {
            get => _maxSpeed;
            set
            {
                if (value < 1f)
                {
                    Logger.Warn($"Max speed too low: {value}, setting to 1");
                    value = 1f;
                }
                else if (value > 200f)
                {
                    Logger.Warn($"Max speed too high: {value}, setting to 200");
                    value = 200f;
                }
                SetValue(ref _maxSpeed, value);
            }
        }
        private float _maxSpeed = 10f;

        [Display(Order = 16, GroupName = "NPCs", Name = "Arrive Distance", Description = "Distance at which NPCs consider they've arrived at their target")]
        public float ArriveDistance
        {
            get => _arriveDistance;
            set
            {
                if (value < 10f)
                {
                    Logger.Warn($"Arrive distance too low: {value}, setting to 10");
                    value = 10f;
                }
                else if (value > 1000f)
                {
                    Logger.Warn($"Arrive distance too high: {value}, setting to 1000");
                    value = 1000f;
                }
                SetValue(ref _arriveDistance, value);
            }
        }
        private float _arriveDistance = 50f;

        [Display(Order = 17, GroupName = "NPCs", Name = "Max Plugin Failures", Description = "Maximum failures before disabling a plugin")]
        public int MaxPluginFailures
        {
            get => _maxPluginFailures;
            set
            {
                if (value < 1)
                {
                    Logger.Warn($"Max plugin failures too low: {value}, setting to 1");
                    value = 1;
                }
                else if (value > 100)
                {
                    Logger.Warn($"Max plugin failures too high: {value}, setting to 100");
                    value = 100;
                }
                SetValue(ref _maxPluginFailures, value);
            }
        }
        private int _maxPluginFailures = 5;

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
        
        [Display(Order = 40, GroupName = "Performance", Name = "Enable Performance Monitoring", Description = "Track and log performance metrics")]
        public bool EnablePerformanceMonitoring
        {
            get => _enablePerformanceMonitoring;
            set => SetValue(ref _enablePerformanceMonitoring, value);
        }
        private bool _enablePerformanceMonitoring = true;

        [Display(Order = 41, GroupName = "Performance", Name = "Performance Warning Threshold (ms)", Description = "Log warning if updates take longer than this")]
        public double PerformanceWarningThreshold
        {
            get => _performanceWarningThreshold;
            set
            {
                if (value < 10.0)
                {
                    Logger.Warn($"Performance threshold too low: {value}ms, setting to 10ms");
                    value = 10.0;
                }
                else if (value > 1000.0)
                {
                    Logger.Warn($"Performance threshold too high: {value}ms, setting to 1000ms");
                    value = 1000.0;
                }
                SetValue(ref _performanceWarningThreshold, value);
            }
        }
        private double _performanceWarningThreshold = 50.0;

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

                if (MaxSpeed <= 0)
                {
                    Logger.Error($"Max speed must be positive: {MaxSpeed}");
                    isValid = false;
                }

                if (ArriveDistance <= 0)
                {
                    Logger.Error($"Arrive distance must be positive: {ArriveDistance}");
                    isValid = false;
                }

                if (MaxPluginFailures <= 0)
                {
                    Logger.Error($"Max plugin failures must be positive: {MaxPluginFailures}");
                    isValid = false;
                }

                if (PerformanceWarningThreshold <= 0)
                {
                    Logger.Error($"Performance warning threshold must be positive: {PerformanceWarningThreshold}");
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
        /// Event fired when configuration changes that require AI Manager restart
        /// </summary>
        public event Action<HeliosAIConfig> ConfigurationChanged;

        /// <summary>
        /// Applies configuration changes that can be hot-reloaded
        /// </summary>
        public void ApplyHotReload()
        {
            try
            {
                Logger.Info("Applying hot-reload configuration changes");
                ConfigurationChanged?.Invoke(this);
        
                Logger.Info("Hot-reload configuration changes applied");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply hot-reload configuration changes");
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
                MaxSpeed = 10f;
                ArriveDistance = 50f;
                MaxPluginFailures = 5;
                EnablePerformanceMonitoring = true;
                PerformanceWarningThreshold = 50.0;
                
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
    
    /// <summary>
    /// Custom grid configuration for AI
    /// </summary>
     public class AIGridConfig
    {
        public List<CustomGridEntry> CustomGrids { get; set; } = new List<CustomGridEntry>();

        public class CustomGridEntry
        {
            public string Name { get; set; }
            public string BlueprintName { get; set; }
            public string AIBehavior { get; set; }
            public bool Enabled { get; set; } = true;
        }
    }
}