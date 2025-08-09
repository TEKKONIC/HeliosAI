using System.Collections.Generic;
using Helios.Core.Interfaces;
using Helios.Modules.AI;
using VRage.Game.ModAPI;
using VRageMath;
using System;
using NLog;

namespace HeliosAI.Behaviors
{
    public abstract class AiBehavior(IMyCubeGrid grid) : IBehavior
    {
        protected static readonly Logger Logger = LogManager.GetLogger("AiBehavior");
        protected IMyCubeGrid Grid { get; set; } = grid ?? throw new ArgumentNullException(nameof(grid));
        public NpcEntity Npc { get; set; }

        public virtual bool IsComplete => false;
        public IBehavior PatrolFallback { get; set; } // Changed from AiBehavior to IBehavior
        public virtual bool CanAssist => true;
        public abstract string Name { get; }

        public virtual void ReceiveBackupRequest(Vector3D location)
        {
            try
            {
                Logger.Debug($"{Name} received backup request at {location}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error processing backup request in {Name}");
            }
        }

        public virtual EnemyEntity SelectTarget(List<EnemyEntity> enemies)
        {
            try
            {
                if (enemies == null || enemies.Count == 0 || Npc?.Position == null)
                    return null;

                EnemyEntity best = null;
                var bestDist = double.MaxValue;
                
                foreach (var enemy in enemies)
                {
                    if (enemy?.IsValid() != true) continue;
                    
                    var dist = enemy.DistanceFrom(Npc.Position);
                    if (dist < bestDist)
                    {
                        best = enemy;
                        bestDist = dist;
                    }
                }
                
                return best;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error selecting target in {Name}");
                return null;
            }
        }

        public virtual void ExecuteTacticalMovement(EnemyEntity target, bool underFire)
        {
            try
            {
                if (target?.IsValid() != true || Npc == null)
                    return;

                // Default: move directly toward target
                // Subclasses can implement more sophisticated movement
                Npc.MoveTo(target.Position);
                
                Logger.Debug($"{Name} moving to target at {target.Position}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing tactical movement in {Name}");
            }
        }

        public virtual bool ShouldRetreat(float myStrength, float enemyStrength)
        {
            try
            {
                var retreatThreshold = myStrength < enemyStrength * 0.7f;
                if (retreatThreshold)
                {
                    Logger.Debug($"{Name} should retreat: My strength {myStrength} vs Enemy {enemyStrength}");
                }
                return retreatThreshold;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error calculating retreat decision in {Name}");
                return false;
            }
        }

        public virtual bool ShouldEnterLastStand(float gridIntegrity)
        {
            try
            {
                var lastStand = gridIntegrity < 0.2f;
                if (lastStand)
                {
                    Logger.Info($"{Name} entering last stand mode: Grid integrity {gridIntegrity:P}");
                }
                return lastStand;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error calculating last stand decision in {Name}");
                return false;
            }
        }

        public abstract void Tick();

        public virtual Vector3D GetNextWaypoint()
        {
            try
            {
                return Npc?.Position ?? Vector3D.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting next waypoint in {Name}");
                return Vector3D.Zero;
            }
        }

        public virtual void OnDamaged()
        {
            try
            {
                Logger.Debug($"{Name} received damage notification");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling damage event in {Name}");
            }
        }

        public virtual void OnTargetAcquired()
        {
            try
            {
                Logger.Debug($"{Name} acquired target");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling target acquired event in {Name}");
            }
        }

        public virtual void OnTargetLost()
        {
            try
            {
                Logger.Debug($"{Name} lost target");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling target lost event in {Name}");
            }
        }

        public virtual void Update()
        {
            try
            {
                // Default: do nothing - subclasses can implement update logic
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error in Update method for {Name}");
            }
        }

        public virtual void Dispose()
        {
            try
            {
                Grid = null;
                Npc = null;
                PatrolFallback = null;
                Logger.Debug($"{Name} behavior disposed");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error disposing {Name} behavior");
            }
        }
    }
}