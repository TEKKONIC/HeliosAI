using System;
using System.Collections.Generic;
using System.ComponentModel;
using HeliosAI.Models;
using NLog;

namespace HeliosAI
{
    /// <summary>
    /// Core configuration definition for Helios AI system
    /// </summary>
    [Serializable]
    public class HeliosConfigDefinition
    {
        private static readonly Logger Logger = LogManager.GetLogger("HeliosConfigDefinition");

        private bool _enableDebugLogs = false;
        private double _globalDetectionRange = 1500.0;
        private bool _enableVisualRadar = true;
        private string _defaultFactionTag = "SPRT";

        /// <summary>
        /// Enable detailed debug logging
        /// </summary>
        [Description("Enable detailed debug logging for troubleshooting")]
        public bool EnableDebugLogs
        {
            get => _enableDebugLogs;
            set
            {
                _enableDebugLogs = value;
                Logger.Debug($"Debug logs {(value ? "enabled" : "disabled")}");
            }
        }

        /// <summary>
        /// Global detection range for AI entities
        /// </summary>
        [Description("Maximum range for AI detection systems (meters)")]
        public double GlobalDetectionRange
        {
            get => _globalDetectionRange;
            set
            {
                if (value < 100.0)
                {
                    Logger.Warn($"Detection range too low: {value}m, setting to 100m");
                    value = 100.0;
                }
                else if (value > 50000.0)
                {
                    Logger.Warn($"Detection range too high: {value}m, setting to 50000m");
                    value = 50000.0;
                }
                _globalDetectionRange = value;
            }
        }

        /// <summary>
        /// Enable visual radar display
        /// </summary>
        [Description("Enable visual radar overlay for AI detection")]
        public bool EnableVisualRadar
        {
            get => _enableVisualRadar;
            set => _enableVisualRadar = value;
        }

        /// <summary>
        /// Default faction tag for AI entities
        /// </summary>
        [Description("Default faction tag used for spawned AI entities")]
        public string DefaultFactionTag
        {
            get => _defaultFactionTag;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Warn("Default faction tag cannot be empty, using fallback");
                    value = "SPRT";
                }
                else if (value.Length > 4)
                {
                    Logger.Warn($"Faction tag too long: {value}, truncating to 4 characters");
                    value = value.Substring(0, 4);
                }
                _defaultFactionTag = value.ToUpperInvariant();
            }
        }

        /// <summary>
        /// Predefined encounter profiles
        /// </summary>
        [Description("Dictionary of preset encounter configurations")]
        public Dictionary<string, EncounterProfile> PresetEncounters { get; set; } = new Dictionary<string, EncounterProfile>();

        /// <summary>
        /// Advanced AI behavior settings
        /// </summary>
        [Description("Advanced configuration for AI behavior")]
        public AiBehaviorSettings BehaviorSettings { get; set; } = new AiBehaviorSettings();

        /// <summary>
        /// Performance-related settings
        /// </summary>
        [Description("Performance optimization settings")]
        public PerformanceSettings Performance { get; set; } = new PerformanceSettings();

        /// <summary>
        /// Validates the configuration and returns any errors
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();

            try
            {
                if (GlobalDetectionRange <= 0)
                {
                    errors.Add($"Global detection range must be positive: {GlobalDetectionRange}");
                }

                if (string.IsNullOrWhiteSpace(DefaultFactionTag))
                {
                    errors.Add("Default faction tag cannot be empty");
                }

                if (PresetEncounters == null)
                {
                    errors.Add("Preset encounters dictionary cannot be null");
                }

                if (BehaviorSettings == null)
                {
                    errors.Add("Behavior settings cannot be null");
                }
                else
                {
                    errors.AddRange(BehaviorSettings.Validate());
                }

                if (Performance == null)
                {
                    errors.Add("Performance settings cannot be null");
                }
                else
                {
                    errors.AddRange(Performance.Validate());
                }

                if (errors.Count == 0)
                {
                    Logger.Debug("Configuration validation passed");
                }
                else
                {
                    Logger.Warn($"Configuration validation found {errors.Count} errors");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to validate configuration");
                errors.Add("Validation failed due to exception");
            }

            return errors;
        }

        /// <summary>
        /// Resets configuration to default values
        /// </summary>
        public void ResetToDefaults()
        {
            try
            {
                Logger.Info("Resetting Helios configuration to defaults");

                EnableDebugLogs = false;
                GlobalDetectionRange = 1500.0;
                EnableVisualRadar = true;
                DefaultFactionTag = "SPRT";
                
                PresetEncounters.Clear();
                BehaviorSettings = new AiBehaviorSettings();
                Performance = new PerformanceSettings();

                Logger.Info("Configuration reset completed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to reset configuration to defaults");
            }
        }

        /// <summary>
        /// Creates a copy of the current configuration
        /// </summary>
        public HeliosConfigDefinition Clone()
        {
            try
            {
                return new HeliosConfigDefinition
                {
                    EnableDebugLogs = this.EnableDebugLogs,
                    GlobalDetectionRange = this.GlobalDetectionRange,
                    EnableVisualRadar = this.EnableVisualRadar,
                    DefaultFactionTag = this.DefaultFactionTag,
                    PresetEncounters = new Dictionary<string, EncounterProfile>(this.PresetEncounters),
                    BehaviorSettings = this.BehaviorSettings?.Clone() ?? new AiBehaviorSettings(),
                    Performance = this.Performance?.Clone() ?? new PerformanceSettings()
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clone configuration");
                return new HeliosConfigDefinition();
            }
        }
    }

    /// <summary>
    /// AI behavior configuration settings
    /// </summary>
    [Serializable]
    public class AiBehaviorSettings
    {
        public double AggressionLevel { get; set; } = 0.5;
        public double PatrolRadius { get; set; } = 2000.0;
        public int MaxTargets { get; set; } = 3;
        public bool UseFormations { get; set; } = true;

        public List<string> Validate()
        {
            var errors = new List<string>();
            
            if (AggressionLevel < 0.0 || AggressionLevel > 1.0)
                errors.Add($"Aggression level must be between 0.0 and 1.0: {AggressionLevel}");
            
            if (PatrolRadius <= 0)
                errors.Add($"Patrol radius must be positive: {PatrolRadius}");
            
            if (MaxTargets < 1)
                errors.Add($"Max targets must be at least 1: {MaxTargets}");

            return errors;
        }

        public AiBehaviorSettings Clone()
        {
            return new AiBehaviorSettings
            {
                AggressionLevel = this.AggressionLevel,
                PatrolRadius = this.PatrolRadius,
                MaxTargets = this.MaxTargets,
                UseFormations = this.UseFormations
            };
        }
    }

    /// <summary>
    /// Performance optimization settings
    /// </summary>
    [Serializable]
    public class PerformanceSettings
    {
        public int UpdateFrequencyMs { get; set; } = 1000;
        public int MaxConcurrentOperations { get; set; } = 5;
        public bool UseThreading { get; set; } = true;
        public double CullingDistance { get; set; } = 20000.0;

        public List<string> Validate()
        {
            var errors = new List<string>();
            
            if (UpdateFrequencyMs < 100)
                errors.Add($"Update frequency too low: {UpdateFrequencyMs}ms");
            
            if (MaxConcurrentOperations < 1)
                errors.Add($"Max concurrent operations must be at least 1: {MaxConcurrentOperations}");
            
            if (CullingDistance <= 0)
                errors.Add($"Culling distance must be positive: {CullingDistance}");

            return errors;
        }

        public PerformanceSettings Clone()
        {
            return new PerformanceSettings
            {
                UpdateFrequencyMs = this.UpdateFrequencyMs,
                MaxConcurrentOperations = this.MaxConcurrentOperations,
                UseThreading = this.UseThreading,
                CullingDistance = this.CullingDistance
            };
        }
    }
}