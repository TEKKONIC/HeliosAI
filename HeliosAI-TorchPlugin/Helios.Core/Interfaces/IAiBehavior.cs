using System;
using System.Collections.Generic;
using VRageMath;
using Helios.Modules.AI;
using VRage.ModAPI;

namespace HeliosAI.Behaviors.Interfaces
{
    public interface IAiBehavior : IDisposable
    {
        /// <summary>
        /// The name of this behavior
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this behavior has completed its task
        /// </summary>
        bool IsComplete { get; }

        /// <summary>
        /// The NPC entity that owns this behavior
        /// </summary>
        NpcEntity Npc { get; set; }

        /// <summary>
        /// Whether this behavior can assist other agents
        /// </summary>
        bool CanAssist { get; }

        /// <summary>
        /// Fallback behavior to use when this behavior completes
        /// </summary>
        AiBehavior PatrolFallback { get; set; }

        /// <summary>
        /// Execute one tick of this behavior
        /// </summary>
        void Tick();

        /// <summary>
        /// Handle a backup request from another agent
        /// </summary>
        /// <param name="location">Location where backup is needed</param>
        void ReceiveBackupRequest(Vector3D location);

        /// <summary>
        /// Select the best target from a list of enemies
        /// </summary>
        /// <param name="enemies">List of potential enemies</param>
        /// <returns>Best target or null if none suitable</returns>
        EnemyEntity SelectTarget(List<EnemyEntity> enemies);

        /// <summary>
        /// Execute tactical movement towards a target
        /// </summary>
        /// <param name="target">Target to move towards</param>
        /// <param name="underFire">Whether the unit is currently under fire</param>
        void ExecuteTacticalMovement(EnemyEntity target, bool underFire);

        /// <summary>
        /// Determine if the behavior should retreat based on strength comparison
        /// </summary>
        /// <param name="myStrength">Current strength of this unit</param>
        /// <param name="enemyStrength">Estimated enemy strength</param>
        /// <returns>True if should retreat</returns>
        bool ShouldRetreat(float myStrength, float enemyStrength);

        /// <summary>
        /// Determine if the behavior should enter last stand mode
        /// </summary>
        /// <param name="gridIntegrity">Current grid integrity (0-1)</param>
        /// <returns>True if should enter last stand</returns>
        bool ShouldEnterLastStand(float gridIntegrity);

        /// <summary>
        /// Get the next waypoint for movement
        /// </summary>
        /// <returns>Next waypoint position</returns>
        Vector3D GetNextWaypoint();

        /// <summary>
        /// Called when the entity takes damage
        /// </summary>
        void OnDamaged();

        /// <summary>
        /// Called when a target is acquired
        /// </summary>
        void OnTargetAcquired();

        /// <summary>
        /// Called when a target is lost
        /// </summary>
        void OnTargetLost();

        /// <summary>
        /// Update method for periodic processing
        /// </summary>
        void Update();
    }

    /// <summary>
    /// Represents an enemy entity for targeting
    /// </summary>
    public class EnemyEntity
    {
        public Vector3D Position { get; set; }
        public string DisplayName { get; set; }
        public IMyEntity Entity { get; set; }

        public EnemyEntity(IMyEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
                
            Entity = entity;
            Position = entity.GetPosition();
            DisplayName = entity.DisplayName ?? "Unknown";
        }

        public bool IsValid()
        {
            return Entity != null && !Entity.MarkedForClose;
        }
    }
}