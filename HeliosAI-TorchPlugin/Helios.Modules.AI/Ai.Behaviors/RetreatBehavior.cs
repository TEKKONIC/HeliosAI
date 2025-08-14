using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageRender;
using NLog;
using Sandbox.ModAPI;

namespace HeliosAI.Behaviors
{
    public class RetreatBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("RetreatBehavior");
        
        private Vector3D _retreatDirection;
        private double _retreatDistance = 1000;
        private Vector3D _startPosition;
        private bool _retreatComplete = false;
        private IMyEntity _primaryThreat;
        private List<IMyEntity> _knownThreats = new List<IMyEntity>();
        private DateTime _lastThreatUpdate = DateTime.MinValue;
        private Vector3D _safeRetreatPosition;
        public bool IsFindingSafeRoute { get; } = false;
        private List<Vector3D> _retreatWaypoints = new List<Vector3D>();
        private int _currentWaypointIndex = 0;
        private DateTime _retreatStartTime = DateTime.UtcNow;
        public float RetreatSpeed { get; } = 80f;
        private bool _evasiveManeuvers = false;
        private DateTime _lastEvasiveAction = DateTime.MinValue;

        public override string Name => "Retreat";

        public RetreatBehavior(IMyCubeGrid grid, IMyEntity attacker = null) : base(grid)
        {
            try
            {
                _startPosition = grid.GetPosition();
                _primaryThreat = attacker;
                _retreatStartTime = DateTime.UtcNow;

                InitializeIntelligentRetreat(attacker);
                
                RequestBackupDuringRetreat();
                
                Logger.Info($"[{Grid?.DisplayName}] Enhanced retreat initiated from {(attacker?.DisplayName ?? "unknown threat")}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error initializing RetreatBehavior");
            }
        }

        protected override void OnTick()
        {
            try
            {
                base.OnTick();

                if (Grid?.Physics == null || Grid.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping retreat");
                    return;
                }

                if (_retreatComplete)
                {
                    HandleRetreatCompletion();
                    return;
                }

                PerformIntelligentRetreat();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in RetreatBehavior.OnTick()");
            }
        }

        private void InitializeIntelligentRetreat(IMyEntity attacker)
        {
            try
            {
                IdentifyThreats();
                CalculateOptimalRetreatDirection(attacker);
                PlanSafeRetreatRoute();
                
                if (attacker != null)
                {
                    _lastKnownTarget = attacker;
                    _lastKnownTargetPosition = attacker.GetPosition();
                    _predictiveAnalyzer?.UpdateMovementHistory(attacker);
                }

                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "RetreatInitiated", new Dictionary<string, object>
                {
                    ["AttackerId"] = attacker?.EntityId ?? 0,
                    ["AttackerType"] = attacker?.GetType().Name ?? "Unknown",
                    ["StartPosition"] = _startPosition,
                    ["ThreatsIdentified"] = _knownThreats.Count,
                    ["PlannedDistance"] = _retreatDistance
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing intelligent retreat");
            }
        }

        private void IdentifyThreats()
        {
            try
            {
                _knownThreats.Clear();
                var scanRadius = 2000.0; // 2km threat identification radius
                
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(_startPosition, entity.GetPosition());
                    return distance <= scanRadius;
                });

                foreach (var entity in entities)
                {
                    if (IsHostileEntity(entity))
                    {
                        _knownThreats.Add(entity);
                        _predictiveAnalyzer?.UpdateMovementHistory(entity);
                    }
                }

                Logger.Debug($"[{Grid?.DisplayName}] Identified {_knownThreats.Count} threats during retreat initialization");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error identifying threats");
            }
        }

        private bool IsHostileEntity(IMyEntity entity)
        {
            try
            {
                if (entity is IMyCharacter character)
                {
                    var playerId = character.ControllerInfo?.ControllingIdentityId ?? 0;
                    return IsHostilePlayer(playerId);
                }
                else if (entity is IMyCubeGrid grid)
                {
                    var gridOwner = grid.BigOwners.FirstOrDefault();
                    return IsHostilePlayer(gridOwner);
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool IsHostilePlayer(long playerId)
        {
            if (playerId == 0) return false;
            
            try
            {
                var myOwner = Grid.BigOwners.FirstOrDefault();
                if (myOwner == 0) return true;
                
                var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                var myFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(myOwner);
                
                if (playerFaction == null || myFaction == null) return true;
                
                return playerFaction.FactionId != myFaction.FactionId;
            }
            catch
            {
                return true;
            }
        }

        private void CalculateOptimalRetreatDirection(IMyEntity attacker)
        {
            try
            {
                if (_knownThreats.Count == 0)
                {
                    if (attacker != null)
                    {
                        var toAttacker = attacker.GetPosition() - _startPosition;
                        _retreatDirection = toAttacker.LengthSquared() > 0 ? 
                            -Vector3D.Normalize(toAttacker) : 
                            Vector3D.Normalize(_startPosition - Vector3D.Zero);
                    }
                    else
                    {
                        _retreatDirection = Vector3D.Normalize(_startPosition - Vector3D.Zero);
                    }
                    return;
                }

                var threatCenter = _knownThreats
                    .Select(t => t.GetPosition())
                    .Aggregate(Vector3D.Zero, (sum, pos) => sum + pos) / _knownThreats.Count;

                var awayFromThreats = Vector3D.Normalize(_startPosition - threatCenter);

                if (_predictiveAnalyzer != null && _knownThreats.Any())
                {
                    var predictedThreatPositions = new List<Vector3D>();
                    
                    foreach (var threat in _knownThreats.Take(5)) // Limit to 5 most significant threats
                    {
                        var predicted = _predictiveAnalyzer.PredictEnemyPosition(threat, 10.0f); // 10 seconds ahead
                        predictedThreatPositions.Add(predicted);
                    }

                    if (predictedThreatPositions.Any())
                    {
                        var predictedThreatCenter = predictedThreatPositions
                            .Aggregate(Vector3D.Zero, (sum, pos) => sum + pos) / predictedThreatPositions.Count;
                        
                        var predictedRetreatDirection = Vector3D.Normalize(_startPosition - predictedThreatCenter);
                        _retreatDirection = Vector3D.Normalize(awayFromThreats * 0.6 + predictedRetreatDirection * 0.4);
                    }
                    else
                    {
                        _retreatDirection = awayFromThreats;
                    }
                }
                else
                {
                    _retreatDirection = awayFromThreats;
                }

                Logger.Debug($"[{Grid?.DisplayName}] Calculated optimal retreat direction away from {_knownThreats.Count} threats");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating optimal retreat direction");
                _retreatDirection = Vector3D.Normalize(_startPosition - Vector3D.Zero);
            }
        }

        private void PlanSafeRetreatRoute()
        {
            try
            {
                _retreatWaypoints.Clear();
                
                _safeRetreatPosition = _startPosition + _retreatDirection * _retreatDistance;
                
                var waypointCount = Math.Min((int)(_retreatDistance / 300), 5); // Waypoint every 300m, max 5
                
                for (var i = 1; i <= waypointCount; i++)
                {
                    var progress = (float)i / waypointCount;
                    var waypoint = _startPosition + _retreatDirection * (_retreatDistance * progress);
                    
                    var perpendicular = Vector3D.CalculatePerpendicularVector(_retreatDirection);
                    var randomOffset = perpendicular * ((new Random().NextDouble() - 0.5) * 100); // ±50m offset
                    
                    waypoint += randomOffset;
                    _retreatWaypoints.Add(waypoint);
                }

                ValidateAndAdjustWaypoints();
                
                _currentWaypointIndex = 0;
                
                Logger.Debug($"[{Grid?.DisplayName}] Planned retreat route with {_retreatWaypoints.Count} waypoints");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error planning safe retreat route");
                _safeRetreatPosition = _startPosition + _retreatDirection * _retreatDistance;
                _retreatWaypoints.Add(_safeRetreatPosition);
                _currentWaypointIndex = 0;
            }
        }

        private void ValidateAndAdjustWaypoints()
        {
            try
            {
                var validatedWaypoints = new List<Vector3D>();
                
                foreach (var waypoint in _retreatWaypoints)
                {
                    var adjustedWaypoint = CheckForObstacles(waypoint);
                    validatedWaypoints.Add(adjustedWaypoint);
                }
                
                _retreatWaypoints = validatedWaypoints;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error validating waypoints");
            }
        }

        private Vector3D CheckForObstacles(Vector3D waypoint)
        {
            try
            {
                var checkRadius = 200.0;
                var entities = new HashSet<IMyEntity>();
                
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    if (!(entity is IMyCubeGrid)) return false;
                    
                    var distance = Vector3D.Distance(waypoint, entity.GetPosition());
                    return distance <= checkRadius;
                });

                if (entities.Any())
                {
                    var obstacleCenter = entities
                        .Select(e => e.GetPosition())
                        .Aggregate(Vector3D.Zero, (sum, pos) => sum + pos) / entities.Count;
                    
                    var awayFromObstacle = Vector3D.Normalize(waypoint - obstacleCenter);
                    var adjustedWaypoint = waypoint + awayFromObstacle * 300; // Move 300m away from obstacle
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Adjusted waypoint to avoid obstacle");
                    return adjustedWaypoint;
                }
                
                return waypoint;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking waypoint for obstacles");
                return waypoint;
            }
        }

        private void RequestBackupDuringRetreat()
        {
            try
            {
                var commsManager = HeliosAIPlugin.Instance?.CommunicationManager;
                if (commsManager != null)
                {
                    commsManager.RegisterAgent(this);
                    commsManager.RequestBackup(this, _startPosition);
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Backup requested during retreat with threat data");
                }
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "RetreatBackupRequest", new Dictionary<string, object>
                {
                    ["Position"] = _startPosition,
                    ["ThreatCount"] = _knownThreats.Count,
                    ["RetreatDistance"] = _retreatDistance
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error requesting backup during retreat");
            }
        }

        private void PerformIntelligentRetreat()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                
                if (_lastThreatUpdate == DateTime.MinValue || 
                    currentTime.Subtract(_lastThreatUpdate).TotalSeconds > 3)
                {
                    UpdateThreatTracking();
                    _lastThreatUpdate = currentTime;
                }

                if (ShouldPerformEvasiveManeuvers())
                {
                    PerformEvasiveManeuvers();
                    return;
                }

                ExecuteWaypointRetreat();
                
                CheckRetreatCompletion();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent retreat execution");
            }
        }

        private void UpdateThreatTracking()
        {
            try
            {
                var activeThreats = new List<IMyEntity>();
                
                foreach (var threat in _knownThreats.ToList())
                {
                    if (threat != null && !threat.MarkedForClose)
                    {
                        _predictiveAnalyzer?.UpdateMovementHistory(threat);
                        activeThreats.Add(threat);
                        
                        var threatPos = threat.GetPosition();
                        var myPos = Grid.GetPosition();
                        var distance = Vector3D.Distance(threatPos, myPos);
                        
                        if (distance > 3000) // Threat is far away
                        {
                            Logger.Debug($"[{Grid?.DisplayName}] Threat {threat.DisplayName} is now distant ({distance:F0}m)");
                        }
                    }
                }
                
                _knownThreats = activeThreats;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating threat tracking");
            }
        }

        private bool ShouldPerformEvasiveManeuvers()
        {
            try
            {
                if (_knownThreats.Count == 0) return false;
                
                var myPos = Grid.GetPosition();
                var myVelocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                
                foreach (var threat in _knownThreats.Take(3)) // Check top 3 threats
                {
                    var threatPos = threat.GetPosition();
                    var distance = Vector3D.Distance(myPos, threatPos);
                    
                    if (distance < 800) // Within 800m - evasive action needed
                    {
                        Logger.Debug($"[{Grid?.DisplayName}] Threat {threat.DisplayName} within evasive range: {distance:F0}m");
                        return true;
                    }
                    
                    if (_predictiveAnalyzer != null)
                    {
                        var predictedThreatPos = _predictiveAnalyzer.PredictEnemyPosition(threat, 5.0f);
                        var myPredictedPos = myPos + myVelocity * 5.0f;
                        var interceptDistance = Vector3D.Distance(predictedThreatPos, myPredictedPos);
                        
                        if (interceptDistance < 600) // Predicted intercept
                        {
                            Logger.Debug($"[{Grid?.DisplayName}] Predicted intercept with {threat.DisplayName}, evasive action needed");
                            return true;
                        }
                    }
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking for evasive maneuvers");
                return false;
            }
        }

        private void PerformEvasiveManeuvers()
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                
                if (_lastEvasiveAction != DateTime.MinValue && 
                    currentTime.Subtract(_lastEvasiveAction).TotalSeconds < 5)
                {
                    return;
                }

                _evasiveManeuvers = true;
                _lastEvasiveAction = currentTime;
                
                var myPos = Grid.GetPosition();
                var currentTarget = _retreatWaypoints.Count > _currentWaypointIndex ? 
                    _retreatWaypoints[_currentWaypointIndex] : _safeRetreatPosition;
                
                var directionToTarget = Vector3D.Normalize(currentTarget - myPos);
                var perpendicular = Vector3D.CalculatePerpendicularVector(directionToTarget);
                
                var evasiveDirection = (currentTime.Millisecond % 2 == 0) ? perpendicular : -perpendicular;
                var evasivePosition = myPos + evasiveDirection * 300 + directionToTarget * 200;
                
                Logger.Info($"[{Grid?.DisplayName}] Performing evasive maneuvers");
                Npc?.MoveTo(evasivePosition);
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "EvasiveManeuver", new Dictionary<string, object>
                {
                    ["Position"] = myPos,
                    ["EvasivePosition"] = evasivePosition,
                    ["ThreatsNearby"] = _knownThreats.Count
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error performing evasive maneuvers");
            }
        }

        private void ExecuteWaypointRetreat()
        {
            try
            {
                if (_retreatWaypoints.Count == 0)
                {
                    Npc?.MoveTo(_safeRetreatPosition);
                    return;
                }

                if (_currentWaypointIndex >= _retreatWaypoints.Count)
                {
                    Npc?.MoveTo(_safeRetreatPosition);
                    return;
                }

                var currentWaypoint = _retreatWaypoints[_currentWaypointIndex];
                var myPos = Grid.GetPosition();
                var distance = Vector3D.Distance(myPos, currentWaypoint);

                if (distance < 100) // Reached waypoint
                {
                    _currentWaypointIndex++;
                    Logger.Debug($"[{Grid?.DisplayName}] Reached retreat waypoint {_currentWaypointIndex - 1}");
                    
                    if (_currentWaypointIndex < _retreatWaypoints.Count)
                    {
                        var nextWaypoint = _retreatWaypoints[_currentWaypointIndex];
                        Npc?.MoveTo(nextWaypoint);
                    }
                }
                else
                {
                    Npc?.MoveTo(currentWaypoint);
                }

                Logger.Debug($"[{Grid.DisplayName}] Retreating to waypoint {_currentWaypointIndex}/{_retreatWaypoints.Count} (distance: {distance:F0}m)");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error executing waypoint retreat");
            }
        }

        private void CheckRetreatCompletion()
        {
            try
            {
                var currentPosition = Grid.GetPosition();
                var distanceFromStart = Vector3D.Distance(currentPosition, _startPosition);
                
                if (distanceFromStart > _retreatDistance * 0.8)
                {
                    _retreatComplete = true;
                    var retreatDuration = (DateTime.UtcNow - _retreatStartTime).TotalSeconds;
                    
                    Logger.Info($"[{Grid.DisplayName}] Retreat complete after {retreatDuration:F1}s (distance: {distanceFromStart:F0}m)");
                    
                    // Record successful retreat
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "RetreatCompleted", new Dictionary<string, object>
                    {
                        ["Duration"] = retreatDuration,
                        ["FinalDistance"] = distanceFromStart,
                        ["WaypointsUsed"] = _retreatWaypoints.Count,
                        ["EvasiveActionsPerformed"] = _evasiveManeuvers ? 1 : 0,
                        ["ThreatsEvaded"] = _knownThreats.Count
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking retreat completion");
            }
        }

        private void HandleRetreatCompletion()
        {
            try
            {
                Logger.Debug($"[{Grid?.DisplayName}] Handling retreat completion");
                
                // Check if threats are still nearby before resuming normal behavior
                var nearbyThreats = _knownThreats.Count(t => 
                {
                    if (t == null || t.MarkedForClose) return false;
                    var distance = Vector3D.Distance(Grid.GetPosition(), t.GetPosition());
                    return distance < 1500; // 1.5km threat proximity
                });

                if (nearbyThreats > 0)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] {nearbyThreats} threats still nearby after retreat, extending retreat distance");
                    
                    _retreatDistance *= 1.5;
                    _retreatComplete = false;
                    PlanSafeRetreatRoute();
                }
                else
                {
                    if (Npc?.PatrolFallback != null)
                    {
                        Logger.Info($"[{Grid.DisplayName}] Resuming patrol behavior after successful retreat");
                        Npc.SetBehavior(Npc.PatrolFallback);
                    }
                    else
                    {
                        Logger.Debug($"[{Grid.DisplayName}] No patrol fallback, switching to idle after retreat");
                        Npc?.SetBehavior(new IdleBehavior(Grid));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling retreat completion");
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                if (_retreatWaypoints.Count > 0 && _currentWaypointIndex < _retreatWaypoints.Count)
                {
                    return _retreatWaypoints[_currentWaypointIndex];
                }
                
                return _safeRetreatPosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return _safeRetreatPosition;
            }
        }


        public void DebugDraw()
        {
            try
            {
                if (Grid == null) return;

                var material = VRage.Utils.MyStringId.GetOrCompute("WeaponLaser");
                
                Vector4 routeColor = Color.Yellow;
                var currentPos = Grid.GetPosition();
                
                if (_retreatWaypoints.Count > 0)
                {
                    var lastPos = currentPos;
                    for (var i = _currentWaypointIndex; i < _retreatWaypoints.Count; i++)
                    {
                        var waypoint = _retreatWaypoints[i];
                        MySimpleObjectDraw.DrawLine(lastPos, waypoint, material, ref routeColor, 0.05f, MyBillboard.BlendTypeEnum.PostPP);
                        lastPos = waypoint;
                    }
                    
                    MySimpleObjectDraw.DrawLine(lastPos, _safeRetreatPosition, material, ref routeColor, 0.05f, MyBillboard.BlendTypeEnum.PostPP);
                }
                else
                {
                    MySimpleObjectDraw.DrawLine(currentPos, _safeRetreatPosition, material, ref routeColor, 0.05f, MyBillboard.BlendTypeEnum.PostPP);
                }
                
                Vector4 threatColor = Color.Red;
                foreach (var threat in _knownThreats.Take(5))
                {
                    if (threat != null && !threat.MarkedForClose)
                    {
                        var threatPos = threat.GetPosition();
                        MySimpleObjectDraw.DrawLine(currentPos, threatPos, material, ref threatColor, 0.02f, MyBillboard.BlendTypeEnum.PostPP);
                    }
                }
                
                var retreatProgress = GetRetreatProgress();
                var statusText = $"Retreating: {retreatProgress:P0}\nThreats: {_knownThreats.Count}\nWaypoint: {_currentWaypointIndex + 1}/{_retreatWaypoints.Count}";
                
                MyRenderProxy.DebugDrawText3D(currentPos, statusText, Color.Orange, 0.8f, false);
                
                MyRenderProxy.DebugDrawText3D(_safeRetreatPosition, "Safe Position", Color.Green, 1.0f, false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in DebugDraw");
            }
        }

        public void SetRetreatDistance(double distance)
        {
            try
            {
                if (distance > 0)
                {
                    _retreatDistance = distance;
                    
                    PlanSafeRetreatRoute();
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Retreat distance updated to: {distance}m");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid retreat distance: {distance}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting retreat distance");
            }
        }

        public double GetRetreatProgress()
        {
            try
            {
                if (Grid == null) return 0;

                var currentDistance = Vector3D.Distance(Grid.GetPosition(), _startPosition);
                return Math.Min(currentDistance / _retreatDistance, 1.0);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating retreat progress");
                return 0;
            }
        }

        public bool IsRetreatComplete()
        {
            return _retreatComplete;
        }

        public Vector3D GetRetreatTarget()
        {
            return _safeRetreatPosition;
        }

        public int GetThreatsDetected()
        {
            return _knownThreats.Count;
        }

        public bool IsPerformingEvasiveManeuvers()
        {
            return _evasiveManeuvers;
        }

        public double GetRetreatDuration()
        {
            return (DateTime.UtcNow - _retreatStartTime).TotalSeconds;
        }

        public List<Vector3D> GetRetreatWaypoints()
        {
            return new List<Vector3D>(_retreatWaypoints);
        }

        public int GetCurrentWaypointIndex()
        {
            return _currentWaypointIndex;
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "RetreatBehaviorDisposed", new Dictionary<string, object>
                    {
                        ["RetreatCompleted"] = _retreatComplete,
                        ["FinalProgress"] = GetRetreatProgress(),
                        ["TotalDuration"] = GetRetreatDuration(),
                        ["ThreatsEvaded"] = _knownThreats.Count,
                        ["EvasiveManeuversUsed"] = _evasiveManeuvers
                    });
                }
                
                var commsManager = HeliosAIPlugin.Instance?.CommunicationManager;
                if (commsManager != null)
                {
                    commsManager.UnregisterAgent(this);
                }

                _knownThreats?.Clear();
                _retreatWaypoints?.Clear();
                
                Logger.Debug($"[{Grid?.DisplayName}] RetreatBehavior disposed after {GetRetreatDuration():F1}s");
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing RetreatBehavior");
            }
        }
    }
}