using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeliosAI.Models;
using Helios.Core.Interfaces;
using VRageMath;
using NLog;
using Torch.API;
using Newtonsoft.Json;

namespace Helios.Modules.Encounters
{
    public class EncounterManager : IEncounterManager
    {
        private static readonly Logger Logger = LogManager.GetLogger("EncounterManager");
        private readonly List<ActiveEncounter> _activeEncounters = new List<ActiveEncounter>();
        private readonly Dictionary<long, bool> _playerEncounterSettings = new Dictionary<long, bool>();
        private Dictionary<string, EncounterProfile> _profiles = new Dictionary<string, EncounterProfile>();
        private float _spawnRateModifier = 1.0f;
        private readonly Random _random = new Random();
        private ITorchBase _torch;
        private string _encounterDirectory;

        #region Properties

        public IReadOnlyDictionary<string, EncounterProfile> Profiles => _profiles;
        public IReadOnlyList<ActiveEncounter> ActiveEncounters => _activeEncounters.AsReadOnly();
        public int MaxConcurrentEncounters { get; set; } = 10;

        #endregion

        #region Initialization

        public Task InitializeAsync(ITorchBase torch)
        {
            try
            {
                _torch = torch ?? throw new ArgumentNullException(nameof(torch));
        
                // Fix: Use Config.InstancePath instead of InstancePath
                _encounterDirectory = Path.Combine(_torch.Config.InstancePath, "HeliosAI", "Encounters");
        
                // Create directory if it doesn't exist
                Directory.CreateDirectory(_encounterDirectory);
        
                Logger.Info($"EncounterManager initialized with directory: {_encounterDirectory}");
        
                // Load profiles on initialization
                LoadProfiles();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing EncounterManager");
            }

            return Task.CompletedTask;
        }

        public void LoadProfiles(string directory = "Instance/HeliosAI/Encounters")
        {
            try
            {
                // Use provided directory or fall back to initialized directory
                var targetDirectory = directory;
                if (directory == "Instance/HeliosAI/Encounters" && !string.IsNullOrEmpty(_encounterDirectory))
                {
                    targetDirectory = _encounterDirectory;
                }
                else if (_torch != null)
                {
                    // Fix: Use Config.InstancePath instead of InstancePath
                    targetDirectory = Path.Combine(_torch.Config.InstancePath, directory.Replace("Instance/", ""));
                }

                if (!Directory.Exists(targetDirectory))
                {
                    Logger.Warn($"Encounter directory does not exist: {targetDirectory}");
                    Directory.CreateDirectory(targetDirectory);
                    CreateDefaultProfiles(targetDirectory);
                    return;
                }

                _profiles.Clear();
                var profileFiles = Directory.GetFiles(targetDirectory, "*.json", SearchOption.AllDirectories);
        
                foreach (var file in profileFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var profile = JsonConvert.DeserializeObject<EncounterProfile>(json);
                
                        if (profile != null && !string.IsNullOrEmpty(profile.Id))
                        {
                            _profiles[profile.Id] = profile;
                            Logger.Debug($"Loaded encounter profile: {profile.Id} from {Path.GetFileName(file)}");
                        }
                        else
                        {
                            Logger.Warn($"Invalid encounter profile in file: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error loading encounter profile from file: {file}");
                    }
                }
        
                Logger.Info($"Loaded {_profiles.Count} encounter profiles from {targetDirectory}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error loading encounter profiles from directory: {directory}");
            }
        }

        public void ReloadProfiles()
        {
            try
            {
                LoadProfiles(); // Call LoadProfiles method
                Logger.Info($"Reloaded {Profiles.Count} encounter profiles");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reloading encounter profiles");
            }
        }

        #endregion

        #region Profile Management

        public EncounterProfile GetProfile(string id)
        {
            try
            {
                if (string.IsNullOrEmpty(id))
                {
                    Logger.Warn("GetProfile called with null or empty ID");
                    return null;
                }

                _profiles.TryGetValue(id, out var profile);
                
                if (profile == null)
                {
                    Logger.Debug($"Encounter profile not found: {id}");
                }
                
                return profile;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting encounter profile: {id}");
                return null;
            }
        }

        public List<EncounterProfile> GetProfilesByFilter(EncounterDifficulty? difficulty = null, EncounterType? encounterType = null)
        {
            try
            {
                return Profiles.Values.Where(p => 
                    (difficulty == null || p.Difficulty == difficulty) &&
                    (encounterType == null || p.EncounterType == encounterType)
                ).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error filtering encounter profiles");
                return new List<EncounterProfile>();
            }
        }

        public EncounterProfile GetRandomProfile(EncounterDifficulty? difficulty = null, EncounterType? encounterType = null)
        {
            try
            {
                var filtered = GetProfilesByFilter(difficulty, encounterType);
                return filtered.Count > 0 ? filtered[_random.Next(filtered.Count)] : null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting random encounter profile");
                return null;
            }
        }

        #endregion

        #region Spawn Management

        public Vector3D GetSpawnPosition(Vector3D origin, double minDistance, double maxDistance, bool avoidPlayers = true, bool avoidGrids = true)
        {
            try
            {
                const int maxAttempts = 50;
        
                for (var i = 0; i < maxAttempts; i++)
                {
                    // Generate random direction manually
                    var direction = CreateRandomDirection();
                    var distance = minDistance + _random.NextDouble() * (maxDistance - minDistance);
                    var position = origin + direction * distance;
            
                    if (IsPositionSafe(position, 500))
                    {
                        return position;
                    }
                }
        
                // Fallback: return position at max distance
                var fallbackDirection = CreateRandomDirection();
                return origin + fallbackDirection * maxDistance;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating spawn position");
                return origin + Vector3D.Forward * maxDistance;
            }
        }

        public bool IsPositionSafe(Vector3D position, double radius = 500)
        {
            try
            {
                // TODO: Implement actual collision checking
                // Check for nearby players, grids, planets, etc.
                // For now, return true as placeholder
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking position safety");
                return false;
            }
        }
        
        private Vector3D CreateRandomDirection()
        {
            try
            {
                // Generate random spherical coordinates
                var theta = _random.NextDouble() * 2.0 * Math.PI; // Azimuth angle (0 to 2π)
                var phi = Math.Acos(2.0 * _random.NextDouble() - 1.0); // Polar angle (0 to π)
        
                // Convert to Cartesian coordinates
                var x = Math.Sin(phi) * Math.Cos(theta);
                var y = Math.Sin(phi) * Math.Sin(theta);
                var z = Math.Cos(phi);
        
                return new Vector3D(x, y, z);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating random direction");
                return Vector3D.Forward; // Fallback direction
            }
        }

        public ActiveEncounter SpawnEncounter(string profileId, Vector3D position, long playerId = 0)
        {
            try
            {
                if (!Profiles.TryGetValue(profileId, out var profile))
                {
                    Logger.Warn($"Encounter profile not found: {profileId}");
                    return null;
                }

                if (_activeEncounters.Count >= MaxConcurrentEncounters)
                {
                    Logger.Warn("Maximum concurrent encounters reached");
                    return null;
                }

                var encounter = new ActiveEncounter
                {
                    Id = Guid.NewGuid().ToString(),
                    ProfileId = profileId,
                    Position = position,
                    SpawnTime = DateTime.UtcNow,
                    TriggeringPlayerId = playerId,
                    Status = EncounterStatus.Active,
                    LastUpdate = DateTime.UtcNow
                };

                // TODO: Spawn the actual entities based on profile
                // This would involve calling prefab spawning logic
                
                _activeEncounters.Add(encounter);
                Logger.Info($"Spawned encounter {profileId} at {position}");
                
                return encounter;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error spawning encounter {profileId}");
                return null;
            }
        }

        public ActiveEncounter SpawnRandomEncounter(long playerId, EncounterDifficulty difficulty = EncounterDifficulty.Medium, EncounterType encounterType = EncounterType.Patrol)
        {
            try
            {
                if (!CanSpawnForPlayer(playerId))
                {
                    Logger.Debug($"Encounters disabled for player {playerId}");
                    return null;
                }

                var profile = GetRandomProfile(difficulty, encounterType);
                if (profile == null)
                {
                    Logger.Warn($"No encounter profiles found for {difficulty} {encounterType}");
                    return null;
                }

                // TODO: Get player position
                var playerPosition = Vector3D.Zero; // Replace with actual player position lookup
                var spawnPosition = GetSpawnPosition(playerPosition, 2000, 5000);
                
                return SpawnEncounter(profile.Id, spawnPosition, playerId);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error spawning random encounter");
                return null;
            }
        }

        #endregion

        #region Encounter Lifecycle

        public void UpdateEncounters()
        {
            try
            {
                foreach (var encounter in _activeEncounters.ToList())
                {
                    try
                    {
                        encounter.LastUpdate = DateTime.UtcNow;
                        
                        // Check if encounter should expire
                        if (encounter.IsExpired(TimeSpan.FromMinutes(30))) // 30 min default
                        {
                            encounter.Status = EncounterStatus.Expired;
                            encounter.StatusReason = "Timeout";
                        }
                        
                        // TODO: Update encounter logic based on spawned entities
                        // Check if entities still exist, update behaviors, etc.
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error updating encounter {encounter.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating encounters");
            }
        }

        public void CleanupEncounters()
        {
            try
            {
                var toRemove = _activeEncounters.Where(e => 
                    e.Status != EncounterStatus.Active).ToList();
                
                foreach (var encounter in toRemove)
                {
                    _activeEncounters.Remove(encounter);
                    Logger.Debug($"Cleaned up encounter {encounter.Id} (Status: {encounter.Status})");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error cleaning up encounters");
            }
        }

        public void DespawnEncounter(string encounterId, string reason = "Manual")
        {
            try
            {
                var encounter = _activeEncounters.FirstOrDefault(e => e.Id == encounterId);
                if (encounter != null)
                {
                    encounter.Status = EncounterStatus.Despawned;
                    encounter.StatusReason = reason;
                    
                    // TODO: Despawn actual entities
                    foreach (var entityId in encounter.SpawnedEntityIds.ToList())
                    {
                        // Remove entities from game world
                    }
                    
                    Logger.Info($"Despawned encounter {encounterId}: {reason}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error despawning encounter {encounterId}");
            }
        }

        #endregion

        #region Query Methods

        public List<ActiveEncounter> GetEncountersInRange(Vector3D position, double range)
        {
            try
            {
                return _activeEncounters.Where(e => 
                    Vector3D.Distance(e.Position, position) <= range).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting encounters in range");
                return new List<ActiveEncounter>();
            }
        }

        public EncounterStatistics GetStatistics()
        {
            try
            {
                var completedCount = _activeEncounters.Count(e => e.Status == EncounterStatus.Completed);
                var failedCount = _activeEncounters.Count(e => e.Status == EncounterStatus.Failed);
                var activeCount = _activeEncounters.Count(e => e.Status == EncounterStatus.Active);
                
                var avgDuration = TimeSpan.Zero;
                var completedEncounters = _activeEncounters.Where(e => e.Status == EncounterStatus.Completed).ToList();
                if (completedEncounters.Count > 0)
                {
                    var totalTicks = completedEncounters.Sum(e => e.Age.Ticks);
                    avgDuration = new TimeSpan(totalTicks / completedEncounters.Count);
                }

                return new EncounterStatistics
                {
                    TotalProfilesLoaded = Profiles.Count,
                    ActiveEncounters = activeCount,
                    TotalEncountersSpawned = _activeEncounters.Count,
                    EncountersCompleted = completedCount,
                    EncountersFailed = failedCount,
                    SpawnRateModifier = _spawnRateModifier,
                    LastSpawn = _activeEncounters.LastOrDefault()?.SpawnTime ?? DateTime.MinValue,
                    AverageEncounterDuration = avgDuration
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating encounter statistics");
                return new EncounterStatistics();
            }
        }

        #endregion

        #region Settings Management

        public void SetSpawnRateModifier(float modifier)
        {
            try
            {
                _spawnRateModifier = Math.Max(0f, modifier);
                Logger.Info($"Spawn rate modifier set to: {modifier:P}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error setting spawn rate modifier");
            }
        }

        public bool CanSpawnForPlayer(long playerId)
        {
            try
            {
                return _playerEncounterSettings.GetValueOrDefault(playerId, true);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking spawn permission for player {playerId}");
                return true; // Default to enabled
            }
        }

        public void SetPlayerEncountersEnabled(long playerId, bool enabled)
        {
            try
            {
                _playerEncounterSettings[playerId] = enabled;
                Logger.Info($"Encounters {(enabled ? "enabled" : "disabled")} for player {playerId}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error setting encounter permission for player {playerId}");
            }
        }

        #endregion

        #region Helper Methods

        private void CreateDefaultProfiles(string directory)
        {
            try
            {
                var defaultProfiles = new List<EncounterProfile>
                {
                    new EncounterProfile
                    {
                        Id = "basic_patrol",
                        Name = "Basic Patrol",
                        Description = "A simple patrol encounter",
                        Difficulty = EncounterDifficulty.Easy,
                        EncounterType = EncounterType.Patrol,
                        PrefabName = "BasicPatrolShip",
                        SpawnChance = 0.3f,
                        MaxInstances = 2,
                        DespawnDistance = 10000,
                        RequiredPlayerDistance = 2000
                    },
                    new EncounterProfile
                    {
                        Id = "pirate_raid",
                        Name = "Pirate Raid",
                        Description = "Aggressive pirate encounter",
                        Difficulty = EncounterDifficulty.Medium,
                        EncounterType = EncounterType.Pirate,
                        PrefabName = "PirateRaider",
                        SpawnChance = 0.2f,
                        MaxInstances = 1,
                        DespawnDistance = 15000,
                        RequiredPlayerDistance = 3000
                    },
                    new EncounterProfile
                    {
                        Id = "defense_station",
                        Name = "Defense Station",
                        Description = "Stationary defensive encounter",
                        Difficulty = EncounterDifficulty.Hard,
                        EncounterType = EncounterType.Defensive,
                        PrefabName = "DefenseStation",
                        SpawnChance = 0.1f,
                        MaxInstances = 1,
                        DespawnDistance = 20000,
                        RequiredPlayerDistance = 5000
                    }
                };

                foreach (var profile in defaultProfiles)
                {
                    var filePath = Path.Combine(directory, $"{profile.Id}.json");
                    var json = JsonConvert.SerializeObject(profile, Formatting.Indented);
                    File.WriteAllText(filePath, json);
                    
                    Logger.Info($"Created default encounter profile: {profile.Id}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating default encounter profiles");
            }
        }

        #endregion
    }
}