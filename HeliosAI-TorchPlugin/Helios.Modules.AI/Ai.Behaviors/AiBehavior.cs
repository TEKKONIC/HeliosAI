using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Core.Interfaces;
using Helios.Modules.AI;
using Helios.Modules.AI.Combat;
using VRage.Game.ModAPI;
using VRageMath;
using Helios.Modules.Nations;
using NLog;
using VRage.ModAPI;

namespace HeliosAI.Behaviors
{
    /// <summary>
    /// Base class for AI behaviors. Provides default implementations and logging with adaptive intelligence.
    /// </summary>
    public abstract class AiBehavior : IBehavior
    {
        protected static readonly Logger Logger = LogManager.GetLogger("AiBehavior");

        /// <summary>
        /// The grid this behavior controls.
        /// </summary>
        protected internal IMyCubeGrid Grid { get; set; }

        /// <summary>
        /// The nation type of this agent.
        /// </summary>
        public virtual NationType Nation { get; set; } = NationType.Unknown;

        /// <summary>
        /// The NPC entity associated with this behavior.
        /// </summary>
        public NpcEntity Npc { get; set; }

        /// <summary>
        /// Indicates if the behavior is complete.
        /// </summary>
        public virtual bool IsComplete => false;

        /// <summary>
        /// Fallback behavior for patrol.
        /// </summary>
        public IBehavior PatrolFallback { get; set; }

        /// <summary>
        /// Indicates if this agent can assist others.
        /// </summary>
        public virtual bool CanAssist => true;

        /// <summary>
        /// The name of this behavior.
        /// </summary>
        public abstract string Name { get; }

        // Add adaptive/predictive components
        protected PredictiveAnalyzer _predictiveAnalyzer;
        protected DateTime _lastTargetUpdate = DateTime.MinValue;
        protected IMyEntity _lastKnownTarget;
        protected Vector3D _lastKnownTargetPosition;
        protected DateTime _behaviorStartTime = DateTime.UtcNow;
        protected Dictionary<string, float> _performanceMetrics = new Dictionary<string, float>();

        /// <summary>
        /// Constructor for AiBehavior.
        /// </summary>
        /// <param name="grid">The grid this behavior controls.</param>
        protected AiBehavior(IMyCubeGrid grid)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            
            var aiManager = AiManager.Instance;
            if (aiManager != null)
            {
                _predictiveAnalyzer = new PredictiveAnalyzer();
            }
            
            _behaviorStartTime = DateTime.UtcNow;
            InitializePerformanceMetrics();
            
            Logger.Debug($"Initialized {Name} behavior with adaptive intelligence");
        }

        /// <summary>
        /// Gets the next waypoint for this behavior. Override in derived classes for specific navigation.
        /// </summary>
        public virtual Vector3D GetNextWaypoint()
        {
            try
            {
                if (_lastKnownTarget != null && _predictiveAnalyzer != null)
                {
                    return _predictiveAnalyzer.PredictEnemyPosition(_lastKnownTarget, 3.0f);
                }
                
                return Grid?.GetPosition() ?? Vector3D.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting next waypoint for {Name}");
                return Grid?.GetPosition() ?? Vector3D.Zero;
            }
        }

        /// <summary>
        /// Receives a backup request from another agent.
        /// </summary>
        public virtual void ReceiveBackupRequest(Vector3D location, string message)
        {
            try
            {
                Logger.Info($"{Name} received backup request at {location}: {message}");
                
                // Record backup request for learning
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "BackupRequestReceived", new Dictionary<string, object>
                {
                    ["RequestLocation"] = location,
                    ["Message"] = message,
                    ["BehaviorType"] = GetType().Name,
                    ["CurrentPosition"] = Grid?.GetPosition() ?? Vector3D.Zero,
                    ["ResponseTime"] = DateTime.UtcNow
                });

                OnBackupRequested(location, message);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling backup request in {Name}");
            }
        }

        /// <summary>
        /// Override this method in derived classes to handle backup requests.
        /// </summary>
        protected virtual void OnBackupRequested(Vector3D location, string message)
        {
            // Default: do nothing - individual behaviors can override
        }

        /// <summary>
        /// Determines if this behavior should enter a last stand mode based on health.
        /// </summary>
        public virtual bool ShouldEnterLastStand(float healthPercentage)
        {
            try
            {
                // Default last stand threshold: 20% health
                var shouldLastStand = healthPercentage <= 0.2f;
                
                if (_predictiveAnalyzer != null && Grid != null)
                {
                    var context = new Dictionary<string, object>
                    {
                        ["HealthPercentage"] = healthPercentage,
                        ["BehaviorType"] = GetType().Name,
                        ["HasTarget"] = _lastKnownTarget != null,
                        ["Position"] = Grid.GetPosition()
                    };
                    
                    var effectiveness = _predictiveAnalyzer.PredictBehaviorEffectiveness(
                        Grid.EntityId, 
                        "LastStandBehavior", 
                        context
                    );
                    
                    // If last stand would be effective, lower the threshold
                    if (effectiveness > 0.6f && healthPercentage <= 0.3f)
                    {
                        shouldLastStand = true;
                    }
                }
                
                if (shouldLastStand)
                {
                    Logger.Warn($"{Name} should enter last stand mode at {healthPercentage:P0} health");
                    
                    _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "LastStandDecision", new Dictionary<string, object>
                    {
                        ["HealthPercentage"] = healthPercentage,
                        ["BehaviorType"] = GetType().Name,
                        ["Decision"] = shouldLastStand
                    });
                }
                
                return shouldLastStand;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error calculating last stand decision in {Name}");
                return healthPercentage <= 0.2f; // Fallback to simple threshold
            }
        }

        /// <summary>
        /// Main tick method for the behavior. Override in derived classes for specific behavior logic.
        /// </summary>
        public virtual void Tick()
        {
            try
            {
                // Update performance tracking
                Update();
                
                if (Grid != null)
                {
                    _predictiveAnalyzer?.UpdateMovementHistory(Grid);
                }
                
                OnTick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error in Tick method for {Name}");
            }
        }

        /// <summary>
        /// Override this method in derived classes for specific behavior tick logic.
        /// </summary>
        protected virtual void OnTick()
        {
            // Default: do nothing - individual behaviors should override this
        }

        private void InitializePerformanceMetrics()
        {
            _performanceMetrics["TargetsEngaged"] = 0f;
            _performanceMetrics["SuccessfulEngagements"] = 0f;
            _performanceMetrics["DamageDealt"] = 0f;
            _performanceMetrics["DamageTaken"] = 0f;
            _performanceMetrics["TimeAlive"] = 0f;
            _performanceMetrics["DistanceTraveled"] = 0f;
        }

        /// <summary>
        /// Enhanced target selection using predictive analysis.
        /// </summary>
        public virtual EnemyEntity SelectTarget(List<EnemyEntity> enemies)
        {
            try
            {
                if (enemies == null || enemies.Count == 0 || Npc?.Position == null)
                    return null;

                EnemyEntity best = null;
                var bestScore = float.MinValue;

                foreach (var enemy in enemies)
                {
                    if (enemy?.IsValid() != true) continue;

                    var score = CalculateTargetScore(enemy);
                    if (score > bestScore)
                    {
                        best = enemy;
                        bestScore = score;
                    }
                }

                if (best != null)
                {
                    RecordTargetSelection(best);
                    Logger.Debug($"{Name} selected target: {best.Entity?.DisplayName} (score: {bestScore:F2})");
                }

                return best;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error selecting target in {Name}");
                return null;
            }
        }

        /// <summary>
        /// Calculates a target priority score using multiple factors.
        /// </summary>
        protected virtual float CalculateTargetScore(EnemyEntity enemy)
        {
            try
            {
                var myPos = Npc?.Position ?? Vector3D.Zero;
                var distance = enemy.DistanceFrom(myPos);
                
                var score = 0f;

                // Distance factor (closer = better, but not too close)
                var optimalRange = GetOptimalEngagementRange();
                var distanceFactor = 1.0f - Math.Abs((float)(distance - optimalRange)) / optimalRange;
                score += distanceFactor * 0.3f;

                // Threat level (higher threat = higher priority)
                var threatLevel = CalculateEnemyThreatLevel(enemy);
                score += threatLevel * 0.4f;

                // Target predictability (more predictable = easier to hit)
                if (_predictiveAnalyzer != null && enemy.Entity != null)
                {
                    _predictiveAnalyzer.UpdateMovementHistory(enemy.Entity);
                    var predictability = GetTargetPredictability(enemy.Entity);
                    score += predictability * 0.2f;
                }

                // Health factor (lower health = easier target)
                var healthFactor = 1.0f - GetTargetHealthRatio(enemy);
                score += healthFactor * 0.1f;

                return score;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating target score");
                return 0f;
            }
        }

        protected virtual float GetOptimalEngagementRange()
        {
            // Default optimal range - subclasses can override
            return 1200f;
        }

        protected virtual float CalculateEnemyThreatLevel(EnemyEntity enemy)
        {
            try
            {
                if (enemy.Entity is IMyCubeGrid grid)
                {
                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    
                    var weaponCount = blocks.Count(b => IsWeaponBlock(b));
                    var totalBlocks = blocks.Count;
                    
                    return Math.Min(weaponCount / (float)Math.Max(totalBlocks, 1) * 2.0f, 1.0f);
                }
                else if (enemy.Entity is IMyCharacter)
                {
                    return 0.3f; // Players are moderate threat
                }
                
                return 0.5f; // Default moderate threat
            }
            catch
            {
                return 0.5f;
            }
        }

        protected virtual bool IsWeaponBlock(IMySlimBlock block)
        {
            var subtype = block.BlockDefinition.Id.SubtypeName;
            return subtype.Contains("Gatling") || 
                   subtype.Contains("Missile") || 
                   subtype.Contains("Laser") || 
                   subtype.Contains("Railgun") ||
                   subtype.Contains("Turret");
        }

        protected virtual float GetTargetPredictability(IMyEntity target)
        {
            // Use movement history to determine predictability
            // Higher value = more predictable = easier to hit
            return 0.5f; // Default - subclasses can implement more sophisticated logic
        }

        protected virtual float GetTargetHealthRatio(EnemyEntity enemy)
        {
            try
            {
                if (enemy.Entity is IMyCubeGrid grid)
                {
                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    
                    if (blocks.Count == 0) return 0f;
                    
                    var totalIntegrity = blocks.Sum(b => b.Integrity);
                    var maxIntegrity = blocks.Sum(b => b.MaxIntegrity);
                    
                    return maxIntegrity > 0 ? totalIntegrity / maxIntegrity : 1.0f;
                }
                
                return 1.0f; // Assume full health for non-grids
            }
            catch
            {
                return 1.0f;
            }
        }

        private void RecordTargetSelection(EnemyEntity target)
        {
            try
            {
                _lastKnownTarget = target.Entity;
                _lastKnownTargetPosition = target.Position;
                _lastTargetUpdate = DateTime.UtcNow;
                
                _performanceMetrics["TargetsEngaged"]++;
                
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "TargetSelected", new Dictionary<string, object>
                {
                    ["TargetType"] = target.Entity?.GetType().Name ?? "Unknown",
                    ["Distance"] = target.DistanceFrom(Npc?.Position ?? Vector3D.Zero),
                    ["BehaviorType"] = GetType().Name,
                    ["SelectionTime"] = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error recording target selection");
            }
        }

        /// <summary>
        /// Enhanced tactical movement with predictive positioning.
        /// </summary>
        public virtual void ExecuteTacticalMovement(EnemyEntity target, bool underFire)
        {
            try
            {
                if (target?.IsValid() != true || Npc == null)
                    return;

                Vector3D targetPosition;

                if (_predictiveAnalyzer != null && target.Entity != null)
                {
                    // Predict target position 2-5 seconds ahead based on situation
                    var predictionTime = underFire ? 2.0f : 3.5f;
                    targetPosition = _predictiveAnalyzer.PredictEnemyPosition(target.Entity, predictionTime);
                }
                else
                {
                    targetPosition = target.Position;
                }

                if (underFire)
                {
                    targetPosition = CalculateEvasivePosition(targetPosition);
                }

                Npc.MoveTo(targetPosition);

                var distanceMoved = Vector3D.Distance(Npc.Position, targetPosition);
                _performanceMetrics["DistanceTraveled"] += (float)distanceMoved;

                Logger.Debug($"{Name} tactical movement to {targetPosition} (predicted: {_predictiveAnalyzer != null})");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing tactical movement in {Name}");
            }
        }

        protected virtual Vector3D CalculateEvasivePosition(Vector3D targetPosition)
        {
            try
            {
                var myPos = Npc?.Position ?? Vector3D.Zero;
                var direction = Vector3D.Normalize(targetPosition - myPos);
                
                var random = new Random();
                var evasionVector = new Vector3D(
                    (random.NextDouble() - 0.5) * 200,
                    (random.NextDouble() - 0.5) * 200,
                    (random.NextDouble() - 0.5) * 200
                );
                
                return targetPosition + evasionVector;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating evasive position");
                return targetPosition;
            }
        }

        /// <summary>
        /// Enhanced retreat decision using predictive analysis.
        /// </summary>
        public virtual bool ShouldRetreat(float myStrength, float enemyStrength)
        {
            try
            {
                var basicRetreat = myStrength < enemyStrength * 0.7f;
                
                if (_predictiveAnalyzer != null && Grid != null)
                {
                    var effectiveness = _predictiveAnalyzer.PredictBehaviorEffectiveness(
                        Grid.EntityId, 
                        GetType().Name, 
                        BuildCurrentContext()
                    );
                    
                    // If behavior is not effective, consider retreat even with good strength ratio
                    if (effectiveness < 0.3f && myStrength < enemyStrength * 0.9f)
                    {
                        basicRetreat = true;
                    }
                }
                
                if (basicRetreat)
                {
                    Logger.Debug($"{Name} should retreat: My strength {myStrength} vs Enemy {enemyStrength}");
                    
                    _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "RetreatDecision", new Dictionary<string, object>
                    {
                        ["MyStrength"] = myStrength,
                        ["EnemyStrength"] = enemyStrength,
                        ["BehaviorType"] = GetType().Name
                    });
                }
                
                return basicRetreat;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error calculating retreat decision in {Name}");
                return false;
            }
        }

        protected Dictionary<string, object> BuildCurrentContext()
        {
            try
            {
                var context = new Dictionary<string, object>();
                
                if (Npc != null)
                {
                    context["Position"] = Npc.Position;
                    context["HasTarget"] = _lastKnownTarget != null;
                    context["BehaviorDuration"] = (DateTime.UtcNow - _behaviorStartTime).TotalSeconds;
                }
                
                if (_lastKnownTarget != null)
                {
                    context["TargetDistance"] = Vector3D.Distance(
                        Npc?.Position ?? Vector3D.Zero, 
                        _lastKnownTargetPosition
                    );
                }
                
                return context;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error building current context");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Enhanced damage handling with learning.
        /// </summary>
        public virtual void OnDamaged()
        {
            try
            {
                _performanceMetrics["DamageTaken"]++;
                
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "DamageReceived", new Dictionary<string, object>
                {
                    ["BehaviorType"] = GetType().Name,
                    ["HasTarget"] = _lastKnownTarget != null,
                    ["DamageTime"] = DateTime.UtcNow
                });
                
                Logger.Debug($"{Name} received damage notification");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling damage event in {Name}");
            }
        }

        /// <summary>
        /// Enhanced target acquisition with learning.
        /// </summary>
        public virtual void OnTargetAcquired()
        {
            try
            {
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "TargetAcquired", new Dictionary<string, object>
                {
                    ["BehaviorType"] = GetType().Name,
                    ["AcquisitionTime"] = DateTime.UtcNow
                });
                
                Logger.Debug($"{Name} acquired target");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling target acquired event in {Name}");
            }
        }

        /// <summary>
        /// Enhanced target loss handling with predictive search.
        /// </summary>
        public virtual void OnTargetLost()
        {
            try
            {
                if (_lastKnownTarget != null && _predictiveAnalyzer != null)
                {
                    // Try to predict where the target went
                    var predictedPosition = _predictiveAnalyzer.PredictEnemyPosition(_lastKnownTarget, 5.0f);
                    Logger.Debug($"{Name} lost target, predicted location: {predictedPosition}");
                }
                
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "TargetLost", new Dictionary<string, object>
                {
                    ["BehaviorType"] = GetType().Name,
                    ["LossTime"] = DateTime.UtcNow,
                    ["LastKnownPosition"] = _lastKnownTargetPosition
                });
                
                Logger.Debug($"{Name} lost target");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error handling target lost event in {Name}");
            }
        }

        /// <summary>
        /// Enhanced update with performance tracking.
        /// </summary>
        public virtual void Update()
        {
            try
            {
                // Update time alive metric
                _performanceMetrics["TimeAlive"] = (float)(DateTime.UtcNow - _behaviorStartTime).TotalSeconds;
                
                if (DateTime.UtcNow.Subtract(_behaviorStartTime).TotalSeconds % 60 < 1) // Every minute
                {
                    RecordPerformanceMetrics();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error in Update method for {Name}");
            }
        }

        protected virtual void RecordPerformanceMetrics()
        {
            try
            {
                _predictiveAnalyzer?.RecordEvent(Grid?.EntityId ?? 0, "PerformanceUpdate", 
                    _performanceMetrics.ToDictionary(k => k.Key, v => (object)v.Value));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error recording performance metrics");
            }
        }

        /// <summary>
        /// Get current behavior effectiveness score.
        /// </summary>
        public virtual float GetEffectivenessScore()
        {
            try
            {
                var engagements = _performanceMetrics["TargetsEngaged"];
                var successes = _performanceMetrics["SuccessfulEngagements"];
                
                if (engagements == 0) return 0.5f; // Neutral if no data
                
                return successes / engagements;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating effectiveness score");
                return 0.5f;
            }
        }

        /// <summary>
        /// Disposes the behavior and releases resources.
        /// </summary>
        public virtual void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (_predictiveAnalyzer != null && Grid != null)
                    {
                        _predictiveAnalyzer.RecordEvent(Grid.EntityId, "BehaviorCompleted", new Dictionary<string, object>
                        {
                            ["BehaviorType"] = GetType().Name,
                            ["Duration"] = (DateTime.UtcNow - _behaviorStartTime).TotalSeconds,
                            ["FinalEffectiveness"] = GetEffectivenessScore(),
                            ["PerformanceMetrics"] = _performanceMetrics.ToDictionary(k => k.Key, v => (object)v.Value)
                        });
                    }
                    
                    _predictiveAnalyzer?.Dispose();
                    _performanceMetrics?.Clear();
                }
                
                Grid = null;
                Npc = null;
                PatrolFallback = null;
                _lastKnownTarget = null;
                
                Logger.Debug($"{Name} behavior disposed with performance data recorded");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error disposing {Name} behavior");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}