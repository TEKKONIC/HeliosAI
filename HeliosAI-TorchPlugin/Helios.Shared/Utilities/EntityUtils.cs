using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;
using NLog;

namespace HeliosAI.Utilities
{
    public static class EntityUtils
    {
        private static readonly Logger Logger = LogManager.GetLogger("EntityUtils");

        public static IMyEntity FindNearestPlayer(Vector3D origin, double range)
        {
            if (range <= 0)
            {
                Logger.Warn($"Invalid range for player search: {range}");
                return null;
            }

            try
            {
                var players = new HashSet<IMyEntity>();

                MyAPIGateway.Entities.GetEntities(players, e =>
                {
                    try
                    {
                        if (e is not IMyCharacter character)
                            return false;

                        var identity = character.ControllerInfo?.ControllingIdentityId;
                        if (!identity.HasValue)
                            return false;

                        return Vector3D.Distance(origin, character.GetPosition()) <= range;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error checking entity in FindNearestPlayer: {e?.DisplayName}");
                        return false;
                    }
                });

                var nearestPlayer = players
                    .OrderBy(e => Vector3D.Distance(origin, e.GetPosition()))
                    .FirstOrDefault();

                if (nearestPlayer != null)
                {
                    Logger.Debug($"Found nearest player: {nearestPlayer.DisplayName} at distance {Vector3D.Distance(origin, nearestPlayer.GetPosition()):F1}m");
                }

                return nearestPlayer;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to find nearest player at {origin}");
                return null;
            }
        }

        public static List<IMyCharacter> GetNearbyPlayers(Vector3D origin, double range)
        {
            if (range <= 0)
            {
                Logger.Warn($"Invalid range for nearby players search: {range}");
                return new List<IMyCharacter>();
            }

            try
            {
                var result = new List<IMyCharacter>();
                var allEntities = new HashSet<IMyEntity>();

                MyAPIGateway.Entities.GetEntities(allEntities, e =>
                {
                    try
                    {
                        return e is IMyCharacter c &&
                               c.ControllerInfo?.ControllingIdentityId != null &&
                               !c.MarkedForClose &&
                               Vector3D.Distance(origin, c.GetPosition()) <= range;
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error filtering entity: {e?.DisplayName}");
                        return false;
                    }
                });

                foreach (var ent in allEntities)
                {
                    if (ent is IMyCharacter character)
                    {
                        result.Add(character);
                    }
                }

                Logger.Debug($"Found {result.Count} nearby players within {range}m of {origin}");
                return result;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get nearby players at {origin}");
                return new List<IMyCharacter>();
            }
        }

        /// <summary>
        /// Returns all faction-owned grids near a point within range.
        /// </summary>
        public static List<MyCubeGrid> GetAllFactionGridsNearPosition(Vector3D origin, double range, long factionId)
        {
            if (range <= 0)
            {
                Logger.Warn($"Invalid range for faction grids search: {range}");
                return new List<MyCubeGrid>();
            }

            if (factionId == 0)
            {
                Logger.Warn("Invalid faction ID (0) for grid search");
                return new List<MyCubeGrid>();
            }

            try
            {
                var grids = new List<MyCubeGrid>();
                var allEntities = new HashSet<IMyEntity>();

                MyAPIGateway.Entities.GetEntities(allEntities, e => e is MyCubeGrid);

                foreach (var ent in allEntities)
                {
                    try
                    {
                        if (ent is not MyCubeGrid grid || grid.MarkedForClose || grid.IsPreview)
                            continue;

                        var owner = grid.BigOwners.FirstOrDefault();
                        if (owner == 0)
                            continue;

                        var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner);
                        if (playerFaction?.FactionId != factionId)
                            continue;

                        var distance = Vector3D.Distance(origin, ((VRage.Game.ModAPI.Ingame.IMyEntity)grid).GetPosition());
                        if (distance <= range)
                        {
                            grids.Add(grid);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error processing grid: {(ent as MyCubeGrid)?.DisplayName}");
                    }
                }

                Logger.Debug($"Found {grids.Count} faction grids for faction {factionId} within {range}m");
                return grids;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get faction grids near {origin}");
                return new List<MyCubeGrid>();
            }
        }

        /// <summary>
        /// Checks if a player is holding a weapon/tool that is potentially dangerous.
        /// </summary>
        public static bool IsPlayerArmed(IMyCharacter character)
        {
            if (character == null)
            {
                Logger.Debug("IsPlayerArmed called with null character");
                return false;
            }

            try
            {
                // Check if character has a weapon equipped
                var weaponDefinition = character.EquippedTool;
                if (weaponDefinition == null)
                    return false;

                var weaponName = weaponDefinition.ToString().ToLowerInvariant();
                
                // Check for common weapon types
                var weaponKeywords = new[] { "rifle", "pistol", "launcher", "welder", "grinder", "drill" };
                var isArmed = weaponKeywords.Any(keyword => weaponName.Contains(keyword));

                Logger.Debug($"Player {character.DisplayName} armed status: {isArmed} (weapon: {weaponName})");
                return isArmed;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check armed status for character: {character?.DisplayName}");
                return false; // Default to not armed if we can't determine
            }
        }

        /// <summary>
        /// Gets all grids within range regardless of ownership
        /// </summary>
        public static List<IMyCubeGrid> GetNearbyGrids(Vector3D origin, double range)
        {
            if (range <= 0)
            {
                Logger.Warn($"Invalid range for grid search: {range}");
                return new List<IMyCubeGrid>();
            }

            try
            {
                var grids = new List<IMyCubeGrid>();
                var allEntities = new HashSet<IMyEntity>();

                MyAPIGateway.Entities.GetEntities(allEntities, e => 
                    e is IMyCubeGrid grid && 
                    !grid.MarkedForClose && 
                    Vector3D.Distance(origin, e.GetPosition()) <= range);

                grids.AddRange(allEntities.Cast<IMyCubeGrid>());

                Logger.Debug($"Found {grids.Count} grids within {range}m of {origin}");
                return grids;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get nearby grids at {origin}");
                return new List<IMyCubeGrid>();
            }
        }

        /// <summary>
        /// Checks if a position is safe for spawning (no grids/players nearby)
        /// </summary>
        public static bool IsPositionSafeForSpawning(Vector3D position, double safetyRadius = 1000.0)
        {
            try
            {
                var nearbyPlayers = GetNearbyPlayers(position, safetyRadius);
                if (nearbyPlayers.Count > 0)
                {
                    Logger.Debug($"Position {position} not safe - {nearbyPlayers.Count} players nearby");
                    return false;
                }

                var nearbyGrids = GetNearbyGrids(position, safetyRadius);
                if (nearbyGrids.Count > 0)
                {
                    Logger.Debug($"Position {position} not safe - {nearbyGrids.Count} grids nearby");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check position safety at {position}");
                return false; // Default to not safe if we can't determine
            }
        }

        /// <summary>
        /// Gets the closest enemy grid to the specified position
        /// </summary>
        public static IMyCubeGrid GetClosestEnemyGrid(Vector3D origin, long ownFactionId, double range)
        {
            try
            {
                var nearbyGrids = GetNearbyGrids(origin, range);
                
                return nearbyGrids
                    .Where(grid => IsGridHostile(grid, ownFactionId))
                    .OrderBy(grid => Vector3D.Distance(origin, grid.GetPosition()))
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get closest enemy grid at {origin}");
                return null;
            }
        }

        /// <summary>
        /// Checks if a grid is hostile to the specified faction
        /// </summary>
        public static bool IsGridHostile(IMyCubeGrid grid, long ownFactionId)
        {
            if (grid == null || ownFactionId == 0)
                return false;

            try
            {
                var gridOwner = grid.BigOwners.FirstOrDefault();
                if (gridOwner == 0)
                    return false; // No owner = not hostile

                var gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                if (gridFaction == null)
                    return true; // No faction = potentially hostile

                var ownFaction = MyAPIGateway.Session.Factions.TryGetFactionById(ownFactionId);
                if (ownFaction == null)
                    return true;

                // Check faction relations
                var relation = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(ownFactionId, gridFaction.FactionId);
                return relation == VRage.Game.MyRelationsBetweenFactions.Enemies;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check grid hostility: {grid?.DisplayName}");
                return false;
            }
        }
    }
}