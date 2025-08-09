using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using VRageMath;
using NLog;
using Sandbox.ModAPI;

namespace HeliosAI.Behaviors
{
    public class PatrolBehavior(IMyCubeGrid grid, List<Vector3D> waypoints) : AiBehavior(grid)
    {
        private new static readonly Logger Logger = LogManager.GetLogger("PatrolBehavior");
        private readonly List<Vector3D> _waypoints = waypoints ?? new List<Vector3D>();
        private int _currentIndex = 0;
        private double _waypointTolerance = 50.0; // Distance to consider waypoint reached

        public override string Name => "Patrol";

        public override void Tick()
        {
            try
            {
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

                var currentTarget = _waypoints[_currentIndex];
                var gridPosition = Grid.GetPosition();
                var distance = Vector3D.Distance(gridPosition, currentTarget);

                if (distance > _waypointTolerance)
                {
                    Logger.Debug($"[{Grid.DisplayName}] Patrolling to waypoint {_currentIndex}: {currentTarget} (distance: {distance:F1}m)");
                    Npc?.MoveTo(currentTarget);
                }
                else
                {
                    // Reached waypoint - move to next
                    _currentIndex = (_currentIndex + 1) % _waypoints.Count;
                    Logger.Debug($"[{Grid.DisplayName}] Reached waypoint, moving to next: {_currentIndex}");

                    // Stop autopilot briefly before heading to next waypoint
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
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in PatrolBehavior.Tick()");
            }
        }

        public void AddWaypoint(Vector3D waypoint)
        {
            try
            {
                _waypoints.Add(waypoint);
                Logger.Info($"[{Grid?.DisplayName}] Added waypoint: {waypoint} (total: {_waypoints.Count})");
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
                    
                    // Adjust current index if necessary
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
                return new List<Vector3D>(_waypoints); // Return copy to prevent external modification
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error getting waypoints");
                return new List<Vector3D>();
            }
        }

        public override void Dispose()
        {
            try
            {
                _waypoints?.Clear();
                Logger.Debug($"[{Grid?.DisplayName}] PatrolBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing PatrolBehavior");
            }
        }
    }
}