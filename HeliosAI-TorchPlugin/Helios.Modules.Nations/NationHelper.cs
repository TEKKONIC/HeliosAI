using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.World;
using VRage.Game.ModAPI;
using Torch;
using HeliosAI.Chat;
using Sandbox.ModAPI;
using Torch.Managers;
using NLog;

namespace HeliosAI.Nations
{
    public static class NationHelper
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationHelper");
        
        // Crunch Alliances API, if present
        private static object _alliancesApi;
        private static bool _checkedForAlliances;

        public static void Init(object alliancesApi)
        {
            try
            {
                _alliancesApi = alliancesApi;
                _checkedForAlliances = true;
                Logger.Info($"NationHelper initialized with alliances API: {alliancesApi != null}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize NationHelper");
            }
        }

        public static NationType GetNationType(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to get nation type for null grid");
                return NationType.Unknown;
            }

            try
            {
                var ownerId = GetPrimaryOwner(grid);
                if (ownerId == 0)
                {
                    Logger.Debug($"Grid {grid.DisplayName} has no primary owner");
                    return NationType.Unknown;
                }

                // Try Alliances if available
                if (!_checkedForAlliances)
                {
                    TryResolveAlliances();
                }

                if (_alliancesApi != null)
                {
                    try
                    {
                        var tag = GetAllianceTagForPlayer(ownerId);
                        var nation = TagToNation(tag);
                        if (nation != NationType.Unknown)
                        {
                            Logger.Debug($"Grid {grid.DisplayName} identified as {nation} via alliance tag: {tag}");
                            return nation;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to get alliance tag for player {ownerId}");
                    }
                }

                // Fallback to factions
                var faction = MySession.Static.Factions.TryGetPlayerFaction(ownerId);
                if (faction != null)
                {
                    var nation = TagToNation(faction.Tag);
                    Logger.Debug($"Grid {grid.DisplayName} identified as {nation} via faction tag: {faction.Tag}");
                    return nation;
                }

                Logger.Debug($"Grid {grid.DisplayName} could not be identified - no faction or alliance");
                return NationType.Unknown;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get nation type for grid: {grid?.DisplayName}");
                return NationType.Unknown;
            }
        }

        private static long GetPrimaryOwner(IMyCubeGrid grid)
        {
            if (grid == null)
                return 0;

            try
            {
                var ownerCounts = new System.Collections.Generic.Dictionary<long, int>();
                var totalBlocks = 0;

                foreach (var block in grid.GetFatBlocks<IMyTerminalBlock>())
                {
                    if (block.OwnerId == 0)
                        continue;

                    totalBlocks++;
                    ownerCounts[block.OwnerId] = ownerCounts.GetValueOrDefault(block.OwnerId, 0) + 1;
                }

                if (ownerCounts.Count == 0)
                    return 0;

                // Return the owner with the most blocks
                var primaryOwner = ownerCounts.OrderByDescending(kv => kv.Value).First();
                
                // If one owner has more than 50% of blocks, consider them primary
                if (primaryOwner.Value > totalBlocks * 0.5)
                {
                    return primaryOwner.Key;
                }

                Logger.Debug($"Grid {grid.DisplayName} has multiple owners, no clear majority");
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get primary owner for grid: {grid?.DisplayName}");
                return 0;
            }
        }

        private static NationType TagToNation(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag))
                return NationType.Unknown;

            try
            {
                tag = tag.ToUpperInvariant().Trim();

                if (tag.Contains("MCRN") || tag.Contains("MARS")) return NationType.MCRN;
                if (tag.Contains("UNN") || tag.Contains("EARTH") || tag.Contains("UN")) return NationType.UNN;
                if (tag.Contains("OPA") || tag.Contains("BELT") || tag.Contains("OUTER")) return NationType.OPA;

                return NationType.Unknown;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to convert tag to nation: {tag}");
                return NationType.Unknown;
            }
        }
        
        private static void TryResolveAlliances()
        {
            _checkedForAlliances = true;

            try
            {
                // Look for Crunch's Alliances plugin via Torch
                var torch = TorchBase.Instance;
                var pluginManager = torch?.Managers?.GetManager(typeof(PluginManager)) as PluginManager;
                if (pluginManager == null)
                {
                    Logger.Debug("Plugin manager not available for alliance resolution");
                    return;
                }

                foreach (var plugin in pluginManager.Plugins)
                {
                    try
                    {
                        var type = plugin.GetType();
                        var metadataProperty = type.GetProperty("Metadata");
                        if (metadataProperty == null) continue;
                        
                        var metadata = metadataProperty.GetValue(plugin);
                        var nameProperty = metadata?.GetType().GetProperty("Name");
                        if (nameProperty == null) continue;
                        
                        var name = nameProperty.GetValue(metadata) as string;
                        if (name == "Alliances")
                        {
                            _alliancesApi = plugin;
                            Logger.Info("Successfully resolved Alliances plugin");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug(ex, "Failed to check plugin for alliances");
                    }
                }

                Logger.Debug("Alliances plugin not found");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to resolve alliances plugin");
                _alliancesApi = null;
            }
        }

        private static string GetAllianceTagForPlayer(long playerId)
        {
            if (_alliancesApi == null)
                return null;

            try
            {
                var method = _alliancesApi.GetType().GetMethod("GetAllianceTagForPlayer");
                if (method != null)
                {
                    return method.Invoke(_alliancesApi, new object[] { playerId }) as string;
                }

                Logger.Warn("GetAllianceTagForPlayer method not found in alliances API");
                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get alliance tag for player: {playerId}");
                return null;
            }
        }

        public static bool IsAlliancesApiAvailable()
        {
            return _alliancesApi != null;
        }

        public static void ClearCache()
        {
            try
            {
                _alliancesApi = null;
                _checkedForAlliances = false;
                Logger.Info("NationHelper cache cleared");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clear NationHelper cache");
            }
        }
    }
}