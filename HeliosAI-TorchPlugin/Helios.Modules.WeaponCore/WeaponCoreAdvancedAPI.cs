using CoreSystems.Api;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage.Game.Entity;
using VRage.ModAPI;
using NLog; // Add this
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace HeliosAI
{
    public class WeaponCoreAdvancedAPI
    {
        private WcApi _wcApi;
        private static readonly Logger Logger = LogManager.GetLogger("WeaponCoreAPI");

        public bool IsReady => _wcApi?.IsReady ?? false;

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
    }
}