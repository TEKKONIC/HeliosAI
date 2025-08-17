using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using System;
using Helios.Modules.API;
using NLog;
using VRage.ModAPI;

namespace HeliosAI.Behaviors
{
    public class DefenseBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("DefenseBehavior");
        
        private readonly Vector3D _defensePosition;
        private readonly double _radius;
        private readonly List<(Vector3D Position, double Radius)> _defensePoints = new();
        private DateTime _lastThreatAssessment = DateTime.MinValue;
        private Dictionary<long, Vector3D> _knownThreats = new Dictionary<long, Vector3D>();
        private Vector3D _optimalDefensePosition;
        private DateTime _lastPositionOptimization = DateTime.MinValue;
        private bool _isPatrolling = false;
        private List<Vector3D> _patrolRoute = new List<Vector3D>();
        private int _currentPatrolIndex = 0;

        public Vector3D DefensePosition => _defensePosition;
        public List<(Vector3D Position, double Radius)> DefensePoints => _defensePoints.ToList();
        public double DefenseRadius => _radius;

        public override string Name => "Defense";

        public DefenseBehavior(IMyCubeGrid grid, Vector3D defensePosition, double radius = 1000) : base(grid)
        {
            _defensePosition = defensePosition;
            _radius = radius;
            _optimalDefensePosition = defensePosition;
            
            // Initialize patrol route around defense position
            GeneratePatrolRoute();
            
            Logger.Info($"[{Grid?.DisplayName}] DefenseBehavior initialized at {defensePosition} with radius {radius}m");
        }

        protected override void OnTick()
        {
            try
            {
                if (Grid == null || Grid.MarkedForClose || Grid.Physics == null)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping defense");
                    return;
                }

                var currentPosition = Grid.GetPosition();
                var currentTime = DateTime.UtcNow;

                // Enhanced threat assessment every 5 seconds
                if (_lastThreatAssessment == DateTime.MinValue || 
                    currentTime.Subtract(_lastThreatAssessment).TotalSeconds > 5)
                {
                    PerformIntelligentThreatAssessment();
                    _lastThreatAssessment = currentTime;
                }

                // Optimize defense position every 30 seconds
                if (_lastPositionOptimization == DateTime.MinValue || 
                    currentTime.Subtract(_lastPositionOptimization).TotalSeconds > 30)
                {
                    OptimizeDefensePosition();
                    _lastPositionOptimization = currentTime;
                }

                // Check for immediate threats
                var immediateThreats = DetectImmediateThreats();
                if (immediateThreats.Any())
                {
                    HandleImmediateThreats(immediateThreats);
                    return;
                }

                // Check primary defense position
                var primaryTarget = CheckDefensePosition(_defensePosition, _radius);
                if (primaryTarget != null)
                {
                    EngageTarget(primaryTarget, "Primary Defense Zone");
                    return;
                }

                var defenseTargets = CheckAllDefensePoints();
                if (defenseTargets.Any())
                {
                    var priorityTarget = SelectPriorityDefenseTarget(defenseTargets);
                    EngageTarget(priorityTarget.target, $"Defense Point {priorityTarget.point}");
                    return;
                }

                // No threats detected - perform intelligent defense posture
                MaintainIntelligentDefense(currentPosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in DefenseBehavior.OnTick()");
            }
        }

        private void PerformIntelligentThreatAssessment()
        {
            try
            {
                // Scan for potential threats beyond immediate detection range
                var extendedThreats = ScanForThreats(_defensePosition, _radius * 2.0);
                
                foreach (var threat in extendedThreats)
                {
                    var threatId = threat.EntityId;
                    var threatPos = threat.GetPosition();
                    
                    _knownThreats[threatId] = threatPos;
                    
                    _predictiveAnalyzer?.UpdateMovementHistory(threat);
                    
                    // Predict if threat is heading towards defense zone
                    if (_predictiveAnalyzer != null)
                    {
                        var predictedPos = _predictiveAnalyzer.PredictEnemyPosition(threat, 10.0f); // 10 seconds ahead
                        var willEnterDefenseZone = Vector3D.Distance(predictedPos, _defensePosition) < _radius;
                        
                        if (willEnterDefenseZone)
                        {
                            Logger.Warn($"[{Grid?.DisplayName}] Threat {threat.DisplayName} predicted to enter defense zone");
                            
                            // Pre-position for optimal interception
                            var interceptPosition = CalculateInterceptPosition(threat, predictedPos);
                            if (interceptPosition.HasValue)
                            {
                                _optimalDefensePosition = interceptPosition.Value;
                            }
                        }
                    }
                }

                // Clean up old threat data
                var threatsToRemove = _knownThreats.Where(kvp => 
                {
                    var entity = MyAPIGateway.Entities.GetEntityById(kvp.Key);
                    return entity == null || entity.MarkedForClose;
                }).Select(kvp => kvp.Key).ToList();

                foreach (var threatId in threatsToRemove)
                {
                    _knownThreats.Remove(threatId);
                }

                // Record threat assessment
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "ThreatAssessment", new Dictionary<string, object>
                {
                    ["KnownThreats"] = _knownThreats.Count,
                    ["DefensePosition"] = _defensePosition,
                    ["OptimalPosition"] = _optimalDefensePosition
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent threat assessment");
            }
        }

        private List<IMyEntity> ScanForThreats(Vector3D center, double scanRadius)
        {
            var threats = new List<IMyEntity>();
            
            try
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(center, entity.GetPosition());
                    return distance <= scanRadius;
                });

                foreach (var entity in entities)
                {
                    if (IsHostileEntity(entity))
                    {
                        threats.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error scanning for threats");
            }

            return threats;
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
                if (myOwner == 0) return true; // No owner = hostile
                
                var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                var myFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(myOwner);
                
                if (playerFaction == null || myFaction == null) return true; // Unknown = hostile
                
                return playerFaction.FactionId != myFaction.FactionId;
            }
            catch
            {
                return true; // Error = assume hostile
            }
        }

        private Vector3D? CalculateInterceptPosition(IMyEntity threat, Vector3D predictedPosition)
        {
            try
            {
                var threatDirection = Vector3D.Normalize(predictedPosition - _defensePosition);
                var interceptDistance = Math.Min(_radius * 0.8, 800); // 80% of defense radius or 800m max
                
                return _defensePosition - threatDirection * interceptDistance;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating intercept position");
                return null;
            }
        }

        private void OptimizeDefensePosition()
        {
            try
            {
                if (_knownThreats.Count == 0)
                {
                    _optimalDefensePosition = _defensePosition;
                    return;
                }

                var threatCenter = _knownThreats.Values.Aggregate(Vector3D.Zero, (sum, pos) => sum + pos) / _knownThreats.Count;
                
                var defenseDirection = Vector3D.Normalize(_defensePosition - threatCenter);
                var optimalDistance = Math.Min(_radius * 0.6, 600); // 60% of radius or 600m max
                
                _optimalDefensePosition = _defensePosition + defenseDirection * optimalDistance;
                
                Logger.Debug($"[{Grid?.DisplayName}] Optimized defense position: {_optimalDefensePosition}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error optimizing defense position");
            }
        }

        private List<IMyEntity> DetectImmediateThreats()
        {
            var threats = new List<IMyEntity>();
            
            try
            {
                // Scan close range for immediate threats
                var closeRange = Math.Min(_radius * 0.5, 500); // Half defense radius or 500m
                threats = ScanForThreats(Grid.GetPosition(), closeRange);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error detecting immediate threats");
            }

            return threats;
        }

        private void HandleImmediateThreats(List<IMyEntity> threats)
        {
            try
            {
                // Select closest threat as priority
                var myPos = Grid.GetPosition();
                var closestThreat = threats
                    .OrderBy(t => Vector3D.Distance(myPos, t.GetPosition()))
                    .FirstOrDefault();

                if (closestThreat != null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Immediate threat detected: {closestThreat.DisplayName}");
                    EngageTarget(closestThreat, "Immediate Threat");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling immediate threats");
            }
        }

        private IMyEntity CheckDefensePosition(Vector3D position, double radius)
        {
            try
            {
                var wc = APIManager.WeaponCoreManager;
                if (wc != null)
                {
                    wc.RegisterWeapons(Grid);
                    return wc.GetPriorityTarget(position, radius);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking defense position {position}");
            }
            
            return null;
        }

        private List<(IMyEntity target, Vector3D point)> CheckAllDefensePoints()
        {
            var results = new List<(IMyEntity target, Vector3D point)>();
            
            try
            {
                foreach (var (pos, rad) in _defensePoints.ToList())
                {
                    var target = CheckDefensePosition(pos, rad);
                    if (target != null)
                    {
                        results.Add((target, pos));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking defense points");
            }

            return results;
        }

        private (IMyEntity target, Vector3D point) SelectPriorityDefenseTarget(List<(IMyEntity target, Vector3D point)> targets)
        {
            try
            {
                // Priority selection based on distance to primary defense position
                return targets
                    .OrderBy(t => Vector3D.Distance(t.point, _defensePosition))
                    .First();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error selecting priority defense target");
                return targets.FirstOrDefault();
            }
        }

        private void EngageTarget(IMyEntity target, string reason)
        {
            try
            {
                Logger.Info($"[{Grid?.DisplayName}] Engaging hostile in {reason}: {target.DisplayName}");
                
                // Record engagement decision
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "DefenseEngagement", new Dictionary<string, object>
                {
                    ["TargetId"] = target.EntityId,
                    ["EngagementReason"] = reason,
                    ["DefensePosition"] = _defensePosition,
                    ["TargetDistance"] = Vector3D.Distance(Grid.GetPosition(), target.GetPosition())
                });
                
                // Switch to attack behavior with enhanced targeting
                if (Npc != null)
                {
                    var attackBehavior = new AttackBehavior(Grid, target);
                    Npc.SetBehavior(attackBehavior);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error engaging target");
            }
        }

        private void MaintainIntelligentDefense(Vector3D currentPosition)
        {
            try
            {
                var targetPosition = _optimalDefensePosition;
                var distance = Vector3D.Distance(currentPosition, targetPosition);

                if (distance > 100) // Allow some leeway
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Moving to optimal defense position (distance: {distance:F0}m)");
                    Npc?.MoveTo(targetPosition);
                    _isPatrolling = false;
                }
                else
                {
                    // We're at optimal position - consider patrolling if no threats
                    if (_knownThreats.Count == 0 && !_isPatrolling)
                    {
                        StartDefensePatrol();
                    }
                    else if (_isPatrolling)
                    {
                        ContinueDefensePatrol();
                    }
                    else
                    {
                        // Hold position and disable autopilot
                        StopMovement();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error maintaining intelligent defense");
            }
        }

        private void GeneratePatrolRoute()
        {
            try
            {
                _patrolRoute.Clear();
                
                // Create patrol points around defense position
                var patrolRadius = Math.Min(_radius * 0.4, 400); // 40% of defense radius
                var numPoints = 6; // Hexagonal patrol pattern
                
                for (var i = 0; i < numPoints; i++)
                {
                    var angle = (2 * Math.PI * i) / numPoints;
                    var offset = new Vector3D(
                        Math.Cos(angle) * patrolRadius,
                        0,
                        Math.Sin(angle) * patrolRadius
                    );
                    
                    _patrolRoute.Add(_defensePosition + offset);
                }
                
                Logger.Debug($"[{Grid?.DisplayName}] Generated {numPoints} patrol points around defense position");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error generating patrol route");
            }
        }

        private void StartDefensePatrol()
        {
            try
            {
                if (_patrolRoute.Count == 0) return;
                
                _isPatrolling = true;
                _currentPatrolIndex = 0;
                
                Logger.Debug($"[{Grid?.DisplayName}] Starting defense patrol");
                Npc?.MoveTo(_patrolRoute[_currentPatrolIndex]);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error starting defense patrol");
            }
        }

        private void ContinueDefensePatrol()
        {
            try
            {
                if (_patrolRoute.Count == 0) return;
                
                var currentPos = Grid.GetPosition();
                var targetPos = _patrolRoute[_currentPatrolIndex];
                var distance = Vector3D.Distance(currentPos, targetPos);
                
                if (distance < 50) // Reached patrol point
                {
                    _currentPatrolIndex = (_currentPatrolIndex + 1) % _patrolRoute.Count;
                    var nextPos = _patrolRoute[_currentPatrolIndex];
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Moving to next patrol point {_currentPatrolIndex}");
                    Npc?.MoveTo(nextPos);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error continuing defense patrol");
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
                    Logger.Debug($"[{Grid?.DisplayName}] Holding optimal defense position");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error stopping movement");
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                if (_isPatrolling && _patrolRoute.Count > 0)
                {
                    return _patrolRoute[_currentPatrolIndex];
                }
                
                return _optimalDefensePosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return _defensePosition;
            }
        }

        protected override void OnBackupRequested(Vector3D location, string message)
        {
            try
            {
                var distanceToRequest = Vector3D.Distance(_defensePosition, location);
                
                if (distanceToRequest <= _radius * 1.5) // Allow 50% extra range for backup
                {
                    Logger.Info($"[{Grid?.DisplayName}] Responding to backup request at {location}");
                    
                    // Temporarily expand defense to include backup location
                    AddDefensePoint(location, 500);
                    
                    // Move towards backup location if no immediate threats
                    if (_knownThreats.Count == 0)
                    {
                        _optimalDefensePosition = location;
                    }
                }
                else
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Backup request too far from defense position: {distanceToRequest:F0}m");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing backup request");
            }
        }

        
        public void AddDefensePoint(Vector3D position, double pointRadius = 1000)
        {
            try
            {
                if (pointRadius <= 0)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid defense point radius: {pointRadius}");
                    return;
                }

                _defensePoints.Add((position, pointRadius));
                Logger.Info($"[{Grid?.DisplayName}] Added defense point at {position} with radius {pointRadius}m");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "DefensePointAdded", new Dictionary<string, object>
                {
                    ["Position"] = position,
                    ["Radius"] = pointRadius,
                    ["TotalPoints"] = _defensePoints.Count
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error adding defense point");
            }
        }

        public void RemoveDefensePoint(Vector3D position)
        {
            try
            {
                var toRemove = _defensePoints
                    .Where(dp => Vector3D.Distance(dp.Position, position) < 10)
                    .ToList();

                foreach (var point in toRemove)
                {
                    _defensePoints.Remove(point);
                    Logger.Info($"[{Grid?.DisplayName}] Removed defense point at {point.Position}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error removing defense point");
            }
        }

        public void ClearDefensePoints()
        {
            try
            {
                var count = _defensePoints.Count;
                _defensePoints.Clear();
                Logger.Info($"[{Grid?.DisplayName}] Cleared {count} defense points");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error clearing defense points");
            }
        }

        public bool IsInDefenseRadius(Vector3D position)
        {
            try
            {
                var distance = Vector3D.Distance(position, _defensePosition);
                return distance <= _radius;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error checking defense radius");
                return false;
            }
        }

        public double GetDistanceFromDefensePosition()
        {
            try
            {
                if (Grid == null) return double.MaxValue;
                return Vector3D.Distance(Grid.GetPosition(), _defensePosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating distance from defense position");
                return double.MaxValue;
            }
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "DefenseBehaviorCompleted", new Dictionary<string, object>
                    {
                        ["DefensePosition"] = _defensePosition,
                        ["DefenseRadius"] = _radius,
                        ["FinalOptimalPosition"] = _optimalDefensePosition,
                        ["DefensePointsCount"] = _defensePoints.Count,
                        ["ThreatsTracked"] = _knownThreats.Count,
                        ["WasPatrolling"] = _isPatrolling
                    });
                }
                
                _defensePoints.Clear();
                _knownThreats.Clear();
                _patrolRoute.Clear();
                
                Logger.Debug($"[{Grid?.DisplayName}] DefenseBehavior disposed");
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error disposing DefenseBehavior");
            }
        }
    }
}