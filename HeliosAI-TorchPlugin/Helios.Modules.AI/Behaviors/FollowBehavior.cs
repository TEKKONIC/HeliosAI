using System;
using System.Linq;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using NLog;

namespace HeliosAI.Behaviors
{
    public class FollowBehavior(IMyCubeGrid grid, IMyEntity target, double followDistance = 50)
        : AiBehavior(grid)
    {
        private new static readonly Logger Logger = LogManager.GetLogger("FollowBehavior");
        
        public IMyEntity Target { get; private set; } = target;
        public double FollowDistance { get; set; } = followDistance;
        
        public override string Name => "Follow";

        public override void Tick()
        {
            try
            {
                if (Target == null || Target.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Follow target is null or marked for close");
                    return;
                }

                if (Grid?.Physics == null || Grid.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid physics is null or grid marked for close");
                    return;
                }

                var gridPosition = Grid.GetPosition();
                var targetPosition = Target.GetPosition();
                var distance = Vector3D.Distance(gridPosition, targetPosition);

                if (distance > FollowDistance)
                {
                    Logger.Debug($"[{Grid.DisplayName}] Following target: distance {distance:F1}m > {FollowDistance}m");
                    Npc?.MoveTo(targetPosition);
                }
                else
                {
                    // We're close enough - stop autopilot
                    try
                    {
                        var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                            .FirstOrDefault(r => r?.IsFunctional == true);

                        if (remote != null)
                        {
                            remote.SetAutoPilotEnabled(false);
                            Logger.Debug($"[{Grid.DisplayName}] Close enough to target ({distance:F1}m), stopping autopilot");
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
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in FollowBehavior.Tick()");
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

                Target = newTarget;
                Logger.Info($"[{Grid?.DisplayName}] Follow target set to: {newTarget.DisplayName}");
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

                FollowDistance = distance;
                Logger.Debug($"[{Grid?.DisplayName}] Follow distance set to: {distance}m");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting follow distance");
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
                return distance <= FollowDistance;
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

        public override void Dispose()
        {
            try
            {
                Target = null;
                Logger.Debug($"[{Grid?.DisplayName}] FollowBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing FollowBehavior");
            }
        }
    }
}