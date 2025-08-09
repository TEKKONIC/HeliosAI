using System.Collections.Generic;
using Helios.Modules.AI;
using Helios.Modules.Encounters;
using HeliosAI.Behaviors;
using Helios.Core.Interfaces;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;
using VRageMath;
using System.Linq;
using VRage.ModAPI;

namespace HeliosAI
{
    [Category("helios")]
    public class AICommands : CommandModule
    {
        [Command("spawn", "Spawns a Helios AI-controlled NPC grid at your location")]
        [Permission(MyPromoteLevel.Admin)]
        public void Spawn(string prefab)
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("Only players can use this command.");
                return;
            }

            try
            {
                var position = player.GetPosition() + player.Character.WorldMatrix.Forward * 50;
                
                // Fixed: Use proper AiManager instance access
                var aiManager = AiManager.Instance;
                if (aiManager == null)
                {
                    Context.Respond("AiManager not available.");
                    return;
                }

                aiManager.SpawnNpc(position, prefab, NpcEntity.AiMood.Aggressive);
                Context.Respond($"HeliosAI: Spawned NPC prefab '{prefab}' 50m ahead.");
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error spawning NPC: {ex.Message}");
            }
        }

        [Command("patrol", "Assign patrol and auto-attack behavior")]
        [Permission(MyPromoteLevel.Admin)]
        public void Patrol()
        {
            try
            {
                var aiManager = AiManager.Instance;
                if (aiManager == null)
                {
                    Context.Respond("AiManager not available.");
                    return;
                }

                var npc = aiManager.LastNpc;
                if (npc == null)
                {
                    Context.Respond("No NPC spawned yet.");
                    return;
                }

                var pos = ((IMyEntity)npc.Grid).GetPosition();
                var waypoints = new List<Vector3D>
                {
                    pos + new Vector3D(0, 0, 100),
                    pos + new Vector3D(100, 0, 0),
                    pos + new Vector3D(0, 0, -100),
                    pos + new Vector3D(-100, 0, 0),
                };

                var patrol = new PatrolBehavior(npc.Grid, waypoints);
                npc.Behavior = patrol;
                npc.PatrolFallback = patrol;

                Context.Respond("Assigned patrol with auto-targeting.");
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error setting patrol: {ex.Message}");
            }
        }
        
        [Command("defensepoint", "Add a defense point to the last spawned NPC. Usage: /helios defensepoint [x] [y] [z] [radius] or without coordinates to use player position.")]
        [Permission(MyPromoteLevel.Admin)]
        public void AddDefensePoint(string x = null, string y = null, string z = null, string radius = null)
        {
            try
            {
                var aiManager = AiManager.Instance;
                if (aiManager == null)
                {
                    Context.Respond("AiManager not available.");
                    return;
                }

                var npc = aiManager.LastNpc;
                if (npc == null)
                {
                    Context.Respond("No NPC spawned yet.");
                    return;
                }

                if (npc.Behavior is not DefenseBehavior defense)
                {
                    Context.Respond("Current NPC is not in DefenseBehavior mode.");
                    return;
                }

                Vector3D position;
                double rad = 1000;

                // Try to parse coordinates, fallback to player location if missing
                if (x == null || y == null || z == null)
                {
                    var player = Context.Player;
                    if (player == null)
                    {
                        Context.Respond("Only players can use this command without coordinates.");
                        return;
                    }
                    position = player.GetPosition();
                }
                else
                {
                    if (!double.TryParse(x, out var px) ||
                        !double.TryParse(y, out var py) ||
                        !double.TryParse(z, out var pz))
                    {
                        Context.Respond("Invalid coordinates. Usage: /helios defensepoint [x] [y] [z] [radius]");
                        return;
                    }
                    position = new Vector3D(px, py, pz);
                }

                if (radius != null && !double.TryParse(radius, out rad))
                {
                    Context.Respond("Invalid radius. Usage: /helios defensepoint [x] [y] [z] [radius]");
                    return;
                }

                defense.AddDefensePoint(position, rad);

                Context.Respond($"Added defense point at {position} (radius {rad}) to NPC '{npc.Grid.DisplayName}'.");
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error adding defense point: {ex.Message}");
            }
        }
        
        [Command("profile", "Spawns an NPC using an encounter profile. Usage: /helios profile [id]")]
        [Permission(MyPromoteLevel.Admin)]
        public void SpawnFromProfile(string id)
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("Only players can use this command.");
                return;
            }

            try
            {
                // Fixed: Use proper EncounterManager access
                var encounterManager = GetEncounterManager();
                if (encounterManager == null)
                {
                    Context.Respond("EncounterManager not available.");
                    return;
                }

                var profile = encounterManager.GetProfile(id);
                if (profile == null)
                {
                    Context.Respond($"Profile '{id}' not found.");
                    return;
                }

                var origin = player.GetPosition();
                
                // Fixed: Use safe property access
                var minDistance = profile.MinDistanceFromPlayers;
                var maxDistance = profile.RequiredPlayerDistance + 1000; // Add buffer
                var spawnPos = encounterManager.GetSpawnPosition(origin, minDistance, maxDistance);

                var encounter = encounterManager.SpawnEncounter(id, spawnPos, player.IdentityId);
                if (encounter != null)
                {
                    GridSpawner.SpawnWithBehavior(profile, spawnPos, encounter);
                    Context.Respond($"Spawning encounter '{id}' near you...");
                }
                else
                {
                    Context.Respond($"Failed to spawn encounter '{id}'.");
                }
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error spawning profile: {ex.Message}");
            }
        }
        
        [Command("list", "Lists all loaded encounter profile IDs")]
        [Permission(MyPromoteLevel.Admin)]
        public void ListProfiles()
        {
            try
            {
                var encounterManager = GetEncounterManager();
                if (encounterManager == null)
                {
                    Context.Respond("EncounterManager not available.");
                    return;
                }

                var profiles = encounterManager.Profiles;
                if (profiles.Count == 0)
                {
                    Context.Respond("No profiles loaded.");
                    return;
                }

                var profileList = profiles.Keys.Take(20).ToList(); // Limit to prevent spam
                var response = $"Available Encounter Profiles ({profiles.Count} total):\n" + string.Join(", ", profileList);
                
                if (profiles.Count > 20)
                {
                    response += $"\n... and {profiles.Count - 20} more.";
                }

                Context.Respond(response);
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error listing profiles: {ex.Message}");
            }
        }
        
        [Command("reload", "Reloads all Encounter Profiles from disk")]
        [Permission(MyPromoteLevel.Admin)]
        public void Reload()
        {
            try
            {
                var encounterManager = GetEncounterManager();
                if (encounterManager == null)
                {
                    Context.Respond("EncounterManager not available.");
                    return;
                }

                encounterManager.ReloadProfiles();
                Context.Respond($"Reloaded {encounterManager.Profiles.Count} encounter profiles.");
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error reloading profiles: {ex.Message}");
            }
        }

        [Command("stats", "Shows AI system statistics")]
        [Permission(MyPromoteLevel.Admin)]
        public void Stats()
        {
            try
            {
                var aiManager = AiManager.Instance;
                var encounterManager = GetEncounterManager();

                if (aiManager == null && encounterManager == null)
                {
                    Context.Respond("No AI systems available.");
                    return;
                }

                var response = "HeliosAI Statistics:\n";

                if (aiManager != null)
                {
                    var aiStats = aiManager.GetStatistics();
                    response += $"NPCs: {aiStats.TotalNpcs} | Active: {aiStats.AttackingNpcs + aiStats.PatrollingNpcs}\n";
                    response += $"Aggressive: {aiStats.AggressiveNpcs} | Passive: {aiStats.PassiveNpcs}\n";
                }

                if (encounterManager != null)
                {
                    var encounterStats = encounterManager.GetStatistics();
                    response += $"Encounters: {encounterStats.ActiveEncounters}/{encounterStats.TotalEncountersSpawned}\n";
                    response += $"Profiles: {encounterStats.TotalProfilesLoaded}";
                }

                Context.Respond(response);
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error getting statistics: {ex.Message}");
            }
        }

        [Command("cleanup", "Removes all inactive NPCs and encounters")]
        [Permission(MyPromoteLevel.Admin)]
        public void Cleanup()
        {
            try
            {
                var aiManager = AiManager.Instance;
                var encounterManager = GetEncounterManager();

                var cleanedNpcs = 0;
                var cleanedEncounters = 0;

                if (aiManager != null)
                {
                    var beforeCount = aiManager.GetAllRegistered().Count;
                    
                    // Fixed: Use proper reflection check
                    var cleanupMethod = aiManager.GetType().GetMethod("Cleanup");
                    if (cleanupMethod != null)
                    {
                        cleanupMethod.Invoke(aiManager, null);
                        cleanedNpcs = beforeCount - aiManager.GetAllRegistered().Count;
                    }
                    else
                    {
                        cleanedNpcs = CleanupInvalidNpcs(aiManager);
                    }
                }

                if (encounterManager != null)
                {
                    var beforeCount = encounterManager.ActiveEncounters.Count;
                    encounterManager.CleanupEncounters();
                    cleanedEncounters = beforeCount - encounterManager.ActiveEncounters.Count;
                }

                Context.Respond($"Cleanup complete. Removed {cleanedNpcs} NPCs and {cleanedEncounters} encounters.");
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error during cleanup: {ex.Message}");
            }
        }

        // Helper method for manual NPC cleanup
        private int CleanupInvalidNpcs(AiManager aiManager)
        {
            try
            {
                var allNpcs = aiManager.GetAllRegistered();
                var toRemove = new List<IMyCubeGrid>();

                // Use reflection to handle unknown return type
                foreach (var item in (System.Collections.IEnumerable)allNpcs)
                {
                    IMyCubeGrid grid = null;
            
                    // Try different ways to extract the grid
                    if (item is IMyCubeGrid directGrid)
                    {
                        grid = directGrid;
                    }
                    else if (item != null)
                    {
                        // Try KeyValuePair pattern
                        var itemType = item.GetType();
                        var keyProperty = itemType.GetProperty("Key");
                        if (keyProperty != null && keyProperty.PropertyType == typeof(IMyCubeGrid))
                        {
                            grid = (IMyCubeGrid)keyProperty.GetValue(item);
                        }
                        else
                        {
                            // Try direct field access
                            var gridField = itemType.GetField("Grid");
                            var gridProperty = itemType.GetProperty("Grid");
                    
                            if (gridField != null)
                            {
                                grid = gridField.GetValue(item) as IMyCubeGrid;
                            }
                            else if (gridProperty != null)
                            {
                                grid = gridProperty.GetValue(item) as IMyCubeGrid;
                            }
                        }
                    }

                    // Check if grid is invalid
                    if (grid != null && (grid.MarkedForClose || grid.Physics == null))
                    {
                        toRemove.Add(grid);
                    }
                }

                // Remove invalid NPCs
                foreach (var grid in toRemove)
                {
                    aiManager.UnregisterGrid(grid);
                }

                return toRemove.Count;
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error during manual cleanup: {ex.Message}");
                return 0;
            }
        }

        // Helper method to check if a method exists
        private bool HasMethod(object obj, string methodName)
        {
            try
            {
                return obj.GetType().GetMethod(methodName) != null;
            }
            catch
            {
                return false;
            }
        }
        
        [Command("help", "Displays available HeliosAI commands")]
        [Permission(MyPromoteLevel.None)]
        public void Help()
        {
            const string msg = """
                               HeliosAI Commands:
                                       /helios spawn <prefab> – Spawn prefab at your location
                                       /helios profile <id> – Spawn an EncounterProfile by ID
                                       /helios patrol – Assign patrol behavior to last NPC
                                       /helios defensepoint [x y z radius] – Add defense point
                                       /helios list – List all EncounterProfiles
                                       /helios reload – Reload profiles from disk
                                       /helios stats – Show AI system statistics
                                       /helios cleanup – Remove inactive NPCs and encounters
                                       /helios help – Show this help message
                               """;

            Context.Respond(msg);
        }

        // Helper method to safely get EncounterManager
        private IEncounterManager GetEncounterManager()
        {
            try
            {
                // Try different possible property names and access patterns
                var plugin = HeliosAIPlugin.Instance;
                if (plugin == null)
                    return null;

                // Option 1: Try common property names
                var pluginType = plugin.GetType();
        
                // Try EncounterManager property
                var encounterManagerProperty = pluginType.GetProperty("EncounterManager");
                if (encounterManagerProperty != null)
                {
                    return encounterManagerProperty.GetValue(plugin) as IEncounterManager;
                }
        
                // Try EncounterMgr property
                var encounterMgrProperty = pluginType.GetProperty("EncounterMgr");
                if (encounterMgrProperty != null)
                {
                    return encounterMgrProperty.GetValue(plugin) as IEncounterManager;
                }
        
                // Try Encounters property
                var encountersProperty = pluginType.GetProperty("Encounters");
                if (encountersProperty != null)
                {
                    return encountersProperty.GetValue(plugin) as IEncounterManager;
                }
        
                // Try field access
                var encounterManagerField = pluginType.GetField("EncounterManager");
                if (encounterManagerField != null)
                {
                    return encounterManagerField.GetValue(plugin) as IEncounterManager;
                }
        
                return null;
            }
            catch (System.Exception ex)
            {
                Context.Respond($"Error accessing EncounterManager: {ex.Message}");
                return null;
            }
        }
    }
}