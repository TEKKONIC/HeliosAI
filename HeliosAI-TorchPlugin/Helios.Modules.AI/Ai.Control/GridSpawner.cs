using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using HeliosAI.Behaviors;
using Helios.Modules.AI;
using HeliosAI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Core.Interfaces;
using NLog;
using VRage.ModAPI;

namespace Helios.Modules.Encounters
{
    public static class GridSpawner
    {
        private static readonly Logger Logger = LogManager.GetLogger("GridSpawner");

        public static void SpawnWithBehavior(EncounterProfile profile, Vector3D position, ActiveEncounter encounter = null)
        {
            var grids = new List<IMyCubeGrid>();

            try
            {
                if (profile == null)
                {
                    Logger.Error("Cannot spawn: EncounterProfile is null");
                    return;
                }

                if (string.IsNullOrEmpty(profile.PrefabName))
                {
                    Logger.Error($"Cannot spawn: PrefabName is null or empty for profile {profile.Id}");
                    return;
                }

                Logger.Info($"Spawning prefab '{profile.PrefabName}' at position {position}");

                MyAPIGateway.PrefabManager.SpawnPrefab(
                    grids,
                    profile.PrefabName,
                    position,
                    Vector3.Forward,
                    Vector3.Up,
                    Vector3.Zero,
                    Vector3.Zero,
                    null,
                    SpawningOptions.UseGridOrigin | SpawningOptions.SetAuthorship,
                    false,
                    () =>
                    {
                        ProcessSpawnedGrids(grids, profile, position, encounter);
                    }
                );
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to spawn prefab '{profile.PrefabName}'");
            }
        }

        private static void ProcessSpawnedGrids(List<IMyCubeGrid> grids, EncounterProfile profile, Vector3D position, ActiveEncounter encounter)
        {
            try
            {
                foreach (var grid in grids.ToList())
                {
                    try
                    {
                        if (grid?.Physics == null || grid.MarkedForClose)
                        {
                            Logger.Warn("Skipping invalid or closed grid");
                            continue;
                        }

                        var myGrid = grid as MyCubeGrid;
                        if (myGrid == null)
                        {
                            Logger.Warn("Spawned entity is not a MyCubeGrid");
                            continue;
                        }

                        var defaultMood = profile.DefaultMood != default(NpcEntity.AiMood) 
                            ? profile.DefaultMood 
                            : NpcEntity.AiMood.Guard;
                        var npc = new NpcEntity(grid, defaultMood);

                        AssignBehavior(npc, profile, position, myGrid);

                        if (!string.IsNullOrEmpty(profile.FactionTag))
                        {
                            AssignOwnership(myGrid, profile.FactionTag);
                        }

                        var aiManager = AiManager.Instance;
                        if (aiManager != null)
                        {
                            aiManager.RegisterGrid(grid, npc.Behavior);
                            Logger.Info($"Spawned and registered grid: {grid.DisplayName}");
                        }
                        else
                        {
                            Logger.Warn("AiManager not available for grid registration");
                        }

                        if (encounter != null)
                        {
                            encounter.SpawnedEntityIds.Add(grid.EntityId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error processing spawned grid: {grid?.DisplayName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing spawned grids");
            }
        }

        private static void AssignBehavior(NpcEntity npc, EncounterProfile profile, Vector3D position, MyCubeGrid grid)
        {
            try
            {
                var behaviorType = profile.DefaultBehavior?.ToLowerInvariant() ?? "patrol";

                switch (behaviorType)
                {
                    case "attack":
                    case "aggressive":
                    {
                        var target = FindNearestTarget(((IMyEntity)grid).GetPosition());
                        npc.Behavior = target != null
                            ? new AttackBehavior(grid, target)
                            : new IdleBehavior(grid);
                        Logger.Debug($"Assigned attack behavior to {grid.DisplayName}");
                        break;
                    }

                    case "defense":
                    case "defensive":
                    {
                        var radius = profile.DefenseRadius > 0 ? profile.DefenseRadius : 1000;
                        npc.Behavior = new DefenseBehavior(grid, position, radius);
                        Logger.Debug($"Assigned defense behavior to {grid.DisplayName} (radius: {radius})");
                        break;
                    }

                    case "patrol":
                    {
                        var waypoints = profile.Waypoints ?? GenerateDefaultWaypoints(position);
                        npc.Behavior = new PatrolBehavior(grid, waypoints);
                        npc.PatrolFallback = npc.Behavior;
                        Logger.Debug($"Assigned patrol behavior to {grid.DisplayName} ({waypoints.Count} waypoints)");
                        break;
                    }

                    case "idle":
                    default:
                    {
                        npc.Behavior = new IdleBehavior(grid);
                        Logger.Debug($"Assigned idle behavior to {grid.DisplayName}");
                        break;
                    }
                }

                if (profile.DefaultMood != default(NpcEntity.AiMood))
                {
                    npc.Mood = profile.DefaultMood;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error assigning behavior to {grid?.DisplayName}");
                npc.Behavior = new IdleBehavior(grid);
            }
        }

        private static IMyEntity FindNearestTarget(Vector3D position)
        {
            try
            {
                var aiManager = AiManager.Instance;
                if (aiManager != null)
                {
                    return aiManager.FindNearestPlayer(position, 2000);
                }

                var candidates = new HashSet<IMyEntity>();
        
                MyAPIGateway.Entities.GetEntities(candidates, entity =>
                {
                    if (entity is IMyCharacter character &&
                        character.ControllerInfo?.ControllingIdentityId != null)
                    {
                        var distance = Vector3D.Distance(position, entity.GetPosition());
                        return distance <= 2000;
                    }
                    return false;
                });

                return candidates
                    .OrderBy(c => Vector3D.Distance(position, c.GetPosition()))
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding nearest target");
                return null;
            }
        }

        private static List<Vector3D> GenerateDefaultWaypoints(Vector3D center, int count = 4, double radius = 2000)
        {
            try
            {
                var waypoints = new List<Vector3D>();
                var random = new Random();

                for (var i = 0; i < count; i++)
                {
                    var angle = (2.0 * Math.PI * i) / count;
                    var offset = new Vector3D(
                        Math.Cos(angle) * radius,
                        (random.NextDouble() - 0.5) * 500, 
                        Math.Sin(angle) * radius
                    );
                    waypoints.Add(center + offset);
                }

                Logger.Debug($"Generated {count} default waypoints around {center}");
                return waypoints;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error generating default waypoints");
                return [center];
            }
        }

        private static void AssignOwnership(MyCubeGrid grid, string factionTag)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(factionTag))
                {
                    Logger.Debug($"No faction tag specified for grid {grid.DisplayName}");
                    return;
                }

                var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
                if (faction == null)
                {
                    Logger.Warn($"Faction tag '{factionTag}' not found for grid {grid.DisplayName}");
                    return;
                }

                var assignedCount = 0;
                foreach (var block in grid.GetFatBlocks().OfType<MyCubeBlock>())
                {
                    try
                    {
                        if (block.OwnerId == 0)
                        {
                            block.ChangeOwner(faction.FounderId, MyOwnershipShareModeEnum.Faction);
                            assignedCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error assigning ownership to block {block.BlockDefinition.DisplayNameText}");
                    }
                }

                Logger.Info($"Assigned ownership of {assignedCount} blocks to faction '{factionTag}' for grid {grid.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error assigning ownership for grid {grid?.DisplayName}");
            }
        }

        public static void DespawnEncounterGrids(ActiveEncounter encounter)
        {
            try
            {
                if (encounter?.SpawnedEntityIds == null)
                    return;

                var despawnedCount = 0;
                foreach (var entityId in encounter.SpawnedEntityIds.ToList())
                {
                    try
                    {
                        if (MyAPIGateway.Entities.TryGetEntityById(entityId, out var entity))
                        {
                            if (entity is IMyCubeGrid grid)
                            {
                                Logger.Info($"Despawning encounter grid: {grid.DisplayName}");
                                
                                var aiManager = AiManager.Instance;
                                aiManager?.UnregisterGrid(grid);
                                
                                grid.Close();
                                despawnedCount++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error despawning entity {entityId}");
                    }
                }

                encounter.SpawnedEntityIds.Clear();
                Logger.Info($"Despawned {despawnedCount} grids for encounter {encounter.Id}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error despawning encounter grids for {encounter?.Id}");
            }
        }
    }
}