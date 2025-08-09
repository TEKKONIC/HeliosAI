using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;

namespace HeliosAI
{
    public class WeaponCoreGridManager
    {
        private static readonly Logger Logger = LogManager.GetLogger("WeaponCoreGridManager");
        private readonly WeaponCoreAdvancedAPI _api;
        private readonly Dictionary<long, List<IMyTerminalBlock>> _gridWeapons = new();

        public WeaponCoreGridManager(WeaponCoreAdvancedAPI api)
        {
            _api = api ?? throw new ArgumentNullException(nameof(api));
            Logger.Info("WeaponCoreGridManager initialized");
        }

        public void RegisterWeapons(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to register weapons for null grid");
                return;
            }

            if (grid.MarkedForClose)
            {
                Logger.Debug($"Skipping weapon registration for closing grid: {grid.DisplayName}");
                return;
            }

            if (_gridWeapons.ContainsKey(grid.EntityId))
            {
                Logger.Debug($"Weapons already registered for grid: {grid.DisplayName}");
                return;
            }

            try
            {
                var weapons = new List<IMyTerminalBlock>();
                var fatBlocks = new List<IMySlimBlock>();
                grid.GetBlocks(fatBlocks);

                foreach (var slim in fatBlocks)
                {
                    var fat = slim.FatBlock as IMyTerminalBlock;
                    if (fat == null || fat.Closed || !fat.IsFunctional) continue;

                    try
                    {
                        // Use WeaponCore API to properly detect weapons
                        if (_api.HasCoreWeapon(fat))
                        {
                            weapons.Add(fat);
                        }
                        // Fallback for vanilla weapons or custom detection
                        else if (fat.BlockDefinition.ToString().Contains("Weapon") || 
                                fat.CustomName.Contains("[WC]"))
                        {
                            weapons.Add(fat);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to check weapon status for block: {fat.DisplayNameText}");
                    }
                }

                _gridWeapons[grid.EntityId] = weapons;
                Logger.Info($"Registered {weapons.Count} weapons for grid: {grid.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to register weapons for grid: {grid?.DisplayName}");
            }
        }

        public void SetTarget(IMyCubeGrid grid, MyDetectedEntityInfo target)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to set target for null grid");
                return;
            }

            if (_api?.IsReady != true)
            {
                Logger.Debug("WeaponCore API not ready for target setting");
                return;
            }

            if (!_gridWeapons.TryGetValue(grid.EntityId, out var weapons))
            {
                Logger.Debug($"No weapons registered for grid: {grid.DisplayName}");
                return;
            }

            try
            {
                var targetedCount = 0;
                foreach (var weapon in weapons)
                {
                    try
                    {
                        if (weapon?.IsFunctional == true && _api.IsWeaponReady(weapon))
                        {
                            _api.SetTarget(weapon, target);
                            targetedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to set target for weapon: {weapon?.DisplayNameText}");
                    }
                }

                Logger.Debug($"Set target for {targetedCount}/{weapons.Count} weapons on grid: {grid.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to set targets for grid: {grid.DisplayName}");
            }
        }

        public bool HasReadyWeapons(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to check ready weapons for null grid");
                return false;
            }

            if (_api?.IsReady != true)
            {
                Logger.Debug("WeaponCore API not ready for weapon status check");
                return false;
            }

            if (!_gridWeapons.TryGetValue(grid.EntityId, out var weapons))
            {
                Logger.Debug($"No weapons registered for grid: {grid.DisplayName}");
                return false;
            }

            try
            {
                return weapons.Any(weapon => weapon?.IsFunctional == true && _api.IsWeaponReady(weapon));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check ready weapons for grid: {grid.DisplayName}");
                return false;
            }
        }

        public void ClearTargets(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to clear targets for null grid");
                return;
            }

            if (_api?.IsReady != true)
            {
                Logger.Debug("WeaponCore API not ready for target clearing");
                return;
            }

            if (!_gridWeapons.TryGetValue(grid.EntityId, out var weapons))
            {
                Logger.Debug($"No weapons registered for grid: {grid.DisplayName}");
                return;
            }

            try
            {
                var clearedCount = 0;
                foreach (var weapon in weapons.Where(w => w?.IsFunctional == true))
                {
                    try
                    {
                        _api.SetTarget(weapon, default);
                        clearedCount++;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to clear target for weapon: {weapon?.DisplayNameText}");
                    }
                }

                Logger.Debug($"Cleared targets for {clearedCount} weapons on grid: {grid.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to clear targets for grid: {grid.DisplayName}");
            }
        }

        public IMyEntity GetPriorityTarget(Vector3D origin, double range, long ownFactionId = 0)
        {
            if (range <= 0)
            {
                Logger.Warn($"Invalid range for priority target search: {range}");
                return null;
            }

            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players);

                var validTargets = players
                    .Where(p => p?.Character != null && !p.IsBot)
                    .Select(p => p.Character)
                    .Where(c => c != null && !c.MarkedForClose)
                    .Where(c => Vector3D.DistanceSquared(origin, c.GetPosition()) <= range * range);

                // If faction-based targeting is needed
                if (ownFactionId != 0)
                {
                    validTargets = validTargets.Where(c => 
                    {
                        try
                        {
                            var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(c.ControllerInfo?.ControllingIdentityId ?? 0);
                            return faction?.FactionId != ownFactionId;
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, $"Failed to check faction for character: {c.DisplayName}");
                            return true; // Include target if faction check fails
                        }
                    });
                }

                var target = validTargets
                    .OrderBy(c => Vector3D.DistanceSquared(origin, c.GetPosition()))
                    .FirstOrDefault();

                if (target != null)
                {
                    Logger.Debug($"Priority target found: {target.DisplayName}");
                }

                return target;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get priority target");
                return null;
            }
        }

        public void UnregisterGrid(IMyCubeGrid grid)
        {
            if (grid == null)
                return;

            try
            {
                if (_gridWeapons.Remove(grid.EntityId))
                {
                    Logger.Info($"Unregistered weapons for grid: {grid.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to unregister grid: {grid?.DisplayName}");
            }
        }

        public int GetRegisteredGridCount()
        {
            return _gridWeapons.Count;
        }

        public int GetWeaponCount(IMyCubeGrid grid)
        {
            if (grid == null || !_gridWeapons.TryGetValue(grid.EntityId, out var weapons))
                return 0;

            return weapons.Count;
        }
    }
}