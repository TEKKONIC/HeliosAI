using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using Helios.Modules.AI;
using VRage.ModAPI;

namespace Helios.Core.Interfaces
{
    /// <summary>
    /// Base interface for all AI behaviors
    /// </summary>
    public interface IBehavior : IDisposable
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
        IBehavior PatrolFallback { get; set; }

        /// <summary>
        /// Execute one tick of this behavior - main behavior logic
        /// </summary>
        void Tick();

        /// <summary>
        /// Update method for periodic processing (separate from main logic)
        /// </summary>
        void Update();

        /// <summary>
        /// Called when a target is acquired
        /// </summary>
        void OnTargetAcquired();

        /// <summary>
        /// Called when a target is lost
        /// </summary>
        void OnTargetLost();

        /// <summary>
        /// Called when the entity takes damage
        /// </summary>
        void OnDamaged();

        /// <summary>
        /// Get the next waypoint for movement
        /// </summary>
        /// <returns>Next waypoint position</returns>
        Vector3D GetNextWaypoint();

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
    }

    /// <summary>
    /// Represents an enemy entity for targeting
    /// </summary>
    public class EnemyEntity
    {
        public Vector3D Position { get; set; }
        public string DisplayName { get; set; }
        public IMyEntity Entity { get; set; }
        public float ThreatLevel { get; set; }
        public DateTime LastSeen { get; set; }

        public EnemyEntity(IMyEntity entity)
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));
                
            Entity = entity;
            Position = entity.GetPosition();
            DisplayName = entity.DisplayName ?? "Unknown";
            ThreatLevel = 0f;
            LastSeen = DateTime.UtcNow;
        }

        /// <summary>
        /// Check if this enemy entity is still valid
        /// </summary>
        /// <returns>True if valid and not marked for close</returns>
        public bool IsValid()
        {
            return Entity != null && !Entity.MarkedForClose;
        }

        /// <summary>
        /// Update the position and last seen time
        /// </summary>
        public void UpdatePosition()
        {
            if (IsValid())
            {
                Position = Entity.GetPosition();
                LastSeen = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Check if this enemy has been seen recently
        /// </summary>
        /// <param name="maxAge">Maximum age in seconds</param>
        /// <returns>True if seen within the time limit</returns>
        public bool IsRecentlySeen(double maxAge = 30.0)
        {
            return DateTime.UtcNow.Subtract(LastSeen).TotalSeconds <= maxAge;
        }

        /// <summary>
        /// Calculate distance to this enemy from a position
        /// </summary>
        /// <param name="from">Position to calculate from</param>
        /// <returns>Distance in meters</returns>
        public double DistanceFrom(Vector3D from)
        {
            return Vector3D.Distance(from, Position);
        }
    }

    /// <summary>
    /// Context information for behavior decision making
    /// </summary>
    public class BehaviorContext
    {
        public Vector3D Position { get; set; }
        public List<EnemyEntity> NearbyEnemies { get; set; } = new List<EnemyEntity>();
        public List<NpcEntity> NearbyAllies { get; set; } = new List<NpcEntity>();
        public float GridIntegrity { get; set; }
        public bool UnderFire { get; set; }
        public DateTime LastDamaged { get; set; }
        public Vector3D? LastKnownEnemyPosition { get; set; }

        public BehaviorContext(Vector3D position)
        {
            Position = position;
            GridIntegrity = 1.0f;
            UnderFire = false;
            LastDamaged = DateTime.MinValue;
        }

        /// <summary>
        /// Check if recently damaged
        /// </summary>
        /// <param name="withinSeconds">Time window in seconds</param>
        /// <returns>True if damaged within time window</returns>
        public bool IsRecentlyDamaged(double withinSeconds = 10.0)
        {
            return DateTime.UtcNow.Subtract(LastDamaged).TotalSeconds <= withinSeconds;
        }

        /// <summary>
        /// Get the closest enemy
        /// </summary>
        /// <returns>Closest enemy or null</returns>
        public EnemyEntity GetClosestEnemy()
        {
            return NearbyEnemies
                .Where(e => e.IsValid())
                .OrderBy(e => e.DistanceFrom(Position))
                .FirstOrDefault();
        }

        /// <summary>
        /// Count enemies within a specific range
        /// </summary>
        /// <param name="range">Range in meters</param>
        /// <returns>Number of enemies in range</returns>
        public int CountEnemiesInRange(double range)
        {
            return NearbyEnemies.Count(e => e.IsValid() && e.DistanceFrom(Position) <= range);
        }
    }
}