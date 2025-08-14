using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.ModAPI;
using VRageRender;
using System;
using System.Linq;
using Helios.Modules.AI;
using NLog;
using Sandbox.ModAPI; 

namespace HeliosAI.Behaviors
{
    public class AttackBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("AttackBehavior");
        public IMyEntity Target { get; private set; }
        private float _lastHealth = 1f;
        private DateTime _lastTargetPositionUpdate = DateTime.MinValue;
        private Vector3D _lastTargetVelocity = Vector3D.Zero;
        private DateTime _lastWeaponFireTime = DateTime.MinValue;
        private bool _isEngaging = false;
        private float _engagementRange = 1200f;

        public override string Name => "Attack";

        public AttackBehavior(IMyCubeGrid grid, IMyEntity target) : base(grid)
        {
            Target = target;
            
            if (target != null)
            {
                _lastKnownTarget = target;
                _lastKnownTargetPosition = target.GetPosition();
                OnTargetAcquired();
            }
            
            Logger.Info($"[{Grid?.DisplayName}] AttackBehavior initialized with target: {target?.DisplayName ?? "None"}");
        }

        protected override void OnTick()
        {
            try
            {
                base.OnTick();

                if (TargetInvalid())
                {
                    HandleTargetLoss();
                    return;
                }

                // Enhanced attack sequence with predictive analysis
                PerformIntelligentAttack();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in AttackBehavior.OnTick()");
            }
        }

        private void PerformIntelligentAttack()
        {
            try
            {
                if (Grid?.Physics == null || Grid.PositionComp == null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Grid physics or position invalid");
                    return;
                }

                UpdateTargetTracking();
                PerformPredictiveMovement();
                PerformPredictiveWeaponTargeting();
                MonitorHealthAndRetreat();
                UpdateEngagementMetrics();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent attack sequence");
            }
        }

        private void UpdateTargetTracking()
        {
            try
            {
                if (Target == null) return;

                var currentPos = Target.GetPosition();
                var currentTime = DateTime.UtcNow;

                if (_lastTargetPositionUpdate != DateTime.MinValue)
                {
                    var deltaTime = (currentTime - _lastTargetPositionUpdate).TotalSeconds;
                    if (deltaTime > 0)
                    {
                        var deltaPos = currentPos - _lastKnownTargetPosition;
                        _lastTargetVelocity = deltaPos / deltaTime;
                    }
                }

                _lastKnownTargetPosition = currentPos;
                _lastTargetPositionUpdate = currentTime;

                _predictiveAnalyzer?.UpdateMovementHistory(Target);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating target tracking");
            }
        }

        private void PerformPredictiveMovement()
        {
            try
            {
                if (Npc == null || Target == null) return;

                Vector3D targetPosition;

                if (_predictiveAnalyzer != null)
                {
                    targetPosition = _predictiveAnalyzer.PredictEnemyPosition(Target, 3.0f);
                    
                    var weaponAnalysis = _predictiveAnalyzer.AnalyzeOptimalWeapons(Target);
                    if (weaponAnalysis != null)
                    {
                        _engagementRange = weaponAnalysis.OptimalRange;
                    }
                }
                else
                {
                    targetPosition = _lastKnownTargetPosition + _lastTargetVelocity * 3.0f;
                }

                var myPos = Grid.GetPosition();
                var distance = Vector3D.Distance(myPos, targetPosition);
                
                Vector3D movePosition;

                if (distance > _engagementRange * 1.2) // Too far
                {
                    var direction = Vector3D.Normalize(targetPosition - myPos);
                    movePosition = targetPosition - direction * _engagementRange;
                }
                else if (distance < _engagementRange * 0.7) // Too close
                {
                    var direction = Vector3D.Normalize(myPos - targetPosition);
                    movePosition = targetPosition + direction * _engagementRange;
                }
                else
                {
                    var perpendicular = Vector3D.CalculatePerpendicularVector(targetPosition - myPos);
                    movePosition = myPos + perpendicular * 50; // Small lateral movement
                }

                // Check if under fire for evasive maneuvers
                var currentHealth = CalculateGridIntegrity(Grid);
                var underFire = currentHealth < _lastHealth - 0.05f;

                if (underFire)
                {
                    movePosition = CalculateEvasivePosition(movePosition);
                }

                Npc.MoveTo(movePosition);

                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "AttackMovement", new Dictionary<string, object>
                {
                    ["TargetPosition"] = targetPosition,
                    ["MovePosition"] = movePosition,
                    ["Distance"] = distance,
                    ["OptimalRange"] = _engagementRange,
                    ["UnderFire"] = underFire
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in predictive movement");
            }
        }

        private void PerformPredictiveWeaponTargeting()
        {
            try
            {
                var wc = HeliosAIPlugin.WeaponCoreManager;
                if (wc == null || Target == null || !wc.HasReadyWeapons(Grid))
                    return;

                Vector3D targetPosition;
                var targetVelocity = Vector3D.Zero;

                // Enhanced target prediction
                if (_predictiveAnalyzer != null)
                {
                    // Predict target position based on weapon travel time
                    var distance = Vector3D.Distance(Grid.GetPosition(), Target.GetPosition());
                    var projectileSpeed = 400f; // Average projectile speed - could be weapon-specific
                    var travelTime = distance / projectileSpeed;
                    
                    targetPosition = _predictiveAnalyzer.PredictEnemyPosition(Target, (float)travelTime);
                    
                    // Get target velocity for weapon core
                    if (Target.Physics != null)
                    {
                        targetVelocity = Target.Physics.LinearVelocity;
                    }
                }
                else
                {
                    var travelTime = Vector3D.Distance(Grid.GetPosition(), Target.GetPosition()) / 400f;
                    targetPosition = _lastKnownTargetPosition + _lastTargetVelocity * travelTime;
                    targetVelocity = _lastTargetVelocity;
                }

                var info = new MyDetectedEntityInfo(
                    Target.EntityId,
                    Target.DisplayName ?? "Unknown",
                    MyDetectedEntityType.CharacterHuman,
                    targetPosition,
                    Target.WorldMatrix,
                    (Vector3)targetVelocity,
                    MyRelationsBetweenPlayerAndBlock.Enemies,
                    Target.PositionComp?.WorldAABB ?? new BoundingBoxD(),
                    0
                );

                wc.SetTarget(Grid, info);
                
                if (wc.HasReadyWeapons(Grid))
                {
                    _lastWeaponFireTime = DateTime.UtcNow;
                    _isEngaging = true;
                    
                    // Update success metrics if we're hitting
                    if (IsTargetTakingDamage())
                    {
                        _performanceMetrics["SuccessfulEngagements"]++;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in predictive weapon targeting");
            }
        }

        private bool IsTargetTakingDamage()
        {
            try
            {
                // Simple heuristic: if we've been firing recently and target is close
                var timeSinceLastFire = (DateTime.UtcNow - _lastWeaponFireTime).TotalSeconds;
                var distance = GetDistanceToTarget();
                
                return timeSinceLastFire < 5.0 && distance < _engagementRange * 1.5;
            }
            catch
            {
                return false;
            }
        }

        private void MonitorHealthAndRetreat()
        {
            try
            {
                var currentHealth = CalculateGridIntegrity(Grid);
                var healthDelta = _lastHealth - currentHealth;
                
                if (healthDelta > 0.05f) // Taking significant damage
                {
                    OnDamaged();
                    
                    var shouldRetreat = ShouldRetreat(currentHealth, 1.0f); // Assume enemy at full strength
                    var shouldLastStand = ShouldEnterLastStand(currentHealth);
                    
                    if (shouldLastStand)
                    {
                        // Convert to aggressive last stand behavior
                        _engagementRange *= 0.7f; // Get closer for last stand
                        Logger.Warn($"[{Grid?.DisplayName}] Entering last stand mode! Health: {currentHealth:P}");
                        
                        _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "LastStandTriggered", new Dictionary<string, object>
                        {
                            ["Health"] = currentHealth,
                            ["EngagementRange"] = _engagementRange
                        });
                    }
                    else if (shouldRetreat)
                    {
                        Logger.Info($"[{Grid?.DisplayName}] Intelligent retreat triggered! Health: {currentHealth:P}");
                        
                        if (Npc != null)
                        {
                            // Find nearest threat to retreat from
                            var threats = FindNearbyThreats();
                            var threatToRetreatFrom = threats.FirstOrDefault();
                            
                            Npc.SetBehavior(new RetreatBehavior(Grid, threatToRetreatFrom));
                        }
                        return;
                    }
                }
                
                _lastHealth = currentHealth;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error monitoring health and retreat");
            }
        }

        private List<IMyEntity> FindNearbyThreats()
        {
            var threats = new List<IMyEntity>();
            
            try
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(Grid.GetPosition(), entity.GetPosition());
                    return distance <= 3000; // 3km threat scan range
                });

                foreach (var entity in entities)
                {
                    if (entity is IMyCharacter || 
                        (entity is IMyCubeGrid grid && IsHostileGrid(grid)))
                    {
                        threats.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding nearby threats");
            }

            return threats;
        }

        private bool IsHostileGrid(IMyCubeGrid grid)
        {
            try
            {
                var myOwner = Grid.BigOwners.FirstOrDefault();
                var otherOwner = grid.BigOwners.FirstOrDefault();
                
                if (myOwner == 0 || otherOwner == 0) return true; // Unknown = hostile
                
                var myFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(myOwner);
                var otherFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(otherOwner);
                
                if (myFaction == null || otherFaction == null) return true;
                
                return myFaction.FactionId != otherFaction.FactionId;
            }
            catch
            {
                return true; // Assume hostile on error
            }
        }

        private void UpdateEngagementMetrics()
        {
            try
            {
                if (_isEngaging)
                {
                    _performanceMetrics["DamageDealt"] += 1.0f; // Placeholder for actual damage dealt
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating engagement metrics");
            }
        }

        private void HandleTargetLoss()
        {
            try
            {
                Logger.Debug($"[{Grid?.DisplayName}] Target lost in attack behavior");
                OnTargetLost(); 
                
                var newTarget = FindNewTarget();
                if (newTarget != null)
                {
                    SetTarget(newTarget);
                    OnTargetAcquired();
                    Logger.Info($"[{Grid?.DisplayName}] New target acquired: {newTarget.DisplayName}");
                }
                else
                {
                    // No new target found - behavior will be changed by AiManager
                    Logger.Debug($"[{Grid?.DisplayName}] No new target found");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling target loss");
            }
        }

        private IMyEntity FindNewTarget()
        {
            try
            {
                var aiManager = AiManager.Instance;
                if (aiManager != null && Npc != null)
                {
                    return aiManager.FindTarget(
                        Grid.GetPosition(), 
                        3000, // 3km search range
                        Npc.Mood, 
                        Grid.BigOwners.FirstOrDefault()
                    );
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding new target");
                return null;
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                if (Target != null && _predictiveAnalyzer != null)
                {
                    return _predictiveAnalyzer.PredictEnemyPosition(Target, 2.0f);
                }
                else if (Target != null)
                {
                    return Target.GetPosition();
                }
                
                return base.GetNextWaypoint();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return base.GetNextWaypoint();
            }
        }

        protected override float GetOptimalEngagementRange()
        {
            return _engagementRange;
        }

        public void DebugDraw()
        {
            try
            {
                if (Target?.MarkedForClose == false && Grid != null)
                {
                    var material = VRage.Utils.MyStringId.GetOrCompute("WeaponLaser");
                    var color = new Vector4(1, 0, 0, 1); // Red
                    
                    var gridPos = Grid.GetPosition();
                    var targetPos = Target.GetPosition();
                    
                    MySimpleObjectDraw.DrawLine(
                        gridPos,
                        targetPos,
                        material,
                        ref color,
                        0.1f,
                        MyBillboard.BlendTypeEnum.PostPP
                    );
                    
                    if (_predictiveAnalyzer != null)
                    {
                        var predictedPos = _predictiveAnalyzer.PredictEnemyPosition(Target, 3.0f);
                        var predictColor = new Vector4(1, 1, 0, 1); // Yellow
                        
                        MySimpleObjectDraw.DrawLine(
                            gridPos,
                            predictedPos,
                            material,
                            ref predictColor,
                            0.05f,
                            MyBillboard.BlendTypeEnum.PostPP
                        );
                    }
                    
                    MyRenderProxy.DebugDrawText3D(
                        targetPos, 
                        $"Target: {Target.DisplayName ?? "Unknown"}\nRange: {GetDistanceToTarget():F0}m\nEngaging: {_isEngaging}", 
                        Color.Red, 
                        0.8f, 
                        false
                    );
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in DebugDraw");
            }
        }

        public bool TargetInvalid()
        {
            try
            {
                if (Target == null || Target.MarkedForClose || Grid == null)
                {
                    return true;
                }

                var distance = GetDistanceToTarget();
                if (distance > 2000) // Increased from 1500 for better persistence
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Target too far: {distance:F0}m");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error checking target validity");
                return true;
            }
        }
        
        private float CalculateGridIntegrity(IMyCubeGrid grid)
        {
            try
            {
                if (grid == null) return 0f;

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

                if (blocks.Count == 0) return 0f;

                float max = 0f, current = 0f;
                foreach (var block in blocks)
                {
                    if (block == null) continue;
                    
                    max += block.MaxIntegrity;
                    current += block.Integrity;
                }

                return max > 0f ? current / max : 1f;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{grid?.DisplayName}] Error calculating grid integrity");
                return _lastHealth;
            }
        }

        public void SetTarget(IMyEntity newTarget)
        {
            try
            {
                if (newTarget == null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Attempted to set null target");
                    return;
                }

                var oldTarget = Target;
                Target = newTarget;
                _lastKnownTarget = newTarget;
                _lastKnownTargetPosition = newTarget.GetPosition();
                
                Logger.Info($"[{Grid?.DisplayName}] Target changed from {oldTarget?.DisplayName ?? "None"} to {newTarget.DisplayName}");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "TargetChanged", new Dictionary<string, object>
                {
                    ["OldTargetId"] = oldTarget?.EntityId ?? 0,
                    ["NewTargetId"] = newTarget.EntityId,
                    ["NewTargetType"] = newTarget.GetType().Name
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting target");
            }
        }

        public bool HasValidTarget()
        {
            return !TargetInvalid();
        }

        public double GetDistanceToTarget()
        {
            try
            {
                if (TargetInvalid()) return double.MaxValue;
                return Vector3D.Distance(Grid.GetPosition(), Target.GetPosition());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating distance to target");
                return double.MaxValue;
            }
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    if (_predictiveAnalyzer != null && Grid != null)
                    {
                        _predictiveAnalyzer.RecordEvent(Grid.EntityId, "AttackBehaviorCompleted", new Dictionary<string, object>
                        {
                            ["FinalTarget"] = Target?.DisplayName ?? "None",
                            ["EngagementTime"] = (DateTime.UtcNow - _behaviorStartTime).TotalSeconds,
                            ["WasEngaging"] = _isEngaging,
                            ["FinalHealth"] = _lastHealth
                        });
                    }
                }
                
                Target = null;
                Logger.Debug($"[{Grid?.DisplayName}] AttackBehavior disposed");
                
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing AttackBehavior");
            }
        }
    }
}