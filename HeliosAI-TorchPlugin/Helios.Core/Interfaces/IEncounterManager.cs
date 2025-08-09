using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HeliosAI.Models;
using Torch.API;
using VRageMath;

namespace Helios.Core.Interfaces
{
    public interface IEncounterManager
    {
        /// <summary>
        /// All loaded encounter profiles
        /// </summary>
        IReadOnlyDictionary<string, EncounterProfile> Profiles { get; }

        /// <summary>
        /// Currently active encounters
        /// </summary>
        IReadOnlyList<ActiveEncounter> ActiveEncounters { get; }

        /// <summary>
        /// Maximum number of concurrent encounters allowed
        /// </summary>
        int MaxConcurrentEncounters { get; set; }

        /// <summary>
        /// Initialize the encounter manager
        /// </summary>
        /// <param name="torch">Torch instance</param>
        Task InitializeAsync(ITorchBase torch);

        /// <summary>
        /// Load encounter profiles from directory
        /// </summary>
        /// <param name="directory">Directory path containing encounter files</param>
        void LoadProfiles(string directory = "Instance/HeliosAI/Encounters");

        /// <summary>
        /// Reload all encounter profiles
        /// </summary>
        void ReloadProfiles();

        /// <summary>
        /// Get a specific encounter profile by ID
        /// </summary>
        /// <param name="id">Profile ID</param>
        /// <returns>Encounter profile or null if not found</returns>
        EncounterProfile GetProfile(string id);

        /// <summary>
        /// Get all profiles matching specific criteria
        /// </summary>
        /// <param name="difficulty">Difficulty level filter</param>
        /// <param name="encounterType">Encounter type filter</param>
        /// <returns>List of matching profiles</returns>
        List<EncounterProfile> GetProfilesByFilter(EncounterDifficulty? difficulty = null, EncounterType? encounterType = null);

        /// <summary>
        /// Get a random encounter profile based on criteria
        /// </summary>
        /// <param name="difficulty">Difficulty level</param>
        /// <param name="encounterType">Encounter type</param>
        /// <returns>Random matching profile or null</returns>
        EncounterProfile GetRandomProfile(EncounterDifficulty? difficulty = null, EncounterType? encounterType = null);

        /// <summary>
        /// Calculate a safe spawn position
        /// </summary>
        /// <param name="origin">Origin point</param>
        /// <param name="minDistance">Minimum distance from origin</param>
        /// <param name="maxDistance">Maximum distance from origin</param>
        /// <param name="avoidPlayers">Whether to avoid player proximity</param>
        /// <param name="avoidGrids">Whether to avoid existing grids</param>
        /// <returns>Safe spawn position</returns>
        Vector3D GetSpawnPosition(Vector3D origin, double minDistance, double maxDistance, bool avoidPlayers = true, bool avoidGrids = true);

        /// <summary>
        /// Spawn an encounter at a specific location
        /// </summary>
        /// <param name="profileId">Encounter profile ID</param>
        /// <param name="position">Spawn position</param>
        /// <param name="playerId">Optional player ID that triggered the encounter</param>
        /// <returns>Active encounter instance or null if failed</returns>
        ActiveEncounter SpawnEncounter(string profileId, Vector3D position, long playerId = 0);

        /// <summary>
        /// Spawn a random encounter near a player
        /// </summary>
        /// <param name="playerId">Target player ID</param>
        /// <param name="difficulty">Encounter difficulty</param>
        /// <param name="encounterType">Encounter type</param>
        /// <returns>Active encounter instance or null if failed</returns>
        ActiveEncounter SpawnRandomEncounter(long playerId, EncounterDifficulty difficulty = EncounterDifficulty.Medium, EncounterType encounterType = EncounterType.Patrol);

        /// <summary>
        /// Update all active encounters
        /// </summary>
        void UpdateEncounters();

        /// <summary>
        /// Clean up completed or invalid encounters
        /// </summary>
        void CleanupEncounters();

        /// <summary>
        /// Despawn a specific encounter
        /// </summary>
        /// <param name="encounterId">Encounter ID</param>
        /// <param name="reason">Reason for despawning</param>
        void DespawnEncounter(string encounterId, string reason = "Manual");

        /// <summary>
        /// Check if a position is suitable for spawning
        /// </summary>
        /// <param name="position">Position to check</param>
        /// <param name="radius">Check radius</param>
        /// <returns>True if position is clear for spawning</returns>
        bool IsPositionSafe(Vector3D position, double radius = 500);

        /// <summary>
        /// Get encounters within range of a position
        /// </summary>
        /// <param name="position">Center position</param>
        /// <param name="range">Search range</param>
        /// <returns>List of encounters in range</returns>
        List<ActiveEncounter> GetEncountersInRange(Vector3D position, double range);

        /// <summary>
        /// Get encounter statistics
        /// </summary>
        /// <returns>Encounter statistics</returns>
        EncounterStatistics GetStatistics();

        /// <summary>
        /// Set global encounter spawn rate modifier
        /// </summary>
        /// <param name="modifier">Spawn rate multiplier (1.0 = normal, 0.0 = disabled)</param>
        void SetSpawnRateModifier(float modifier);

        /// <summary>
        /// Check if encounters can spawn for a specific player
        /// </summary>
        /// <param name="playerId">Player ID</param>
        /// <returns>True if encounters are enabled for this player</returns>
        bool CanSpawnForPlayer(long playerId);

        /// <summary>
        /// Enable or disable encounters for a specific player
        /// </summary>
        /// <param name="playerId">Player ID</param>
        /// <param name="enabled">Whether encounters are enabled</param>
        void SetPlayerEncountersEnabled(long playerId, bool enabled);
    }

    /// <summary>
    /// Represents an active encounter instance
    /// </summary>
    public class ActiveEncounter
    {
        public string Id { get; set; }
        public string ProfileId { get; set; }
        public Vector3D Position { get; set; }
        public DateTime SpawnTime { get; set; }
        public long TriggeringPlayerId { get; set; }
        public List<long> SpawnedEntityIds { get; set; } = new List<long>();
        public EncounterStatus Status { get; set; }
        public string StatusReason { get; set; }
        public DateTime LastUpdate { get; set; }

        public TimeSpan Age => DateTime.UtcNow - SpawnTime;
        public bool IsExpired(TimeSpan maxAge) => Age > maxAge;
    }

    /// <summary>
    /// Encounter status enumeration
    /// </summary>
    public enum EncounterStatus
    {
        Active,
        Completed,
        Failed,
        Despawned,
        Expired
    }

    /// <summary>
    /// Encounter difficulty levels
    /// </summary>
    public enum EncounterDifficulty
    {
        Easy,
        Medium,
        Hard,
        Extreme
    }

    /// <summary>
    /// Encounter type categories
    /// </summary>
    public enum EncounterType
    {
        Patrol,
        Guard,
        Aggressive,
        Defensive,
        Trader,
        Pirate,
        Military,
        Civilian
    }

    /// <summary>
    /// Encounter system statistics
    /// </summary>
    public class EncounterStatistics
    {
        public int TotalProfilesLoaded { get; set; }
        public int ActiveEncounters { get; set; }
        public int TotalEncountersSpawned { get; set; }
        public int EncountersCompleted { get; set; }
        public int EncountersFailed { get; set; }
        public float SpawnRateModifier { get; set; }
        public DateTime LastSpawn { get; set; }
        public TimeSpan AverageEncounterDuration { get; set; }

        public override string ToString()
        {
            return $"Profiles: {TotalProfilesLoaded} | Active: {ActiveEncounters} | Spawned: {TotalEncountersSpawned} | Completed: {EncountersCompleted} | Failed: {EncountersFailed} | Rate: {SpawnRateModifier:P}";
        }
    }
}