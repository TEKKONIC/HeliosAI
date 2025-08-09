using System.Collections.Generic;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;
using System;
using NLog;

namespace HeliosAI.Behaviors
{
    public class DefenseBehavior(IMyCubeGrid grid, Vector3D defensePosition, double radius = 1000)
        : AiBehavior(grid)
    {
        private new static readonly Logger Logger = LogManager.GetLogger("DefenseBehavior");
        
        public Vector3D DefensePosition => defensePosition;
        public List<(Vector3D Position, double Radius)> DefensePoints { get; } = new();
        public double DefenseRadius => radius;

        public override string Name => "Defense";

        public override void Tick()
        {
            try
            {
                if (Grid == null || Grid.MarkedForClose || Grid.Physics == null)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping defense");
                    return;
                }

                var currentPosition = Grid.GetPosition();

                // Check primary defense position
                var wc = HeliosAIPlugin.WeaponCoreManager;
                if (wc != null)
                {
                    try
                    {
                        wc.RegisterWeapons(Grid);
                        var target = wc.GetPriorityTarget(defensePosition, radius);
                        if (target != null)
                        {
                            Logger.Info($"[{Grid.DisplayName}] Hostile detected at defense position: {target.DisplayName}");
                            
                            // Switch to attack behavior
                            if (Npc != null)
                            {
                                Npc.SetBehavior(new AttackBehavior(Grid, target));
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{Grid.DisplayName}] Error checking primary defense position");
                    }
                }

                // Check additional defense points
                foreach (var (pos, rad) in DefensePoints.ToList())
                {
                    try
                    {
                        if (wc != null)
                        {
                            var target = wc.GetPriorityTarget(pos, rad);
                            if (target != null)
                            {
                                Logger.Info($"[{Grid.DisplayName}] Hostile detected at defense point {pos}: {target.DisplayName}");
                                
                                // Switch to attack behavior
                                if (Npc != null)
                                {
                                    Npc.SetBehavior(new AttackBehavior(Grid, target));
                                    return;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{Grid.DisplayName}] Error checking defense point {pos}");
                    }
                }

                // No threats detected - maintain defense position
                MaintainDefensePosition(currentPosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in DefenseBehavior.Tick()");
            }
        }

        private void MaintainDefensePosition(Vector3D currentPosition)
        {
            try
            {
                var distance = Vector3D.Distance(currentPosition, defensePosition);
        
                if (distance > 100) // Allow some leeway
                {
                    Logger.Debug($"[{Grid.DisplayName}] Returning to defense position (distance: {distance:F0}m)");
                    Npc?.MoveTo(defensePosition);
                }
                else
                {
                    // We're at the defense position - stop autopilot
                    try
                    {
                        var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                            .FirstOrDefault(r => r?.IsFunctional == true);

                        if (remote != null)
                        {
                            remote.SetAutoPilotEnabled(false);
                            Logger.Debug($"[{Grid.DisplayName}] Holding defense position");
                        }
                        else
                        {
                            Logger.Debug($"[{Grid.DisplayName}] No functional remote control found for autopilot");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{Grid.DisplayName}] Error stopping autopilot");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error maintaining defense position");
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

                DefensePoints.Add((position, pointRadius));
                Logger.Info($"[{Grid?.DisplayName}] Added defense point at {position} with radius {pointRadius}m");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error adding defense point");
            }
        }

        public void RemoveDefensePoint(Vector3D position)
        {
            try
            {
                var toRemove = DefensePoints
                    .Where(dp => Vector3D.Distance(dp.Position, position) < 10)
                    .ToList();

                foreach (var point in toRemove)
                {
                    DefensePoints.Remove(point);
                    Logger.Info($"[{Grid?.DisplayName}] Removed defense point at {point.Position}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error removing defense point");
            }
        }

        public void ClearDefensePoints()
        {
            try
            {
                var count = DefensePoints.Count;
                DefensePoints.Clear();
                Logger.Info($"[{Grid?.DisplayName}] Cleared {count} defense points");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error clearing defense points");
            }
        }

        public bool IsInDefenseRadius(Vector3D position)
        {
            try
            {
                var distance = Vector3D.Distance(position, defensePosition);
                return distance <= radius;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error checking defense radius");
                return false;
            }
        }

        public double GetDistanceFromDefensePosition()
        {
            try
            {
                if (Grid == null)
                    return double.MaxValue;

                return Vector3D.Distance(Grid.GetPosition(), defensePosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating distance from defense position");
                return double.MaxValue;
            }
        }

        public override void ReceiveBackupRequest(Vector3D location)
        {
            try
            {
                var distanceToRequest = Vector3D.Distance(defensePosition, location);
                
                // Only respond if the request is within our defense area or nearby
                if (distanceToRequest <= radius * 1.5) // Allow 50% extra range for backup
                {
                    Logger.Info($"[{Grid?.DisplayName}] Responding to backup request at {location}");
                    
                    // Temporarily expand defense to include backup location
                    AddDefensePoint(location, 500);
                }
                else
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Backup request too far from defense position: {distanceToRequest:F0}m");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error processing backup request");
            }
        }

        public override void Dispose()
        {
            try
            {
                DefensePoints.Clear();
                Logger.Debug($"[{Grid?.DisplayName}] DefenseBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing DefenseBehavior");
            }
        }
    }
}