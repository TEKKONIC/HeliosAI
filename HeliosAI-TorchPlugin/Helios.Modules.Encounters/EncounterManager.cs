using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HeliosAI.Models;
using Helios.Core.Interfaces;
using Helios.Modules.AI.Behaviors;
using Helios.Modules.AI.Combat;
using VRageMath;
using NLog;
using Torch.API;
using Newtonsoft.Json;
using VRage.Game.ModAPI;

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
        private AdaptiveBehaviorEngine _behaviorEngine = new AdaptiveBehaviorEngine();
        private PredictiveAnalyzer _predictiveAnalyzer = new PredictiveAnalyzer();

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
                _encounterDirectory = Path.Combine(_torch.Config.InstancePath, "HeliosAI", "Encounters");
                Directory.CreateDirectory(_encounterDirectory);
        
                Logger.Info($"EncounterManager initialized with directory: {_encounterDirectory}");
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
                var targetDirectory = directory;
                if (directory == "Instance/HeliosAI/Encounters" && !string.IsNullOrEmpty(_encounterDirectory))
                {
                    targetDirectory = _encounterDirectory;
                }
                else if (_torch != null)
                {
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
                LoadProfiles();
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
                    var direction = CreateRandomDirection();
                    var distance = minDistance + _random.NextDouble() * (maxDistance - minDistance);
                    var position = origin + direction * distance;
            
                    if (IsPositionSafe(position, 500))
                    {
                        return position;
                    }
                }
        
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
                var theta = _random.NextDouble() * 2.0 * Math.PI; // Azimuth angle (0 to 2π)
                var phi = Math.Acos(2.0 * _random.NextDouble() - 1.0); // Polar angle (0 to π)
        
                var x = Math.Sin(phi) * Math.Cos(theta);
                var y = Math.Sin(phi) * Math.Sin(theta);
                var z = Math.Cos(phi);
        
                return new Vector3D(x, y, z);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error creating random direction");
                return Vector3D.Forward; 
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
                var spawnedGrids = SpawnPrefabEntities(profile, position);
        
                _activeEncounters.Add(encounter);
                Logger.Info($"Spawned encounter {profileId} at {position}");
        
                if (spawnedGrids != null && spawnedGrids.Any())
                {
                    foreach (var spawnedGrid in spawnedGrids)
                    {
                        RegisterWithAI(spawnedGrid, profile.EncounterType.ToString());
                        encounter.SpawnedEntityIds.Add(spawnedGrid.EntityId);
                    }
                }
        
                return encounter;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error spawning encounter {profileId}");
                return null;
            }
        }

        private List<IMyCubeGrid> SpawnPrefabEntities(EncounterProfile profile, Vector3D position)
        {
            try
            {
                var spawnedGrids = new List<IMyCubeGrid>();
        
                // TODO: Implement actual prefab spawning logic here
                // This is where you would integrate with your existing spawn system
                // Example:
                // var grid = SpawnPrefab(profile.PrefabName, position);
                // if (grid != null)
                //     spawnedGrids.Add(grid);
        
                Logger.Debug($"Spawning prefab: {profile.PrefabName} at {position}");
        
                return spawnedGrids;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error spawning prefab entities for profile: {profile.Id}");
                return new List<IMyCubeGrid>();
            }
        }

        private void RegisterWithAI(IMyCubeGrid grid, string encounterType)
        {
            try
            {
                if (grid == null)
                {
                    Logger.Warn("Attempted to register null grid with AI");
                    return;
                }

                var personality = GetPersonalityFromEncounter(encounterType);
                _behaviorEngine.RegisterBehavior($"{personality}_Combat", 0.7f);
                _behaviorEngine.RegisterBehavior($"{personality}_Patrol", 0.6f);
                _behaviorEngine.RegisterBehavior($"{personality}_Retreat", 0.4f);
                _behaviorEngine.RegisterBehavior($"{personality}_Escort", 0.5f);
        
                Logger.Debug($"Registered grid {grid.EntityId} with AI personality: {personality}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error registering grid {grid?.EntityId} with AI");
            }
        }

        private string GetPersonalityFromEncounter(string encounterType)
        {
            try
            {
                return encounterType?.ToLower() switch
                {
                    "pirate" => "Aggressive",
                    "patrol" => "Defensive", 
                    "defensive" => "Guardian",
                    "trader" => "Peaceful",
                    "military" => "Tactical",
                    _ => "Neutral"
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error determining personality for encounter type: {encounterType}");
                return "Neutral";
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
                        
                        if (encounter.IsExpired(TimeSpan.FromMinutes(30))) // 30 min default
                        {
                            encounter.Status = EncounterStatus.Expired;
                            encounter.StatusReason = "Timeout";
                        }
                        
                        UpdateEncounterAI(encounter);
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
                return !_playerEncounterSettings.TryGetValue(playerId, out var value) || value;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking spawn permission for player {playerId}");
                return true;
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

        private float GetGridHealth(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement actual health calculation
                // Calculate percentage of functional blocks vs total blocks
                return 1.0f; 
            }
            catch
            {
                return 1.0f;
            }
        }

        private float GetGridPower(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement actual power calculation
                // Check reactor/battery power levels
                return 1.0f; 
            }
            catch
            {
                return 1.0f;
            }
        }

        private float GetNearbyEnemyCount(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement enemy detection within range
                return 0f; 
            }
            catch
            {
                return 0f;
            }
        }

        private float GetNearbyAllyCount(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement ally detection within range
                return 1f; 
            }
            catch
            {
                return 1f;
            }
        }

        private float GetDistanceToNearestTarget(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement target detection and distance calculation
                return 1000f; 
            }
            catch
            {
                return 1000f;
            }
        }

        private float CalculateThreatLevel(IMyCubeGrid grid) 
        { 
            try
            {
                // TODO: Implement threat assessment based on nearby entities
                return 0.5f; 
            }
            catch
            {
                return 0.5f;
            }
        }

        #endregion

        #region Spawn Management

        public ActiveEncounter SpawnRandomEncounter(long playerId, EncounterDifficulty difficulty = EncounterDifficulty.Easy, EncounterType encounterType = EncounterType.Patrol)
        {
            try
            {
                // Check if player can have encounters spawned
                if (!CanSpawnForPlayer(playerId))
                {
                    Logger.Debug($"Encounters disabled for player {playerId}");
                    return null;
                }

                var profile = GetRandomProfile(difficulty, encounterType);
                if (profile == null)
                {
                    Logger.Warn($"No encounter profiles found for difficulty {difficulty} and type {encounterType}");
                    return null;
                }

                var playerPosition = GetPlayerPosition(playerId);
                if (playerPosition == null)
                {
                    Logger.Warn($"Could not get position for player {playerId}");
                    return null;
                }

                var spawnPosition = GetSpawnPosition(
                    playerPosition.Value, 
                    profile.RequiredPlayerDistance, 
                    profile.RequiredPlayerDistance + 5000,
                    avoidPlayers: true,
                    avoidGrids: true
                );

                var encounter = SpawnEncounter(profile.Id, spawnPosition, playerId);
        
                if (encounter != null)
                {
                    Logger.Info($"Spawned random encounter {profile.Id} for player {playerId} at {spawnPosition}");
                }

                return encounter;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error spawning random encounter for player {playerId}");
                return null;
            }
        }

        private Vector3D? GetPlayerPosition(long playerId)
        {
            try
            {
                // TODO: Implement actual player position lookup
                // This would typically involve querying the game API for player entities
                // Example:
                // var player = MyAPIGateway.Players.GetPlayerById(playerId);
                // if (player?.Character != null)
                //     return player.Character.PositionComp.GetPosition();
        
                // For now, return a placeholder position
                Logger.Debug($"Getting position for player {playerId} - placeholder implementation");
                return Vector3D.Zero; // Replace with actual implementation
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting position for player {playerId}");
                return null;
            }
        }

        #endregion

        #region AI Integration Methods

        private void UpdateEncounterAI(ActiveEncounter encounter)
        {
            try
            {
                // Get spawned entities for this encounter
                var encounterGrids = GetEncounterGrids(encounter);
                
                foreach (var grid in encounterGrids)
                {
                    if (grid?.Physics != null)
                    {
                        _predictiveAnalyzer.UpdateMovementHistory(grid);
                        var context = GatherAIContext(grid, encounter);
                        var availableBehaviors = GetAvailableBehaviors(encounter);
                        var selectedBehavior = _behaviorEngine.SelectOptimalBehavior(
                            grid.EntityId, context, availableBehaviors);
                        
                        if (!string.IsNullOrEmpty(selectedBehavior))
                        {
                            var success = ExecuteAIBehavior(grid, selectedBehavior, context);
                            _behaviorEngine.ReportBehaviorOutcome(
                                grid.EntityId, selectedBehavior, success, context);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating AI for encounter {encounter.Id}");
            }
        }

        private List<IMyCubeGrid> GetEncounterGrids(ActiveEncounter encounter)
        {
            try
            {
                var grids = new List<IMyCubeGrid>();
        
                // TODO: Get actual grids from SpawnedEntityIds
                // This would query the game world for entities with matching IDs
                foreach (var entityId in encounter.SpawnedEntityIds)
                {
                    // Example implementation when you have access to game API:
                    // var entity = MyAPIGateway.Entities.GetEntityById(entityId);
                    // if (entity is IMyCubeGrid grid && !grid.MarkedForClose)
                    //     grids.Add(grid);
                }
        
                return grids;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting grids for encounter {encounter.Id}");
                return new List<IMyCubeGrid>();
            }
        }

        private Dictionary<string, float> GatherAIContext(IMyCubeGrid grid, ActiveEncounter encounter)
        {
            try
            {
                return new Dictionary<string, float>
                {
                    ["HealthPercentage"] = GetGridHealth(grid),
                    ["PowerLevel"] = GetGridPower(grid),
                    ["EnemyCount"] = GetNearbyEnemyCount(grid),
                    ["AllyCount"] = GetNearbyAllyCount(grid),
                    ["DistanceToTarget"] = GetDistanceToNearestTarget(grid),
                    ["ThreatLevel"] = CalculateThreatLevel(grid),
                    ["EncounterAge"] = (float)encounter.Age.TotalMinutes
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error gathering AI context for grid {grid?.EntityId}");
                return new Dictionary<string, float>();
            }
        }

        private List<string> GetAvailableBehaviors(ActiveEncounter encounter)
        {
            try
            {
                var profile = GetProfile(encounter.ProfileId);
                var personality = GetPersonalityFromEncounter(profile?.EncounterType.ToString() ?? "Neutral");
        
                return new List<string>
                {
                    $"{personality}_Combat",
                    $"{personality}_Patrol", 
                    $"{personality}_Retreat",
                    $"{personality}_Escort"
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting available behaviors for encounter {encounter.Id}");
                return new List<string> { "Neutral_Patrol" };
            }
        }

        private bool ExecuteAIBehavior(IMyCubeGrid grid, string behavior, Dictionary<string, float> context)
        {
            try
            {
                // TODO: Implement actual behavior execution
                // This would integrate with your existing ship control systems
                Logger.Debug($"Executing behavior {behavior} for grid {grid.EntityId}");
        
                // Placeholder behavior execution - implement based on your needs
                var behaviorParts = behavior.Split('_');
                if (behaviorParts.Length >= 2)
                {
                    var behaviorType = behaviorParts[1].ToLower();
                    switch (behaviorType)
                    {
                        case "combat":
                            return ExecuteCombatBehavior(grid, context);
                        case "patrol":
                            return ExecutePatrolBehavior(grid, context);
                        case "retreat":
                            return ExecuteRetreatBehavior(grid, context);
                        case "escort":
                            return ExecuteEscortBehavior(grid, context);
                        default:
                            Logger.Debug($"Unknown behavior type: {behaviorType}");
                            return false;
                    }
                }
        
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing behavior {behavior} for grid {grid?.EntityId}");
                return false;
            }
        }

        private bool ExecuteCombatBehavior(IMyCubeGrid grid, Dictionary<string, float> context) 
        { 
            try
            {
                // TODO: Implement combat behavior
                // - Find nearest enemy
                // - Use predictive analyzer for weapon selection
                // - Engage target with optimal tactics
                Logger.Debug($"Executing combat behavior for grid {grid.EntityId}");
                return true; 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing combat behavior for grid {grid?.EntityId}");
                return false;
            }
        }

        private bool ExecutePatrolBehavior(IMyCubeGrid grid, Dictionary<string, float> context) 
        { 
            try
            {
                // TODO: Implement patrol behavior
                // - Follow predefined waypoints
                // - Scan for threats
                // - Maintain formation if part of group
                Logger.Debug($"Executing patrol behavior for grid {grid.EntityId}");
                return true; 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing patrol behavior for grid {grid?.EntityId}");
                return false;
            }
        }

        private bool ExecuteRetreatBehavior(IMyCubeGrid grid, Dictionary<string, float> context) 
        { 
            try
            {
                // TODO: Implement retreat behavior
                // - Move away from threats
                // - Attempt to escape to safe distance
                // - Call for reinforcements if available
                Logger.Debug($"Executing retreat behavior for grid {grid.EntityId}");
                return true; 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing retreat behavior for grid {grid?.EntityId}");
                return false;
            }
        }

        private bool ExecuteEscortBehavior(IMyCubeGrid grid, Dictionary<string, float> context) 
        { 
            try
            {
                // TODO: Implement escort behavior
                // - Stay near protected target
                // - Intercept threats to target
                // - Maintain escort formation
                Logger.Debug($"Executing escort behavior for grid {grid.EntityId}");
                return true; 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing escort behavior for grid {grid?.EntityId}");
                return false;
            }
        }

        #endregion
    }
}