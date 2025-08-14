using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using VRage.Game.ModAPI;
using VRageMath;
using HeliosAI.Behaviors;
using Helios.Modules.AI;
using VRage.ModAPI;

namespace Helios.Core.Interfaces
{
    public interface IAiManager
    {
        /// <summary>
        /// Register a grid for AI management
        /// </summary>
        /// <param name="grid">Grid to register</param>
        /// <param name="initialBehavior">Optional initial behavior</param>
        void RegisterGrid(IMyCubeGrid grid, AiBehavior initialBehavior = null);

        /// <summary>
        /// Unregister a grid from AI management
        /// </summary>
        /// <param name="grid">Grid to unregister</param>
        void UnregisterGrid(IMyCubeGrid grid);

        /// <summary>
        /// Tick all registered AI entities
        /// </summary>
        void TickAll();

        /// <summary>
        /// Get the current behavior of a grid
        /// </summary>
        /// <param name="grid">Grid to check</param>
        /// <returns>Current behavior or null if not registered</returns>
        AiBehavior GetBehavior(IMyCubeGrid grid);

        /// <summary>
        /// Gets all currently active NPCs (read-only)
        /// </summary>
        IReadOnlyList<NpcEntity> ActiveNpcs { get; }

        /// <summary>
        /// Set the behavior of a registered grid
        /// </summary>
        /// <param name="grid">Grid to modify</param>
        /// <param name="behavior">New behavior to set</param>
        void SetBehavior(IMyCubeGrid grid, AiBehavior behavior);

        /// <summary>
        /// Check if a grid is registered for AI management
        /// </summary>
        /// <param name="grid">Grid to check</param>
        /// <returns>True if registered</returns>
        bool IsRegistered(IMyCubeGrid grid);

        /// <summary>
        /// Get all registered NPC entities
        /// </summary>
        /// <returns>Read-only list of NPCs</returns>
        IReadOnlyList<NpcEntity> GetAllRegistered();

        /// <summary>
        /// Get the NPC entity for a specific grid
        /// </summary>
        /// <param name="grid">Grid to find</param>
        /// <returns>NPC entity or null if not found</returns>
        NpcEntity GetNpc(IMyCubeGrid grid);

        /// <summary>
        /// Spawn a new NPC at the specified location
        /// </summary>
        /// <param name="position">World position to spawn at</param>
        /// <param name="prefab">Prefab name to spawn</param>
        /// <param name="mood">Initial AI mood</param>
        void SpawnNpc(Vector3D position, string prefab, NpcEntity.AiMood mood);

        /// <summary>
        /// Find the nearest player to a position
        /// </summary>
        /// <param name="origin">Origin position</param>
        /// <param name="range">Search range</param>
        /// <returns>Nearest player entity or null</returns>
        IMyEntity FindNearestPlayer(Vector3D origin, double range);

        /// <summary>
        /// Find a suitable target for an NPC
        /// </summary>
        /// <param name="origin">NPC position</param>
        /// <param name="range">Search range</param>
        /// <param name="mood">NPC mood affecting targeting</param>
        /// <param name="ownFactionId">NPC's faction ID</param>
        /// <returns>Target entity or null</returns>
        IMyEntity FindTarget(Vector3D origin, double range, NpcEntity.AiMood mood, long ownFactionId = 0);

        /// <summary>
        /// Assess the threat level of an entity
        /// </summary>
        /// <param name="entity">Entity to assess</param>
        /// <param name="origin">Assessment origin point</param>
        /// <param name="ownFactionId">Assessor's faction ID</param>
        /// <returns>Threat score (higher = more dangerous)</returns>
        double AssessThreat(IMyEntity entity, Vector3D origin, long ownFactionId);

        /// <summary>
        /// Clean up NPCs with closed/invalid grids
        /// </summary>
        void CleanupClosedGrids();

        /// <summary>
        /// Get NPCs within a specific range of a position
        /// </summary>
        /// <param name="position">Center position</param>
        /// <param name="range">Search range</param>
        /// <returns>List of NPCs in range</returns>
        List<NpcEntity> GetNpcsInRange(Vector3D position, double range);

        /// <summary>
        /// Get NPCs with a specific mood
        /// </summary>
        /// <param name="mood">Mood to filter by</param>
        /// <returns>List of NPCs with the specified mood</returns>
        List<NpcEntity> GetNpcsByMood(NpcEntity.AiMood mood);
        
        /// <summary>
        /// Sets the mood for a specific NPC
        /// </summary>
        /// <param name="npc">The NPC to update</param>
        /// <param name="mood">The new mood</param>
        void SetNpcMood(NpcEntity npc, NpcEntity.AiMood mood);

        /// <summary>
        /// Get NPCs with a specific behavior type
        /// </summary>
        /// <typeparam name="T">Behavior type to filter by</typeparam>
        /// <returns>List of NPCs with the specified behavior</returns>
        List<NpcEntity> GetNpcsByBehavior<T>() where T : AiBehavior;

        /// <summary>
        /// Set the mood for all registered NPCs
        /// </summary>
        /// <param name="mood">New mood to set</param>
        void SetGlobalMood(NpcEntity.AiMood mood);

        /// <summary>
        /// Update method for custom broadcast processing
        /// </summary>
        void CustomUpdate();

        /// <summary>
        /// Get statistics about registered NPCs
        /// </summary>
        /// <returns>NPC statistics</returns>
        NpcStatistics GetStatistics();

        /// <summary>
        /// Raised when an NPC is spawned.
        /// </summary>
        event Action<NpcEntity> NpcSpawned;

        /// <summary>
        /// Raised when an NPC is removed/unregistered.
        /// </summary>
        event Action<NpcEntity> NpcRemoved;

        /// <summary>
        /// Raised when an NPC's mood changes.
        /// </summary>
        event Action<NpcEntity, NpcEntity.AiMood> NpcMoodChanged;

        /// <summary>
        /// Raised when an NPC's behavior changes.
        /// </summary>
        event Action<NpcEntity, AiBehavior> NpcBehaviorChanged;

        /// <summary>
        /// Register an AI plugin for custom logic.
        /// </summary>
        void RegisterPlugin(IAiPlugin plugin);

        /// <summary>
        /// Asynchronously spawn a new NPC at the specified location.
        /// </summary>
        Task SpawnNpcAsync(Vector3D position, string prefab, NpcEntity.AiMood mood);
    }

    /// <summary>
    /// Statistics about NPC entities
    /// </summary>
    public class NpcStatistics
    {
        public int TotalNpcs { get; set; }
        public int AggressiveNpcs { get; set; }
        public int PassiveNpcs { get; set; }
        public int GuardNpcs { get; set; }
        public int AttackingNpcs { get; set; }
        public int PatrollingNpcs { get; set; }
        public int IdleNpcs { get; set; }
        public int RetratingNpcs { get; set; }
        public double AverageHealth { get; set; }

        public override string ToString()
        {
            return $"NPCs: {TotalNpcs} | Aggressive: {AggressiveNpcs} | Passive: {PassiveNpcs} | Guard: {GuardNpcs} | Attacking: {AttackingNpcs} | Patrolling: {PatrollingNpcs} | Idle: {IdleNpcs} | Retreating: {RetratingNpcs} | Avg Health: {AverageHealth:P}";
        }
    }
}