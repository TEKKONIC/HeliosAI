using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRageMath;
using NLog;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace HeliosAI.Behaviors
{
    public class PatrolBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("PatrolBehavior");
        private readonly List<Vector3D> _waypoints;
        private int _currentIndex = 0;
        private double _waypointTolerance = 50.0;
        private DateTime _lastThreatScan = DateTime.MinValue;
        private DateTime _lastWaypointReached = DateTime.MinValue;
        private Dictionary<int, DateTime> _waypointTimings = new Dictionary<int, DateTime>();
        private List<IMyEntity> _detectedThreats = new List<IMyEntity>();
        private Vector3D _lastScanPosition = Vector3D.Zero;
        private bool _isPaused = false;
        private DateTime _pauseStartTime = DateTime.MinValue;
        private Dictionary<int, List<IMyEntity>> _waypointThreatHistory = new Dictionary<int, List<IMyEntity>>();
        private float _patrolSpeed = 50f; // m/s
        public bool IsReversing { get; } = false;

        public override string Name => "Patrol";

        public PatrolBehavior(IMyCubeGrid grid, List<Vector3D> waypoints) : base(grid)
        {
            _waypoints = waypoints ?? new List<Vector3D>();
            InitializePatrolData();
            
            Logger.Info($"[{Grid?.DisplayName}] PatrolBehavior initialized with {_waypoints.Count} waypoints");
        }

        protected override void OnTick()
        {
            try
            {
                base.OnTick();

                if (Grid?.Physics == null || Grid.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping patrol");
                    return;
                }

                if (_waypoints == null || _waypoints.Count == 0)
                {
                    Logger.Warn($"[{Grid.DisplayName}] No waypoints defined for patrol");
                    return;
                }

                PerformIntelligentPatrol();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in PatrolBehavior.OnTick()");
            }
        }

        private void InitializePatrolData()
        {
            try
            {
                for (var i = 0; i < _waypoints.Count; i++)
                {
                    _waypointTimings[i] = DateTime.MinValue;
                    _waypointThreatHistory[i] = new List<IMyEntity>();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error initializing patrol data");
            }
        }

        private void PerformIntelligentPatrol()
        {
            try
            {
                var currentTime = DateTime.UtcNow;

                // 1. Perform threat scanning while patrolling
                if (_lastThreatScan == DateTime.MinValue || 
                    currentTime.Subtract(_lastThreatScan).TotalSeconds > 5)
                {
                    PerformPatrolScan(currentTime);
                    _lastThreatScan = currentTime;
                }

                // 2. Handle any detected threats
                if (_detectedThreats.Any())
                {
                    HandleThreatsDuringPatrol();
                    return; // Threat handling takes priority
                }

                // 3. Check if paused and handle pause logic
                if (_isPaused)
                {
                    HandlePatrolPause(currentTime);
                    return;
                }

                // 4. Execute intelligent waypoint navigation
                ExecuteIntelligentNavigation(currentTime);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent patrol");
            }
        }

        private void PerformPatrolScan(DateTime currentTime)
        {
            try
            {
                var myPos = Grid.GetPosition();
                var scanRadius = 1500.0; // 1.5km patrol scan
                
                var movementDistance = Vector3D.Distance(myPos, _lastScanPosition);
                if (movementDistance < 100 && _lastScanPosition != Vector3D.Zero)
                {
                    return;
                }

                _lastScanPosition = myPos;

                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(myPos, entity.GetPosition());
                    return distance <= scanRadius;
                });

                _detectedThreats.Clear();

                foreach (var entity in entities)
                {

                    _predictiveAnalyzer?.UpdateMovementHistory(entity);

                    if (IsHostileEntity(entity))
                    {
                        _detectedThreats.Add(entity);
                        

                        if (_currentIndex >= 0 && _currentIndex < _waypointThreatHistory.Count)
                        {
                            _waypointThreatHistory[_currentIndex].Add(entity);
                        }
                    }
                }
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "PatrolScan", new Dictionary<string, object>
                {
                    ["CurrentWaypoint"] = _currentIndex,
                    ["ThreatsDetected"] = _detectedThreats.Count,
                    ["EntitiesScanned"] = entities.Count,
                    ["ScanPosition"] = myPos,
                    ["ScanRadius"] = scanRadius
                });

                if (_detectedThreats.Any())
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Patrol scan detected {_detectedThreats.Count} threats at waypoint {_currentIndex}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in patrol scanning");
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

        private void HandleThreatsDuringPatrol()
        {
            try
            {
                var highPriorityThreats = _detectedThreats.Where(t => IsHighPriorityThreat(t)).ToList();
                
                if (highPriorityThreats.Any())
                {

                    var closestThreat = highPriorityThreats
                        .OrderBy(t => Vector3D.Distance(Grid.GetPosition(), t.GetPosition()))
                        .First();

                    Logger.Info($"[{Grid?.DisplayName}] Patrol engaging high priority threat: {closestThreat.DisplayName}");
                    

                    if (Npc != null)
                    {
                        var attackBehavior = new AttackBehavior(Grid, closestThreat);
                        Npc.SetBehavior(attackBehavior);
                    }
                }
                else if (_detectedThreats.Count > 3)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Multiple threats detected during patrol, considering tactical options");
                    
                    var shouldRetreat = ShouldRetreat(0.8f, 1.2f); // Conservative threat assessment
                    
                    if (shouldRetreat)
                    {
                        var safeWaypoint = FindSafestWaypoint();
                        if (safeWaypoint.HasValue)
                        {
                            Logger.Info($"[{Grid?.DisplayName}] Retreating to safe waypoint during patrol");
                            _currentIndex = safeWaypoint.Value;
                        }
                    }
                    else
                    {
                        RequestBackupAtCurrentPosition();
                    }
                }
                else
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Monitoring {_detectedThreats.Count} low-priority threats during patrol");
                    
                    _patrolSpeed = Math.Min(_patrolSpeed * 1.5f, 100f);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling threats during patrol");
            }
        }

        private bool IsHighPriorityThreat(IMyEntity threat)
        {
            try
            {
                if (threat is IMyCharacter)
                {
                    return true; // Players are always high priority
                }
                else if (threat is IMyCubeGrid grid)
                {
                    var blocks = new List<IMySlimBlock>();
                    grid.GetBlocks(blocks);
                    
                    var hasWeapons = blocks.Any(b => IsWeaponBlock(b));
                    var blockCount = blocks.Count;
                    
                    // Large armed grids are high priority
                    return hasWeapons && blockCount > 50;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        private new bool IsWeaponBlock(IMySlimBlock block)
        {
            var subtype = block.BlockDefinition.Id.SubtypeName;
            return subtype.Contains("Gatling") || 
                   subtype.Contains("Missile") || 
                   subtype.Contains("Laser") || 
                   subtype.Contains("Railgun") ||
                   subtype.Contains("Turret");
        }

        private int? FindSafestWaypoint()
        {
            try
            {
                var myPos = Grid.GetPosition();
                var safeWaypoints = new List<(int index, double safety)>();
                
                for (var i = 0; i < _waypoints.Count; i++)
                {
                    var waypoint = _waypoints[i];
                    var threatHistory = _waypointThreatHistory.ContainsKey(i) ? _waypointThreatHistory[i] : new List<IMyEntity>();
                    
                    // Calculate safety score (lower threat history = safer)
                    var recentThreats = threatHistory.Count(t => !t.MarkedForClose);
                    var distanceFromCurrent = Vector3D.Distance(myPos, waypoint);
                    
                    // Prefer closer waypoints with fewer threats
                    var safetyScore = (1.0 / (recentThreats + 1)) * (2000.0 / Math.Max(distanceFromCurrent, 100));
                    
                    safeWaypoints.Add((i, safetyScore));
                }
                
                var safest = safeWaypoints.OrderByDescending(w => w.safety).FirstOrDefault();
                return safest.index;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding safest waypoint");
                return null;
            }
        }

        private void RequestBackupAtCurrentPosition()
        {
            try
            {
                var myPos = Grid.GetPosition();
                var message = $"Patrol unit {Grid.DisplayName} requests backup - multiple threats detected";
                
                // This would typically send to other AI units or alert systems
                Logger.Info($"[{Grid?.DisplayName}] Requesting backup at {myPos}");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "PatrolBackupRequest", new Dictionary<string, object>
                {
                    ["Position"] = myPos,
                    ["ThreatCount"] = _detectedThreats.Count,
                    ["CurrentWaypoint"] = _currentIndex
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error requesting backup");
            }
        }

        private void HandlePatrolPause(DateTime currentTime)
        {
            try
            {
                var pauseDuration = (currentTime - _pauseStartTime).TotalSeconds;
                
                if (pauseDuration > 30)
                {
                    _isPaused = false;
                    Logger.Debug($"[{Grid?.DisplayName}] Resuming patrol after {pauseDuration:F1}s pause");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling patrol pause");
            }
        }

        private void ExecuteIntelligentNavigation(DateTime currentTime)
        {
            try
            {
                var currentTarget = _waypoints[_currentIndex];
                var gridPosition = Grid.GetPosition();
                var distance = Vector3D.Distance(gridPosition, currentTarget);

                if (distance > _waypointTolerance)
                {
                    var targetPosition = currentTarget;
                    
                    if (_predictiveAnalyzer != null)
                    {
                        var estimatedTravelTime = distance / _patrolSpeed;
                        
                        var pathClear = ValidatePatrolPath(gridPosition, targetPosition);
                        if (!pathClear)
                        {
                            targetPosition = CalculateAlternativeRoute(gridPosition, targetPosition);
                        }
                    }

                    Logger.Debug($"[{Grid.DisplayName}] Patrolling to waypoint {_currentIndex}: {targetPosition} (distance: {distance:F1}m)");
                    Npc?.MoveTo(targetPosition);
                    
                    AdjustPatrolSpeed();
                }
                else
                {
                    // Reached waypoint
                    OnWaypointReached(currentTime);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent navigation");
            }
        }

        private bool ValidatePatrolPath(Vector3D start, Vector3D end)
        {
            try
            {
                var direction = Vector3D.Normalize(end - start);
                var checkDistance = Vector3D.Distance(start, end);
                var checkPoints = Math.Min((int)(checkDistance / 200), 10); // Check every 200m, max 10 points
                
                for (var i = 1; i <= checkPoints; i++)
                {
                    var checkPos = start + direction * (checkDistance * i / checkPoints);
                    
                    var entities = new HashSet<IMyEntity>();
                    MyAPIGateway.Entities.GetEntities(entities, entity =>
                    {
                        if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                        if (!(entity is IMyCubeGrid grid)) return false;
                        
                        var distance = Vector3D.Distance(checkPos, entity.GetPosition());
                        return distance < 150; // 150m clearance needed
                    });

                    if (entities.Any())
                    {
                        Logger.Debug($"[{Grid?.DisplayName}] Obstacle detected on patrol path");
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error validating patrol path");
                return true; // Assume clear on error
            }
        }

        private Vector3D CalculateAlternativeRoute(Vector3D start, Vector3D end)
        {
            try
            {
                var direction = Vector3D.Normalize(end - start);
                var perpendicular = Vector3D.CalculatePerpendicularVector(direction);
                
                var offset = perpendicular * 300; // 300m offset
                
                if (_currentIndex % 2 == 1) offset = -offset;
                
                var alternativeTarget = end + offset;
                
                Logger.Debug($"[{Grid?.DisplayName}] Using alternative route to avoid obstacles");
                return alternativeTarget;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating alternative route");
                return end; // Fallback to original target
            }
        }

        private void AdjustPatrolSpeed()
        {
            try
            {
                var baseSpeed = 50f;
                var currentThreatLevel = _detectedThreats.Count;
                
                if (currentThreatLevel > 0)
                {
                    // Speed up in threat areas
                    _patrolSpeed = Math.Min(baseSpeed * 1.5f, 100f);
                }
                else
                {
                    // Normal speed in clear areas
                    _patrolSpeed = baseSpeed;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error adjusting patrol speed");
            }
        }

        private void OnWaypointReached(DateTime currentTime)
        {
            try
            {
                _waypointTimings[_currentIndex] = currentTime;
                _lastWaypointReached = currentTime;
                
                var previousIndex = _currentIndex;
                _currentIndex = (_currentIndex + 1) % _waypoints.Count;
                
                Logger.Debug($"[{Grid.DisplayName}] Reached waypoint {previousIndex}, moving to next: {_currentIndex}");

                PauseAtWaypoint();

                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "WaypointReached", new Dictionary<string, object>
                {
                    ["WaypointIndex"] = previousIndex,
                    ["Position"] = _waypoints[previousIndex],
                    ["ThreatsDetected"] = _detectedThreats.Count,
                    ["TravelTime"] = CalculateTravelTimeToWaypoint(previousIndex)
                });

                // Stop autopilot briefly before heading to next waypoint
                StopAutopilotAtWaypoint();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error handling waypoint reached");
            }
        }

        private double CalculateTravelTimeToWaypoint(int waypointIndex)
        {
            try
            {
                if (_waypointTimings.ContainsKey(waypointIndex) && _waypointTimings[waypointIndex] != DateTime.MinValue)
                {
                    var startTime = _lastWaypointReached != DateTime.MinValue ? _lastWaypointReached : _behaviorStartTime;
                    return (_waypointTimings[waypointIndex] - startTime).TotalSeconds;
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        private void PauseAtWaypoint()
        {
            try
            {
                _isPaused = true;
                _pauseStartTime = DateTime.UtcNow;
                
                PerformWaypointScan();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error pausing at waypoint");
            }
        }

        private void PerformWaypointScan()
        {
            try
            {
                var myPos = Grid.GetPosition();
                var extendedScanRadius = 2000.0; // Extended scan at waypoints
                
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(myPos, entity.GetPosition());
                    return distance <= extendedScanRadius;
                });

                var threatsFound = entities.Where(IsHostileEntity).ToList();
                var friendliesFound = entities.Where(e => !IsHostileEntity(e) && (e is IMyCharacter || e is IMyCubeGrid)).ToList();
                
                Logger.Debug($"[{Grid?.DisplayName}] Waypoint scan: {threatsFound.Count} threats, {friendliesFound.Count} friendlies");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "WaypointScan", new Dictionary<string, object>
                {
                    ["WaypointIndex"] = _currentIndex,
                    ["Threats"] = threatsFound.Count,
                    ["Friendlies"] = friendliesFound.Count,
                    ["ScanRadius"] = extendedScanRadius
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in waypoint scanning");
            }
        }

        private void StopAutopilotAtWaypoint()
        {
            try
            {
                var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                    .FirstOrDefault(r => r?.IsFunctional == true);

                if (remote != null)
                {
                    remote.SetAutoPilotEnabled(false);
                    Logger.Debug($"[{Grid.DisplayName}] Stopping at waypoint {_currentIndex - 1}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Error stopping autopilot at waypoint");
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                return GetCurrentWaypoint();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return Vector3D.Zero;
            }
        }

        protected override void OnBackupRequested(Vector3D location, string message)
        {
            try
            {
                var closestWaypoint = _waypoints
                    .Select((wp, index) => new { waypoint = wp, index, distance = Vector3D.Distance(wp, location) })
                    .OrderBy(x => x.distance)
                    .FirstOrDefault();

                if (closestWaypoint != null && closestWaypoint.distance <= 1000) // Within 1km of patrol route
                {
                    Logger.Info($"[{Grid?.DisplayName}] Patrol responding to backup request near waypoint {closestWaypoint.index}");
                    
                    _waypoints.Insert(closestWaypoint.index + 1, location);
                    
                    if (_currentIndex <= closestWaypoint.index)
                    {
                        _currentIndex = closestWaypoint.index + 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing backup request during patrol");
            }
        }


        public void AddWaypoint(Vector3D waypoint)
        {
            try
            {
                _waypoints.Add(waypoint);
                var newIndex = _waypoints.Count - 1;
                _waypointTimings[newIndex] = DateTime.MinValue;
                _waypointThreatHistory[newIndex] = new List<IMyEntity>();
                
                Logger.Info($"[{Grid?.DisplayName}] Added waypoint: {waypoint} (total: {_waypoints.Count})");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "WaypointAdded", new Dictionary<string, object>
                {
                    ["Position"] = waypoint,
                    ["TotalWaypoints"] = _waypoints.Count
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error adding waypoint");
            }
        }

        public void RemoveWaypoint(int index)
        {
            try
            {
                if (index >= 0 && index < _waypoints.Count)
                {
                    var waypoint = _waypoints[index];
                    _waypoints.RemoveAt(index);
                    _waypointTimings.Remove(index);
                    _waypointThreatHistory.Remove(index);
                    
                    if (_currentIndex >= _waypoints.Count && _waypoints.Count > 0)
                    {
                        _currentIndex = 0;
                    }
                    
                    Logger.Info($"[{Grid?.DisplayName}] Removed waypoint {index}: {waypoint}");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid waypoint index: {index}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error removing waypoint");
            }
        }

        public void ClearWaypoints()
        {
            try
            {
                var count = _waypoints.Count;
                _waypoints.Clear();
                _waypointTimings.Clear();
                _waypointThreatHistory.Clear();
                _currentIndex = 0;
                
                Logger.Info($"[{Grid?.DisplayName}] Cleared {count} waypoints");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error clearing waypoints");
            }
        }

        public void SetWaypointTolerance(double tolerance)
        {
            try
            {
                if (tolerance > 0)
                {
                    _waypointTolerance = tolerance;
                    Logger.Debug($"[{Grid?.DisplayName}] Waypoint tolerance set to: {tolerance}m");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid waypoint tolerance: {tolerance}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting waypoint tolerance");
            }
        }

        public Vector3D GetCurrentWaypoint()
        {
            try
            {
                if (_waypoints.Count > 0 && _currentIndex >= 0 && _currentIndex < _waypoints.Count)
                {
                    return _waypoints[_currentIndex];
                }
                return Vector3D.Zero;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error getting current waypoint");
                return Vector3D.Zero;
            }
        }

        public int GetCurrentWaypointIndex()
        {
            return _currentIndex;
        }

        public int GetWaypointCount()
        {
            return _waypoints?.Count ?? 0;
        }

        public double GetDistanceToCurrentWaypoint()
        {
            try
            {
                if (Grid == null || _waypoints.Count == 0)
                    return double.MaxValue;

                var currentWaypoint = GetCurrentWaypoint();
                return Vector3D.Distance(Grid.GetPosition(), currentWaypoint);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating distance to waypoint");
                return double.MaxValue;
            }
        }

        public void GoToWaypoint(int index)
        {
            try
            {
                if (index >= 0 && index < _waypoints.Count)
                {
                    _currentIndex = index;
                    _isPaused = false; // Resume if paused
                    Logger.Info($"[{Grid?.DisplayName}] Jumping to waypoint {index}");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid waypoint index for jump: {index}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error jumping to waypoint");
            }
        }

        public List<Vector3D> GetWaypoints()
        {
            try
            {
                return new List<Vector3D>(_waypoints);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error getting waypoints");
                return new List<Vector3D>();
            }
        }

        public void SetPatrolSpeed(float speed)
        {
            try
            {
                if (speed > 0 && speed <= 200)
                {
                    _patrolSpeed = speed;
                    Logger.Debug($"[{Grid?.DisplayName}] Patrol speed set to: {speed}m/s");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Invalid patrol speed: {speed}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error setting patrol speed");
            }
        }

        public bool IsPaused()
        {
            return _isPaused;
        }

        public int GetThreatsDetected()
        {
            return _detectedThreats.Count;
        }

        public Dictionary<int, DateTime> GetWaypointTimings()
        {
            return new Dictionary<int, DateTime>(_waypointTimings);
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "PatrolBehaviorCompleted", new Dictionary<string, object>
                    {
                        ["TotalWaypoints"] = _waypoints.Count,
                        ["CompletedWaypoints"] = _waypointTimings.Count(t => t.Value != DateTime.MinValue),
                        ["TotalThreatsDetected"] = _waypointThreatHistory.SelectMany(h => h.Value).Count(),
                        ["PatrolDuration"] = (DateTime.UtcNow - _behaviorStartTime).TotalSeconds,
                        ["FinalWaypoint"] = _currentIndex
                    });
                }
                
                _waypoints?.Clear();
                _waypointTimings?.Clear();
                _waypointThreatHistory?.Clear();
                _detectedThreats?.Clear();
                
                Logger.Debug($"[{Grid?.DisplayName}] PatrolBehavior disposed");
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing PatrolBehavior");
            }
        }
    }
}