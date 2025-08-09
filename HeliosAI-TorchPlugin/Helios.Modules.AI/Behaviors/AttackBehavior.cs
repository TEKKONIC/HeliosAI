using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI.Ingame;
using VRage.Game;
using VRage.ModAPI;
using VRageRender;
using System;
using NLog;

namespace HeliosAI.Behaviors
{
    public class AttackBehavior(IMyCubeGrid grid, IMyEntity target) : AiBehavior(grid)
    {
        private new static readonly Logger Logger = LogManager.GetLogger("AttackBehavior");
        public IMyEntity Target { get; private set; } = target;
        private float _lastHealth = 1f;

        public override string Name => "Attack";

        public override void Tick()
        {
            try
            {
                if (TargetInvalid())
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Target invalid, stopping attack");
                    return;
                }

                if (Grid?.Physics == null || Grid.PositionComp == null)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Grid physics or position invalid");
                    return;
                }
                
                // Move towards target
                if (Npc != null && Target != null)
                {
                    Npc.MoveTo(Target.GetPosition());
                }
                
                // Weapon targeting with null checks
                var wc = HeliosAIPlugin.WeaponCoreManager;
                if (wc != null && Target != null)
                {
                    try
                    {
                        if (wc.HasReadyWeapons(Grid))
                        {
                            var targetPos = Target.GetPosition();
                            var info = new MyDetectedEntityInfo(
                                Target.EntityId,
                                Target.DisplayName ?? "Unknown",
                                MyDetectedEntityType.CharacterHuman, 
                                targetPos, 
                                Target.WorldMatrix,                  
                                Vector3.Zero,                        
                                MyRelationsBetweenPlayerAndBlock.Enemies,
                                Target.PositionComp?.WorldAABB ?? new BoundingBoxD(),
                                0                                    
                            );

                            wc.SetTarget(Grid, info);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{Grid?.DisplayName}] Error setting weapon target");
                    }
                }
                
                // Health monitoring and retreat logic
                var integrity = CalculateGridIntegrity(Grid);
                if (integrity < _lastHealth - 0.1f)
                {
                    Logger.Info($"[{Grid?.DisplayName}] Under fire! Health: {integrity:P}, retreating...");
                    
                    if (Npc != null)
                    {
                        Npc.SetBehavior(new RetreatBehavior(Grid));
                    }
                    return;
                }
                _lastHealth = integrity;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in AttackBehavior.Tick()");
            }
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
                    
                    MyRenderProxy.DebugDrawText3D(
                        targetPos, 
                        $"Target: {Target.DisplayName ?? "Unknown"}", 
                        Color.Red, 
                        1.0f, 
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
                    Logger.Debug($"[{Grid?.DisplayName}] Target invalid: null or marked for close");
                    return true;
                }

                var gridPos = Grid.GetPosition();
                var targetPos = Target.GetPosition();
                var distance = Vector3D.Distance(gridPos, targetPos);
                
                if (distance > 1500)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Target too far: {distance:F0}m");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error checking target validity");
                return true; // Assume invalid on error
            }
        }
        
        private float CalculateGridIntegrity(IMyCubeGrid grid)
        {
            try
            {
                if (grid == null)
                    return 0f;

                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

                if (blocks.Count == 0)
                    return 0f;

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
                return _lastHealth; // Return last known health as fallback
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

                Target = newTarget;
                Logger.Info($"[{Grid?.DisplayName}] Target set to: {newTarget.DisplayName}");
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
                if (TargetInvalid())
                    return double.MaxValue;

                return Vector3D.Distance(Grid.GetPosition(), Target.GetPosition());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error calculating distance to target");
                return double.MaxValue;
            }
        }

        public override void Dispose()
        {
            try
            {
                Target = null;
                Logger.Debug($"[{Grid?.DisplayName}] AttackBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing AttackBehavior");
            }
        }
    }
}