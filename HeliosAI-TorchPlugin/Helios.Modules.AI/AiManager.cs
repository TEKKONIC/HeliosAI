using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;
using System.Collections.Generic;
using System.Linq;
using System;
using Helios.Core;
using Helios.Core.Interfaces;
using HeliosAI;
using HeliosAI.Behaviors;
using Sandbox.Definitions;
using VRage;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using NLog;

namespace Helios.Modules.AI
{
    public class AiManager : IAiManager 
    {
        private static readonly Logger Logger = LogManager.GetLogger("AiManager");
        public static AiManager Instance { get; private set; } = new AiManager();
        public NpcEntity LastNpc => _npcs.LastOrDefault();
        public IReadOnlyList<NpcEntity> ActiveNpcs => _npcs.AsReadOnly();

        private List<NpcEntity> _npcs = new List<NpcEntity>();

        public void Update()
        {
            try
            {
                var npcsToRemove = new List<NpcEntity>();
                
                foreach (var npc in _npcs.ToList())
                {
                    if (npc?.Grid == null || npc.Grid.MarkedForClose)
                    {
                        npcsToRemove.Add(npc);
                        continue;
                    }

                    try
                    {
                        npc.Tick();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error ticking NPC {npc.Grid?.DisplayName}");
                        npcsToRemove.Add(npc); // Remove problematic NPCs
                    }
                }

                // Remove failed NPCs
                foreach (var npc in npcsToRemove)
                {
                    _npcs.Remove(npc);
                    npc?.Dispose(); // Clean up resources
                    Logger.Debug($"Removed invalid NPC entity");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Critical error in AiManager.Update()");
            }
        }

        public void SpawnNpc(Vector3D position, string prefab, NpcEntity.AiMood mood)
        {
            try
            {
                var prefabDef = MyDefinitionManager.Static.GetPrefabDefinition(prefab);
                if (prefabDef == null)
                {
                    Logger.Warn($"Prefab definition not found: {prefab}");
                    return;
                }

                foreach (var gridBuilder in prefabDef.CubeGrids)
                {
                    gridBuilder.PositionAndOrientation = new MyPositionAndOrientation(position, Vector3D.Forward, Vector3D.Up);

                    var entity = MyEntities.CreateFromObjectBuilder(gridBuilder, true);
                    MyAPIGateway.Entities.AddEntity(entity);

                    if (entity is MyCubeGrid grid)
                    {
                        var npc = new NpcEntity(grid, mood) { SpawnedPrefab = prefab };
                        _npcs.Add(npc);
                        Logger.Info($"Spawned NPC: {prefab} at {position} with mood {mood}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to spawn NPC: {prefab}");
            }
        }

        public IMyEntity FindNearestPlayer(Vector3D origin, double range)
        {
            try
            {
                var players = new HashSet<IMyEntity>();
                
                MyAPIGateway.Entities.GetEntities(players, e =>
                {
                    if (e is not IMyCharacter c) return false;
                    var dist = Vector3D.Distance(origin, e.GetPosition());
                    return c.ControllerInfo?.ControllingIdentityId != null && dist <= range;
                });

                return players.OrderBy(e => Vector3D.Distance(origin, e.GetPosition())).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding nearest player");
                return null;
            }
        }

        public IMyEntity FindTarget(Vector3D origin, double range, NpcEntity.AiMood mood, long ownFactionId = 0)
        {
            try
            {
                var candidates = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(candidates, e =>
                {
                    if (e?.MarkedForClose != false) return false;
                    var dist = Vector3D.Distance(origin, e.GetPosition());
                    if (dist > range) return false;

                    switch (e)
                    {
                        case IMyCharacter playerChar:
                        {
                            var playerId = playerChar.ControllerInfo?.ControllingIdentityId ?? 0;
                            var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                            if (playerFaction?.FactionId == ownFactionId) return false;
                            return mood != NpcEntity.AiMood.Passive;
                        }
                        case MyCubeGrid grid:
                        {
                            // Check if grid belongs to different faction
                            var gridOwner = grid.BigOwners.FirstOrDefault();
                            if (gridOwner != 0)
                            {
                                var gridFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(gridOwner);
                                if (gridFaction?.FactionId == ownFactionId) return false;
                            }
                            return mood is NpcEntity.AiMood.Aggressive or NpcEntity.AiMood.Guard;
                        }
                    }

                    return false;
                });

                // Sort by threat score, descending (highest threat first)
                return candidates
                    .OrderByDescending(e => AssessThreat(e, origin, ownFactionId))
                    .FirstOrDefault();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding target");
                return null;
            }
        }
        
        public double AssessThreat(IMyEntity entity, Vector3D origin, long ownFactionId)
        {
            try
            {
                double score = 0;
                var dist = Vector3D.Distance(origin, entity.GetPosition());

                switch (entity)
                {
                    case IMyCharacter playerChar:
                    {
                        var isArmed = playerChar.EquippedTool != null;
                        score += isArmed ? 100 : 50;
                        score += 1000 - dist;
                        var playerId = playerChar.ControllerInfo?.ControllingIdentityId ?? 0;
                        var faction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                        if (faction != null && faction.FactionId != ownFactionId)
                            score += 200;
                        break;
                    }
                    case MyCubeGrid grid:
                    {
                        var weaponCount = 0;

                        // Vanilla turrets - add null checks
                        weaponCount += grid.GetFatBlocks().Count(b => 
                            b?.DefinitionDisplayNameText?.Contains("Turret") == true);

                        // WeaponCore/CoreSystems weapons using plugin API
                        var wcapi = HeliosAIPlugin.WCAPI;
                        if (wcapi?.IsReady == true)
                        {
                            weaponCount += grid.GetFatBlocks().Count(b => 
                                b != null && wcapi.HasCoreWeapon(b as MyEntity));
                        }

                        score += weaponCount * 200;
                        score += 1000 - dist;
                        break;
                    }
                }

                return score;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error assessing threat for entity {entity?.DisplayName}");
                return 0;
            }
        }
        
        public IReadOnlyList<NpcEntity> GetAllRegistered()
        {
            try
            {
                return _npcs.AsReadOnly(); // Convert to read-only list
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting all registered NPCs");
                return new List<NpcEntity>().AsReadOnly(); // Return empty read-only list
            }
        }

        public void CustomUpdate()
        {
            try
            {
                var broadcastManager = HeliosContext.Instance?.BroadcastManager;
                if (broadcastManager == null)
                {
                    Logger.Warn("BroadcastManager not available - skipping broadcast operations");
                    return;
                }

                foreach (var npc in ActiveNpcs.ToList())
                {
                    var grid = npc.Grid as IMyEntity;
                    if (grid == null)
                        continue;

                    var position = grid.GetPosition();

                    try
                    {
                        // Example: Proximity warning
                        var player = FindNearestPlayer(position, 1500); // Within 1.5km
                        if (player != null && !npc.HasWarned)
                        {
                            broadcastManager.Broadcast(
                                npc,
                                "ProximityWarning",
                                new Dictionary<string, string>
                                {
                                    { "PlayerName", player.DisplayName ?? "Unknown" },
                                    { "ShipName", npc.SpawnedPrefab ?? "Unknown Ship" }
                                }
                            );
                            npc.HasWarned = true;
                        }

                        // Example: Engagement trigger
                        if (npc.Mood == NpcEntity.AiMood.Aggressive && !npc.EngagedRecently && player != null)
                        {
                            broadcastManager.Broadcast(
                                npc,
                                "EngagementTrigger",
                                new Dictionary<string, string>
                                {
                                    { "PlayerName", player.DisplayName ?? "Unknown" },
                                    { "ShipName", npc.SpawnedPrefab ?? "Unknown Ship" }
                                }
                            );
                            npc.EngagedRecently = true;
                        }

                        // Example: Reinforcement call
                        if (npc.NeedsHelp && !npc.CalledReinforcements)
                        {
                            broadcastManager.Broadcast(
                                npc,
                                "Reinforcement",
                                new Dictionary<string, string>
                                {
                                    { "ShipName", npc.SpawnedPrefab ?? "Unknown Ship" },
                                    { "Position", position.ToString() }
                                }
                            );
                            npc.CalledReinforcements = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error processing broadcasts for NPC {npc.Grid?.DisplayName}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in CustomUpdate broadcast processing");
            }
        }

        public void RegisterGrid(IMyCubeGrid grid, AiBehavior initialBehavior = null)
        {
            try
            {
                if (grid == null)
                {
                    Logger.Warn("Attempted to register null grid");
                    return;
                }

                if (IsRegistered(grid))
                {
                    Logger.Debug($"Grid {grid.DisplayName} already registered");
                    return;
                }

                var npc = new NpcEntity(grid as MyCubeGrid, NpcEntity.AiMood.Passive);
                if (initialBehavior != null)
                {
                    npc.SetBehavior(initialBehavior);
                }
        
                _npcs.Add(npc);
                Logger.Info($"Registered grid: {grid.DisplayName}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to register grid: {grid?.DisplayName}");
            }
        }

        public void UnregisterGrid(IMyCubeGrid grid)
        {
            try
            {
                var npc = _npcs.FirstOrDefault(n => n.Grid == grid);
                if (npc != null)
                {
                    _npcs.Remove(npc);
                    npc.Dispose();
                    Logger.Info($"Unregistered grid: {grid?.DisplayName}");
                }
                else
                {
                    Logger.Debug($"Grid {grid?.DisplayName} not found for unregistration");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to unregister grid: {grid?.DisplayName}");
            }
        }

        public void TickAll()
        {
            Update(); // Use the existing Update method
        }

        public AiBehavior GetBehavior(IMyCubeGrid grid)
        {
            try
            {
                var npc = _npcs.FirstOrDefault(n => n.Grid == grid);
                return npc?.Behavior;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting behavior for grid: {grid?.DisplayName}");
                return null;
            }
        }

        public void SetBehavior(IMyCubeGrid grid, AiBehavior behavior)
        {
            try
            {
                var npc = _npcs.FirstOrDefault(n => n.Grid == grid);
                if (npc != null)
                {
                    npc.SetBehavior(behavior);
                    Logger.Debug($"Set behavior for {grid?.DisplayName}: {behavior?.GetType().Name}");
                }
                else
                {
                    Logger.Warn($"NPC not found for grid: {grid?.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to set behavior for grid: {grid?.DisplayName}");
            }
        }

        public bool IsRegistered(IMyCubeGrid grid)
        {
            try
            {
                return _npcs.Any(n => n.Grid == grid);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking registration for grid: {grid?.DisplayName}");
                return false;
            }
        }

        public void CleanupClosedGrids()
        {
            try
            {
                var npcsToRemove = _npcs.Where(npc => npc?.Grid == null || npc.Grid.MarkedForClose).ToList();
                
                foreach (var npc in npcsToRemove)
                {
                    _npcs.Remove(npc);
                    npc?.Dispose();
                }

                if (npcsToRemove.Count > 0)
                {
                    Logger.Info($"Cleaned up {npcsToRemove.Count} closed grid(s)");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during cleanup of closed grids");
            }
        }
        
        public NpcEntity GetNpc(IMyCubeGrid grid)
        {
            try
            {
                return _npcs.FirstOrDefault(n => n.Grid == grid);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting NPC for grid: {grid?.DisplayName}");
                return null;
            }
        }

        public List<NpcEntity> GetNpcsByBehavior<T>() where T : AiBehavior
        {
            try
            {
                return _npcs.Where(n => n.Behavior is T).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting NPCs by behavior type: {typeof(T).Name}");
                return new List<NpcEntity>();
            }
        }

        public List<NpcEntity> GetNpcsByMood(NpcEntity.AiMood mood)
        {
            try
            {
                return _npcs.Where(n => n.Mood == mood).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting NPCs by mood: {mood}");
                return new List<NpcEntity>();
            }
        }

        public List<NpcEntity> GetNpcsInRange(Vector3D position, double range)
        {
            try
            {
                return _npcs.Where(n => 
                    n?.Grid != null && 
                    Vector3D.Distance(((VRage.Game.ModAPI.Ingame.IMyEntity)n.Grid).GetPosition(), position) <= range
                ).ToList();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting NPCs in range of {position}");
                return new List<NpcEntity>();
            }
        }

        public NpcStatistics GetStatistics()
        {
            try
            {
                var stats = new NpcStatistics();
        
                if (_npcs == null || _npcs.Count == 0)
                    return stats;

                stats.TotalNpcs = _npcs.Count;
                stats.AggressiveNpcs = _npcs.Count(n => n.Mood == NpcEntity.AiMood.Aggressive);
                stats.PassiveNpcs = _npcs.Count(n => n.Mood == NpcEntity.AiMood.Passive);
                stats.GuardNpcs = _npcs.Count(n => n.Mood == NpcEntity.AiMood.Guard);
        
                // Count by behavior types
                stats.AttackingNpcs = _npcs.Count(n => n.Behavior is AttackBehavior);
                stats.PatrollingNpcs = _npcs.Count(n => n.Behavior is PatrolBehavior);
                stats.IdleNpcs = _npcs.Count(n => n.Behavior is IdleBehavior);
                stats.RetratingNpcs = _npcs.Count(n => n.Behavior is RetreatBehavior);
        
                // Calculate average health - FIXED VERSION
                var totalHealth = 0f;
                var healthCount = 0;
        
                foreach (var npc in _npcs)
                {
                    try
                    {
                        if (npc?.Grid != null && !npc.Grid.MarkedForClose)
                        {
                            // Option 1: Use GetBlocks() without parameters
                            var blocks = npc.Grid.GetBlocks();
                    
                            if (blocks.Count > 0)
                            {
                                var currentIntegrity = blocks.Sum(b => b.Integrity);
                                var maxIntegrity = blocks.Sum(b => b.MaxIntegrity);
                        
                                if (maxIntegrity > 0)
                                {
                                    totalHealth += currentIntegrity / maxIntegrity;
                                    healthCount++;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error calculating health for NPC {npc?.Grid?.DisplayName}");
                    }
                }
        
                stats.AverageHealth = healthCount > 0 ? totalHealth / healthCount : 0f;
        
                return stats;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating NPC statistics");
                return new NpcStatistics();
            }
}

        public void SetGlobalMood(NpcEntity.AiMood mood)
        {
            try
            {
                var count = 0;
                foreach (var npc in _npcs.ToList())
                {
                    try
                    {
                        if (npc?.Grid != null && !npc.Grid.MarkedForClose)
                        {
                            npc.Mood = mood;
                            count++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error setting mood for NPC {npc?.Grid?.DisplayName}");
                    }
                }
        
                Logger.Info($"Set global mood to {mood} for {count} NPCs");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error setting global mood to {mood}");
            }
        }
    }
}