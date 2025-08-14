using System;
using System.Collections.Generic;
using CoreSystems.Api;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage.Game.Entity;
using VRage.ModAPI;
using NLog;
using VRage.Game.ModAPI;
using VRageMath;
using Helios.Modules.AI.Combat;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace HeliosAI
{
    public class WeaponCoreAdvancedAPI
    {
        private WcApi _wcApi;
        private PredictiveAnalyzer _predictiveAnalyzer; 
        private static readonly Logger Logger = LogManager.GetLogger("WeaponCoreAPI");

        public bool IsReady => _wcApi?.IsReady ?? false;
        
        public WeaponCoreAdvancedAPI()
        {
            _predictiveAnalyzer = new PredictiveAnalyzer();
        }

        public WeaponCoreAdvancedAPI(PredictiveAnalyzer predictiveAnalyzer)
        {
            _predictiveAnalyzer = predictiveAnalyzer ?? new PredictiveAnalyzer();
        }

        public void LoadWeaponCoreAPI()
        {
            _wcApi = new WcApi();

            try
            {
                _wcApi.Load(() =>
                {
                    Logger.Info("WeaponCore API loaded and ready.");
                });

                if (!_wcApi.IsReady)
                {
                    Logger.Warn("WeaponCore API not ready after Load(). Skipping WeaponCore integration."); 
                    _wcApi = null;
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, "Failed to load WeaponCore API"); 
                _wcApi = null;
            }
        }

        public void SetTarget(IMyTerminalBlock weapon, MyDetectedEntityInfo target)
        {
            if (_wcApi?.IsReady != true) return;

            MyEntity targetEntity = null;
            if (target.EntityId != 0 && MyAPIGateway.Entities.TryGetEntityById(target.EntityId, out var entity))
                targetEntity = entity as MyEntity;

            _wcApi?.SetWeaponTarget(weapon as MyEntity, targetEntity);
        }

        public MyEntity GetCurrentTarget(IMyTerminalBlock weapon)
        {
            if (_wcApi?.IsReady != true) return null;
            return _wcApi?.GetWeaponTarget(weapon as MyEntity).Item4 as MyEntity;
        }

        public bool IsWeaponReady(IMyTerminalBlock weapon)
        {
            if (_wcApi?.IsReady != true) return false;
            return _wcApi?.IsWeaponReadyToFire(weapon as MyEntity) ?? false;
        }

        public float GetMaxRange(IMyTerminalBlock weapon)
        {
            if (_wcApi?.IsReady != true) return 0f;
            return _wcApi?.GetMaxWeaponRange(weapon as MyEntity, 0) ?? 0f;
        }
        
        public bool HasCoreWeapon(IMyEntity entity)
        {
            if (_wcApi?.IsReady != true) return false;
            return _wcApi?.HasCoreWeapon(entity as MyEntity) ?? false;
        }
        
        public void EnhanceTargeting(IMyCubeGrid grid, IMyEntity target)
        {
            try
            {
                if (_predictiveAnalyzer == null || target == null)
                {
                    Logger.Debug("PredictiveAnalyzer or target is null, skipping enhanced targeting");
                    return;
                }

                _predictiveAnalyzer.UpdateMovementHistory(target);

                var predictedPos = _predictiveAnalyzer.PredictEnemyPosition(target, 2.0f);
                var optimalWeapon = _predictiveAnalyzer.AnalyzeOptimalWeapons(target);
                
                SetTargetingData(grid, predictedPos, optimalWeapon.WeaponType);
                
                Logger.Debug($"Enhanced targeting applied for grid {grid.EntityId} targeting {target.EntityId}");
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, $"Error in enhanced targeting for grid {grid?.EntityId}");
            }
        }

        private void SetTargetingData(IMyCubeGrid grid, Vector3D predictedPosition, string weaponType)
        {
            try
            {
                if (_wcApi?.IsReady != true || grid == null)
                    return;

                var weapons = GetWeaponsOfType(grid, weaponType);
                
                foreach (var weapon in weapons)
                {
                    if (IsWeaponReady(weapon))
                    {
                        // Set predicted position as target
                        // Note: WeaponCore may need specific implementation for position targeting
                        // This is a simplified approach - you may need to adapt based on WC API
                        SetPredictedTarget(weapon, predictedPosition);
                    }
                }
                
                Logger.Debug($"Set targeting data for {weapons.Count} weapons of type {weaponType}");
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, $"Error setting targeting data for grid {grid?.EntityId}");
            }
        }

        private List<IMyTerminalBlock> GetWeaponsOfType(IMyCubeGrid grid, string weaponType)
        {
            var weapons = new List<IMyTerminalBlock>();
            
            try
            {
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                
                foreach (var block in blocks)
                {
                    if (block.CubeGrid.GetCubeBlock(block.Position)?.CubeGrid != null)
                    {
                        var terminal = block.CubeGrid.GetCubeBlock(block.Position) as IMyTerminalBlock;
                        if (terminal != null && IsWeaponOfType(terminal, weaponType))
                        {
                            weapons.Add(terminal);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, $"Error getting weapons of type {weaponType} from grid {grid?.EntityId}");
            }
            
            return weapons;
        }

        private bool IsWeaponOfType(IMyTerminalBlock weapon, string weaponType)
        {
            if (weapon == null || string.IsNullOrEmpty(weaponType))
                return false;

            var subtype = weapon.BlockDefinition.SubtypeName;
            var displayName = weapon.DisplayNameText;
    
            return subtype.IndexOf(weaponType, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   displayName.IndexOf(weaponType, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void SetPredictedTarget(IMyTerminalBlock weapon, Vector3D position)
        {
            try
            {
                // TODO: Implement position-based targeting with WeaponCore
                // This may require creating a temporary entity or using WC's position targeting features
                // For now, log the predicted position
                Logger.Debug($"Setting predicted target position {position} for weapon {weapon.EntityId}");
                
                // Example implementation (adapt based on actual WC API capabilities):
                // _wcApi?.SetWeaponTarget(weapon as MyEntity, null, position);
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, $"Error setting predicted target for weapon {weapon?.EntityId}");
            }
        }

        public void UpdateTargetTracking(IMyEntity target)
        {
            try
            {
                if (_predictiveAnalyzer != null && target != null)
                {
                    _predictiveAnalyzer.UpdateMovementHistory(target);
                }
            }
            catch (System.Exception ex)
            {
                Logger.Error(ex, $"Error updating target tracking for entity {target?.EntityId}");
            }
        }
    }
}