using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using NLog;

namespace HeliosAI.Behaviors
{
    public class FollowBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("FollowBehavior");
        
        public IMyEntity Target { get; private set; }
        public double FollowDistance { get; set; } = 50;
        private Vector3D _lastTargetPosition;
        private Vector3D _targetVelocity = Vector3D.Zero;
        private DateTime _lastPositionUpdate = DateTime.MinValue;
        private Vector3D _optimalFollowPosition;
        private bool _isFormationFollowing = false;
        private Vector3D _formationOffset = Vector3D.Zero;
        private double _maxFollowSpeed = 100; // m/s
        private DateTime _lastObstacleCheck = DateTime.MinValue;
        private List<Vector3D> _obstructionHistory = new List<Vector3D>();
        
        public override string Name => "Follow";

        public FollowBehavior(IMyCubeGrid grid, IMyEntity target, double followDistance = 50) : base(grid)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            FollowDistance = followDistance;
            
            _lastTargetPosition = target.GetPosition();
            _optimalFollowPosition = _lastTargetPosition;
            
            if (target != null)
            {
                _lastKnownTarget = target;
                _lastKnownTargetPosition = target.GetPosition();
                OnTargetAcquired();
            }
            
            Logger.Info($"[{Grid?.DisplayName}] FollowBehavior initialized with target: {target?.DisplayName ?? "None"}, distance: {followDistance}m");
        }

        protected override void OnTick()
        {
            try
            {
                base.OnTick();

                if (!IsTargetValid())
                {
                    HandleTargetLoss();
                    return;
                }

                // Enhanced following with predictive positioning
                PerformIntelligentFollow();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in FollowBehavior.OnTick()");
            }
        }

        private void PerformIntelligentFollow()
        {
            try
            {
                if (Grid?.Physics == null || Grid.PositionComp == null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Grid physics or position invalid");
                    return;
                }

                UpdateTargetTracking();
                CalculateOptimalFollowPosition();
                ExecuteIntelligentMovement();
                AdaptFollowingBehavior();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent follow sequence");
            }
        }

        private void UpdateTargetTracking()
        {
            try
            {
                if (Target == null) return;

                var currentPos = Target.GetPosition();
                var currentTime = DateTime.UtcNow;

                // Calculate target velocity for prediction
                if (_lastPositionUpdate != DateTime.MinValue)
                {
                    var deltaTime = (currentTime - _lastPositionUpdate).TotalSeconds;
                    if (deltaTime > 0)
                    {
                        var deltaPos = currentPos - _lastTargetPosition;
                        _targetVelocity = deltaPos / deltaTime;
                        
                        // Smooth velocity to reduce jitter
                        if (_targetVelocity.Length() > _maxFollowSpeed)
                        {
                            _targetVelocity = Vector3D.Normalize(_targetVelocity) * _maxFollowSpeed;
                        }
                    }
                }

                _lastTargetPosition = currentPos;
                _lastKnownTargetPosition = currentPos;
                _lastPositionUpdate = currentTime;
                
                // Update last known target for predictive analysis
                _predictiveAnalyzer?.UpdateMovementHistory(Target);
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FollowTracking", new Dictionary<string, object>
                {
                    ["TargetPosition"] = currentPos,
                    ["TargetVelocity"] = _targetVelocity.Length(),
                    ["FollowDistance"] = GetDistanceToTarget()
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating target tracking");
            }
        }

        private void CalculateOptimalFollowPosition()
        {
            try
            {
                if (Target == null) return;

                Vector3D targetPosition;

                if (_predictiveAnalyzer != null)
                {
                    // Predict where target will be in 2-5 seconds
                    var predictionTime = CalculateOptimalPredictionTime();
                    targetPosition = _predictiveAnalyzer.PredictEnemyPosition(Target, predictionTime);
                }
                else
                {
                    var predictionTime = 3.0f;
                    targetPosition = _lastTargetPosition + _targetVelocity * predictionTime;
                }

                // Calculate optimal follow position
                if (_isFormationFollowing)
                {
                    _optimalFollowPosition = CalculateFormationPosition(targetPosition);
                }
                else
                {
                    _optimalFollowPosition = CalculateTrailingPosition(targetPosition);
                }

                // Check for obstacles and adjust if needed
                _optimalFollowPosition = AvoidObstacles(_optimalFollowPosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating optimal follow position");
                _optimalFollowPosition = _lastTargetPosition;
            }
        }

        private float CalculateOptimalPredictionTime()
        {
            try
            {
                var distance = GetDistanceToTarget();
                var mySpeed = Grid.Physics?.LinearVelocity.Length() ?? 0;
        
                // Shorter prediction for close following, longer for distant
                var basePrediction = distance > FollowDistance * 2 ? 5.0f : 2.0f;
        
                // Adjust based on relative speeds
                var relativeSpeed = Math.Abs(_targetVelocity.Length() - mySpeed);
                var speedFactor = Math.Min(relativeSpeed / 50.0, 1.0); // Cap at 50 m/s difference
        
                return (float)(basePrediction * (1.0f + speedFactor)); 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating prediction time");
                return 3.0f;
            }
        }

        private Vector3D CalculateFormationPosition(Vector3D targetPosition)
        {
            try
            {
                // Formation following - maintain relative offset
                var targetForward = Vector3D.Forward; // Default direction
                
                if (Target.Physics != null && Target.Physics.LinearVelocity.Length() > 1.0)
                {
                    targetForward = Vector3D.Normalize(Target.Physics.LinearVelocity);
                }
                else if (Target is IMyCubeGrid targetGrid)
                {
                    targetForward = targetGrid.WorldMatrix.Forward;
                }

                var targetRight = Vector3D.CalculatePerpendicularVector(targetForward);
                var targetUp = Vector3D.Cross(targetForward, targetRight);

                // Apply formation offset
                return targetPosition + 
                       targetForward * _formationOffset.Z +
                       targetRight * _formationOffset.X +
                       targetUp * _formationOffset.Y;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating formation position");
                return targetPosition;
            }
        }

        private Vector3D CalculateTrailingPosition(Vector3D targetPosition)
        {
            try
            {
                var direction = Vector3D.Zero;
                
                if (_targetVelocity.Length() > 1.0)
                {
                    direction = -Vector3D.Normalize(_targetVelocity);
                }
                else
                {
                    var myPos = Grid.GetPosition();
                    var toMe = myPos - targetPosition;
                    if (toMe.Length() > 0)
                    {
                        direction = Vector3D.Normalize(toMe);
                    }
                    else
                    {
                        direction = Vector3D.Backward;
                    }
                }

                return targetPosition + direction * FollowDistance;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating trailing position");
                return targetPosition;
            }
        }

        private Vector3D AvoidObstacles(Vector3D targetPosition)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                
                // Check for obstacles every 2 seconds
                if (_lastObstacleCheck == DateTime.MinValue || 
                    currentTime.Subtract(_lastObstacleCheck).TotalSeconds > 2)
                {
                    CheckForObstacles(targetPosition);
                    _lastObstacleCheck = currentTime;
                }

                // If we have recent obstruction history, try to navigate around
                if (_obstructionHistory.Count > 0)
                {
                    return CalculateAlternativeRoute(targetPosition);
                }

                return targetPosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error avoiding obstacles");
                return targetPosition;
            }
        }

        private void CheckForObstacles(Vector3D targetPosition)
        {
            try
            {
                var myPos = Grid.GetPosition();
                var direction = targetPosition - myPos;
                var distance = direction.Length();
                
                if (distance < 10) return;
                
                direction = Vector3D.Normalize(direction);
                
                var checkDistance = Math.Min(distance, 500); // Check up to 500m ahead
                var checkPosition = myPos + direction * checkDistance;
                
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid || entity == Target)
                        return false;
                    
                    var entPos = entity.GetPosition();
                    var distanceToPath = Vector3D.Distance(entPos, checkPosition);
                    return distanceToPath < 100; // 100m clearance needed
                });

                if (entities.Any())
                {
                    // Record obstruction
                    _obstructionHistory.Add(checkPosition);
                    
                    // Keep only recent obstructions (last 10)
                    if (_obstructionHistory.Count > 10)
                    {
                        _obstructionHistory.RemoveAt(0);
                    }
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Obstacle detected, recording obstruction at {checkPosition}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking for obstacles");
            }
        }

        private Vector3D CalculateAlternativeRoute(Vector3D targetPosition)
        {
            try
            {
                var myPos = Grid.GetPosition();
                var directDirection = Vector3D.Normalize(targetPosition - myPos);
                var perpendicular = Vector3D.CalculatePerpendicularVector(directDirection);
                var alternativeOffset = perpendicular * 200; // 200m offset
                var useLeftOffset = (_obstructionHistory.Count % 2) == 0;
                if (!useLeftOffset) alternativeOffset = -alternativeOffset;
                var alternativePosition = targetPosition + alternativeOffset;
                
                Logger.Debug($"[{Grid?.DisplayName}] Using alternative route to avoid obstacles");
                
                return alternativePosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating alternative route");
                return targetPosition;
            }
        }

        private void ExecuteIntelligentMovement()
        {
            try
            {
                var myPos = Grid.GetPosition();
                var distance = Vector3D.Distance(myPos, _optimalFollowPosition);
                var targetDistance = GetDistanceToTarget();

                if (distance > FollowDistance * 0.8) 
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Following target: distance {targetDistance:F1}m, moving to optimal position");
                    Npc?.MoveTo(_optimalFollowPosition);
                    
                    // Clear obstruction history when moving successfully
                    if (_obstructionHistory.Count > 0 && distance < FollowDistance * 1.5)
                    {
                        _obstructionHistory.Clear();
                    }
                }
                else
                {
                    // We're close enough - stop autopilot
                    StopMovement();
                    Logger.Debug($"[{Grid?.DisplayName}] Close enough to target ({targetDistance:F1}m), holding position");
                }

                _performanceMetrics["DistanceTraveled"] += (float)distance;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error executing intelligent movement");
            }
        }

        private void AdaptFollowingBehavior()
        {
            try
            {
                var targetSpeed = _targetVelocity.Length();
                var mySpeed = Grid.Physics?.LinearVelocity.Length() ?? 0;
                
                var baseDistance = FollowDistance;
                var speedFactor = Math.Min(targetSpeed / 50.0, 2.0); // Cap at 2x distance
                var adaptiveDistance = baseDistance * (1.0 + speedFactor * 0.5);
                
                if (Math.Abs(adaptiveDistance - FollowDistance) > 10)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Adapting follow distance: {FollowDistance:F0}m -> {adaptiveDistance:F0}m (speed: {targetSpeed:F1}m/s)");
                    FollowDistance = adaptiveDistance;
                }

                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FollowAdaptation", new Dictionary<string, object>
                {
                    ["TargetSpeed"] = targetSpeed,
                    ["MySpeed"] = mySpeed,
                    ["AdaptiveDistance"] = adaptiveDistance,
                    ["ObstructionCount"] = _obstructionHistory.Count
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error adapting following behavior");
            }
        }

        private void StopMovement()
        {
            try
            {
                var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                    .FirstOrDefault(r => r?.IsFunctional == true);

                if (remote != null)
                {
                    remote.SetAutoPilotEnabled(false);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping movement");
            }
        }

        private void HandleTargetLoss()
        {
            try
            {
                Logger.Debug($"[{Grid?.DisplayName}] Follow target lost");
                OnTargetLost();
                
                // Try to predict where target went if we had tracking data
                if (_predictiveAnalyzer != null && _lastKnownTarget != null)
                {
                    var predictedPosition = _predictiveAnalyzer.PredictEnemyPosition(_lastKnownTarget, 10.0f);
                    var searchDistance = Vector3D.Distance(Grid.GetPosition(), predictedPosition);
                    
                    if (searchDistance < 1000) // Within reasonable search range
                    {
                        Logger.Info($"[{Grid?.DisplayName}] Searching for lost target at predicted location");
                        Npc?.MoveTo(predictedPosition);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling target loss");
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                return _optimalFollowPosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return _lastTargetPosition;
            }
        }

        public bool IsTargetValid()
        {
            try
            {
                return Target != null && !Target.MarkedForClose;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error checking target validity");
                return false;
            }
        }

        public void SetTarget(IMyEntity newTarget)
        {
            try
            {
                if (newTarget == null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Attempted to set null follow target");
                    return;
                }

                var oldTarget = Target;
                Target = newTarget;
                _lastKnownTarget = newTarget;
                _lastTargetPosition = newTarget.GetPosition();
                _lastKnownTargetPosition = _lastTargetPosition;
                
                // Reset tracking data
                _targetVelocity = Vector3D.Zero;
                _lastPositionUpdate = DateTime.MinValue;
                _obstructionHistory.Clear();
                
                OnTargetAcquired();
                
                Logger.Info($"[{Grid?.DisplayName}] Follow target changed from {oldTarget?.DisplayName ?? "None"} to {newTarget.DisplayName}");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FollowTargetChanged", new Dictionary<string, object>
                {
                    ["OldTargetId"] = oldTarget?.EntityId ?? 0,
                    ["NewTargetId"] = newTarget.EntityId,
                    ["NewTargetType"] = newTarget.GetType().Name
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting follow target");
            }
        }

        public void SetFollowDistance(double distance)
        {
            try
            {
                if (distance <= 0)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid follow distance: {distance}");
                    return;
                }

                var oldDistance = FollowDistance;
                FollowDistance = distance;
                
                Logger.Debug($"[{Grid?.DisplayName}] Follow distance changed from {oldDistance:F0}m to {distance:F0}m");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FollowDistanceChanged", new Dictionary<string, object>
                {
                    ["OldDistance"] = oldDistance,
                    ["NewDistance"] = distance
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting follow distance");
            }
        }

        public void SetFormationFollowing(bool enabled, Vector3D offset = default)
        {
            try
            {
                _isFormationFollowing = enabled;
                _formationOffset = offset;
                
                Logger.Info($"[{Grid?.DisplayName}] Formation following {(enabled ? "enabled" : "disabled")} with offset {offset}");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FormationFollowingChanged", new Dictionary<string, object>
                {
                    ["Enabled"] = enabled,
                    ["Offset"] = offset
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error setting formation following");
            }
        }

        public double GetDistanceToTarget()
        {
            try
            {
                if (!IsTargetValid() || Grid == null)
                    return double.MaxValue;

                return Vector3D.Distance(Grid.GetPosition(), Target.GetPosition());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating distance to target");
                return double.MaxValue;
            }
        }

        public bool IsInFollowRange()
        {
            try
            {
                var distance = GetDistanceToTarget();
                return distance <= FollowDistance * 1.2; // Allow 20% tolerance
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error checking follow range");
                return false;
            }
        }

        public Vector3D GetTargetPosition()
        {
            try
            {
                return IsTargetValid() ? Target.GetPosition() : Vector3D.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error getting target position");
                return Vector3D.Zero;
            }
        }

        public Vector3D GetTargetVelocity()
        {
            try
            {
                return _targetVelocity;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting target velocity");
                return Vector3D.Zero;
            }
        }

        public Vector3D GetOptimalFollowPosition()
        {
            try
            {
                return _optimalFollowPosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting optimal follow position");
                return _lastTargetPosition;
            }
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "FollowBehaviorCompleted", new Dictionary<string, object>
                    {
                        ["FinalTarget"] = Target?.DisplayName ?? "None",
                        ["FinalDistance"] = GetDistanceToTarget(),
                        ["FollowDistance"] = FollowDistance,
                        ["ObstructionsEncountered"] = _obstructionHistory.Count,
                        ["WasFormationFollowing"] = _isFormationFollowing
                    });
                }
                
                Target = null;
                _obstructionHistory.Clear();
                
                Logger.Debug($"[{Grid?.DisplayName}] FollowBehavior disposed");
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing FollowBehavior");
            }
        }
    }
}