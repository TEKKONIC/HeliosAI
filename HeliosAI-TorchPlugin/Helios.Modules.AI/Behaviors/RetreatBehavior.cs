using System;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageRender;
using NLog;

namespace HeliosAI.Behaviors
{
    public class RetreatBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("RetreatBehavior");
        private Vector3D _retreatDirection;
        private double _retreatDistance = 1000;
        private Vector3D _startPosition;
        private bool _retreatComplete = false;

        public RetreatBehavior(IMyCubeGrid grid, IMyEntity attacker = null) : base(grid)
        {
            try
            {
                _startPosition = grid.GetPosition();

                if (attacker != null)
                {
                    // Retreat in the opposite direction of attacker
                    var toAttacker = attacker.GetPosition() - _startPosition;
                    if (toAttacker.LengthSquared() > 0)
                        _retreatDirection = -Vector3D.Normalize(toAttacker);
                    else
                        _retreatDirection = Vector3D.Normalize(_startPosition - Vector3D.Zero);
                        
                    Logger.Info($"[{Grid?.DisplayName}] Retreating from attacker: {attacker.DisplayName}");
                }
                else
                {
                    // Default: away from world center
                    _retreatDirection = Vector3D.Normalize(_startPosition - Vector3D.Zero);
                    Logger.Info($"[{Grid?.DisplayName}] Retreating from current position");
                }
                
                // Safe communication manager access
                var commsManager = HeliosAIPlugin.Instance?.CommunicationManager;
                if (commsManager != null)
                {
                    commsManager.RegisterAgent(this);
                    commsManager.RequestBackup(this, _startPosition);
                    Logger.Debug($"[{Grid?.DisplayName}] Backup requested during retreat");
                }
                else
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Communication manager not available for backup request");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error initializing RetreatBehavior");
            }
        }

        public override string Name => "Retreat";

        public override void Tick()
        {
            try
            {
                if (Grid?.Physics == null || Grid.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping retreat");
                    return;
                }

                if (_retreatComplete)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Retreat already complete");
                    return;
                }

                var currentPosition = Grid.GetPosition();
                var targetPos = _startPosition + _retreatDirection * _retreatDistance;
                
                // Move to retreat position
                Npc?.MoveTo(targetPos);
                
                Logger.Debug($"[{Grid.DisplayName}] Retreating to: {targetPos}");
                
                // Check if retreat is complete
                var distanceFromStart = Vector3D.Distance(currentPosition, _startPosition);
                if (distanceFromStart > _retreatDistance * 0.8) // 80% of retreat distance
                {
                    _retreatComplete = true;
                    Logger.Info($"[{Grid.DisplayName}] Retreat complete (distance: {distanceFromStart:F0}m). Resuming patrol.");
                    
                    // Resume patrol behavior
                    if (Npc?.PatrolFallback != null)
                    {
                        Npc.SetBehavior(Npc.PatrolFallback);
                    }
                    else
                    {
                        // Default fallback to idle
                        Npc?.SetBehavior(new IdleBehavior(Grid));
                        Logger.Debug($"[{Grid.DisplayName}] No patrol fallback, switching to idle");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in RetreatBehavior.Tick()");
            }
        }

        public void DebugDraw()
        {
            try
            {
                if (Grid == null) return;

                var end = _startPosition + _retreatDirection * _retreatDistance;
                Vector4 color = Color.Yellow;
                var material = VRage.Utils.MyStringId.GetOrCompute("WeaponLaser");
                
                MySimpleObjectDraw.DrawLine(
                    _startPosition, 
                    end, 
                    material, 
                    ref color, 
                    0.05f, 
                    MyBillboard.BlendTypeEnum.PostPP
                );
                
                MyRenderProxy.DebugDrawText3D(
                    end, 
                    "Retreat Target", 
                    Color.Yellow, 
                    1.0f, 
                    false
                );
                
                // Draw current position
                var currentPos = Grid.GetPosition();
                MyRenderProxy.DebugDrawText3D(
                    currentPos, 
                    $"Retreating ({Vector3D.Distance(currentPos, _startPosition):F0}m)", 
                    Color.Orange, 
                    0.8f, 
                    false
                );
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
                    Logger.Debug($"[{Grid?.DisplayName}] Retreat distance set to: {distance}m");
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
            return _startPosition + _retreatDirection * _retreatDistance;
        }

        public override void Dispose()
        {
            try
            {
                // Unregister from communication manager
                var commsManager = HeliosAIPlugin.Instance?.CommunicationManager;
                if (commsManager != null)
                {
                    commsManager.UnregisterAgent(this);
                }

                Logger.Debug($"[{Grid?.DisplayName}] RetreatBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing RetreatBehavior");
            }
        }
    }
}