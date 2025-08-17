using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Helios.Core.Interfaces;
using Helios.Modules.Encounters;
using Helios.Modules.AI.Behaviors;
using Helios.Modules.AI.Combat;
using Helios.Modules.API;
using NLog;
using Torch.API;
using VRage.Utils;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace HeliosAI.NPCZones
{
    public class ZoneManager : IZoneManager
    {
        public List<ZoneProfile> Zones = new();
        public static string PluginFolder { get; private set; }
        public static string ZonesFolder { get; private set; }
        public static string EncountersFolder { get; private set; }
        private static readonly Logger Logger = LogManager.GetLogger("ZoneManager");
        private AdaptiveBehaviorEngine _behaviorEngine = new AdaptiveBehaviorEngine();
        private PredictiveAnalyzer _predictiveAnalyzer = new PredictiveAnalyzer();
        
        public bool IsInitialized { get; private set; }

        public async Task InitializeAsync(ITorchBase torch)
        {
            PluginFolder = Path.Combine(torch.Config.InstancePath, "HeliosAI");
            ZonesFolder = Path.Combine(PluginFolder, "Zones");
            EncountersFolder = Path.Combine(PluginFolder, "Encounters");

            Directory.CreateDirectory(PluginFolder);
            Directory.CreateDirectory(ZonesFolder);
            Directory.CreateDirectory(EncountersFolder);

            Logger.Info("[ZoneManager] Directories ensured:");
            Logger.Info($"  Plugin:     {PluginFolder}");
            Logger.Info($"  Zones:      {ZonesFolder}");
            Logger.Info($"  Encounters: {EncountersFolder}");
            
            IsInitialized = true;
            await Task.CompletedTask;
        }

        public async Task LoadZonesAsync(string path)
        {
            Logger.Info($"[ZoneManager] Loading zones from: {path}");
            await Task.CompletedTask; // Placeholder for actual async loading logic
        }

        public void Tick()
        {
            if (!IsInitialized || HeliosAIPlugin.EncounterMgr == null) return;
    
            foreach (var zone in Zones)
            {
                if (!zone.Active) continue;

                if (zone.UsesNexus && !IsInCorrectSector(zone)) continue;

                if (zone.ShouldSpawn())
                {
                    var profileId = zone.GetRandomEncounterProfile();
                    if (profileId == null) continue;
                    var profile = HeliosAIPlugin.EncounterMgr.GetProfile(profileId);
                    if (profile == null) continue;
                    var pos = zone.GetRandomPositionInside();
                    GridSpawner.SpawnWithBehavior(profile, pos);
                    zone.MarkSpawned();
                }
                
                UpdateZoneAI(zone);
            }
        }

        public void UpdateZoneAI(ZoneProfile zone)
        {
            try
            {
                var entitiesInZone = GetEntitiesInZone(zone);
                foreach (var entity in entitiesInZone)
                {
                    if (IsAIEntity(entity))
                    {
                        UpdateEntityAI(entity, zone);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating AI for zone {zone.ZoneId}");
            }
        }

        private List<IMyEntity> GetEntitiesInZone(ZoneProfile zone)
        {
            var entitiesInZone = new List<IMyEntity>();
            
            try
            {
                // TODO: Implement actual entity detection within zone bounds
                // This would typically use MyAPIGateway.Entities to find entities
                // within the zone's sphere (Center + Radius)
                
                /*
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntitiesInSphere(ref zone.Center, zone.Radius, entities);
                
                foreach (var entity in entities)
                {
                    if (entity is IMyCubeGrid grid && !grid.MarkedForClose)
                    {
                        entitiesInZone.Add(entity);
                    }
                }
                */
                
                Logger.Debug($"Found {entitiesInZone.Count} entities in zone {zone.ZoneId}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error getting entities in zone {zone.ZoneId}");
            }
            
            return entitiesInZone;
        }

        private bool IsAIEntity(IMyEntity entity)
        {
            try
            {
                if (entity is IMyCubeGrid grid)
                {
                    // TODO: Implement logic to determine if grid is AI-controlled
                    // This could check for specific tags, ownership, or registry with AI system
                    
                    // For now, assume all non-player grids are AI entities
                    return !IsPlayerControlled(grid);
                }
                
                return false;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking if entity {entity?.EntityId} is AI");
                return false;
            }
        }

        private bool IsPlayerControlled(IMyCubeGrid grid)
        {
            try
            {
                // TODO: Implement actual player ownership check
                // This would typically check grid ownership and faction data
                
                /*
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                
                foreach (var block in blocks)
                {
                    if (block.CubeGrid.GetCubeBlock(block.Position) is IMyTerminalBlock terminal)
                    {
                        if (terminal.OwnerId != 0)
                        {
                            // Check if owner is a player
                            var player = MyAPIGateway.Players.GetPlayerByID(terminal.OwnerId);
                            if (player != null)
                                return true;
                        }
                    }
                }
                */
                
                return false; // Default to AI-controlled for now
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error checking player control for grid {grid?.EntityId}");
                return false;
            }
        }

        private void UpdateEntityAI(IMyEntity entity, ZoneProfile zone)
        {
            try
            {
                if (entity is IMyCubeGrid grid)
                {
                    _predictiveAnalyzer.UpdateMovementHistory(entity);
                    var context = GatherZoneAIContext(grid, zone);
                    var availableBehaviors = GetZoneBehaviors(zone);
                    var selectedBehavior = _behaviorEngine.SelectOptimalBehavior(
                        entity.EntityId, context, availableBehaviors);
                    
                    if (!string.IsNullOrEmpty(selectedBehavior))
                    {
                        var success = ExecuteZoneBehavior(grid, selectedBehavior, zone);
                        _behaviorEngine.ReportBehaviorOutcome(
                            entity.EntityId, selectedBehavior, success, context);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error updating AI for entity {entity?.EntityId} in zone {zone.ZoneId}");
            }
        }

        private Dictionary<string, float> GatherZoneAIContext(IMyCubeGrid grid, ZoneProfile zone)
        {
            var context = new Dictionary<string, float>();
            
            try
            {
                var gridPosition = grid.PositionComp.GetPosition();
                var distanceFromCenter = Vector3D.Distance(gridPosition, zone.Center);
                
                context["DistanceFromZoneCenter"] = (float)(distanceFromCenter / zone.Radius);
                context["ZoneRadius"] = (float)zone.Radius;
                context["IsNearZoneEdge"] = distanceFromCenter > zone.Radius * 0.8f ? 1.0f : 0.0f;
                context["ZoneType"] = zone.NoSpawnZone ? 0.0f : 1.0f; // 0 = safe zone, 1 = active zone
                context["MaxSpawnsReached"] = zone.MaxSpawns > 0 ? 1.0f : 0.0f;
                context["HealthPercentage"] = GetGridHealth(grid);
                context["PowerLevel"] = GetGridPower(grid);
                context["NearbyEntities"] = GetNearbyEntityCount(grid);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error gathering zone AI context for grid {grid?.EntityId}");
            }
            
            return context;
        }

        private List<string> GetZoneBehaviors(ZoneProfile zone)
        {
            var behaviors = new List<string>();
            
            if (zone.NoSpawnZone)
            {
                behaviors.AddRange(new[] { "Patrol_Safe", "Escort_Civilian", "Trade_Safe" });
            }
            else
            {
                behaviors.AddRange(new[] { "Patrol_Combat", "Hunt_Aggressive", "Defend_Zone" });
            }
            
            return behaviors;
        }

        private bool ExecuteZoneBehavior(IMyCubeGrid grid, string behavior, ZoneProfile zone)
        {
            try
            {
                Logger.Debug($"Executing zone behavior {behavior} for grid {grid.EntityId} in zone {zone.ZoneId}");
                
                // TODO: Implement actual zone behavior execution
                // This would integrate with your ship control and navigation systems
                
                return true; // Placeholder
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error executing zone behavior {behavior} for grid {grid?.EntityId}");
                return false;
            }
        }

        private float GetGridHealth(IMyCubeGrid grid) { return 1.0f; } // TODO: Implement
        private float GetGridPower(IMyCubeGrid grid) { return 1.0f; } // TODO: Implement
        private float GetNearbyEntityCount(IMyCubeGrid grid) { return 0f; } // TODO: Implement

        public void Shutdown()
        {
            Logger.Info("[ZoneManager] Shutting down...");
            Zones.Clear();
            IsInitialized = false;
        }

        public void Dispose()
        {
            Shutdown();
        }

        private bool IsInCorrectSector(ZoneProfile zone)
        {
            if (!NexusIntegration.IsAvailable) return false;
            return NexusIntegration.CurrentSectorMatches(zone.NexusSectors);
        }

        public ZoneProfile GetZoneForPosition(Vector3D position) {
    // Implement your logic here to return the correct Zone for the given position
    // Example:
    foreach (var zone in Zones) {
        if (zone.Contains(position))
            return zone;
    }
    return null;
}
    }
    
    public class ZoneProfile
    {
        public string ZoneId;
        public string DisplayName;
        public bool Active;
        public bool Persistent;
        public Vector3D Center;
        public double Radius;
        public List<string> EncounterProfiles;
        public int SpawnIntervalSeconds = 600;
        public int MaxSpawns = 3;
        public bool NoSpawnZone = false;
        public List<string> NexusSectors = new();

        private DateTime _lastSpawn = DateTime.MinValue;

        public bool UsesNexus => NexusSectors != null && NexusSectors.Count > 0;

        public bool ShouldSpawn()
        {
            return (DateTime.UtcNow - _lastSpawn).TotalSeconds >= SpawnIntervalSeconds;
        }

        public string GetRandomEncounterProfile()
        {
            if (EncounterProfiles == null || EncounterProfiles.Count == 0) return null;
            return EncounterProfiles[MyUtils.GetRandomInt(EncounterProfiles.Count)];
        }

        public Vector3D GetRandomPositionInside()
        {
            return Center + MyUtils.GetRandomVector3Normalized() * (float)MyUtils.GetRandomDouble(0, Radius);
        }

        public void MarkSpawned()
        {
            _lastSpawn = DateTime.UtcNow;
        }

        public bool Contains(Vector3D position)
        {
            // Check if the position is within the radius of the zone
            return Vector3D.Distance(position, Center) <= Radius;
        }
    }
    
    public static class NexusIntegration
    {
        public static bool IsAvailable => APIManager.Nexus?.Enabled == true;
        public static string CurrentSector => APIManager.Nexus?.CurrentServerID.ToString() ?? "Unknown";

        public static bool CurrentSectorMatches(List<string> sectors)
        {
            if (!IsAvailable || sectors == null || sectors.Count == 0) return false;
            return sectors.Contains(CurrentSector);
        }
    }
}