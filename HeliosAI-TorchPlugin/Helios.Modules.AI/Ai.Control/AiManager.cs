using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game;
using System.Collections.Generic;
using System.Linq;
using System;
using Helios.Core;
using Helios.Core.Interfaces;
using HeliosAI;
using Helios.Modules.AI.Behaviors;
using HeliosAI.Behaviors;
using Sandbox.Definitions;
using VRage;
using VRage.Game.Entity;
using VRage.ModAPI;
using NLog;
using System.Threading.Tasks;
using Helios.Modules.AI.Combat;
using Helios.Modules.AI.Navigation;
using VRageMath;
using System.IO;
using System.Xml.Serialization;
using Helios.Modules.API;

namespace Helios.Modules.AI
{
    public class AiManager : IAiManager, IDisposable
    {
        private static readonly Logger Logger = LogManager.GetLogger("AiManager");
        public static AiManager Instance { get; private set; }

        static AiManager()
        {
            Instance = new AiManager();
        }

        public NpcEntity LastNpc => _npcs.LastOrDefault();
        public IReadOnlyList<NpcEntity> ActiveNpcs => _npcs.AsReadOnly();
        public AdaptiveBehaviorEngine AdaptiveBehaviorEngine => _behaviorEngine;

        private List<NpcEntity> _npcs = new List<NpcEntity>();
        private List<IAiPlugin> _plugins = new List<IAiPlugin>();
        private bool _disposed = false;
        public event Action<NpcEntity> NpcSpawned;
        public event Action<NpcEntity> NpcRemoved;
        public event Action<NpcEntity, NpcEntity.AiMood> NpcMoodChanged;
        public event Action<NpcEntity, AiBehavior> NpcBehaviorChanged;

        private readonly HeliosAIConfig _config;
        private AdaptiveBehaviorEngine _behaviorEngine = new AdaptiveBehaviorEngine();
        public readonly PredictiveAnalyzer _predictiveAnalyzer = new PredictiveAnalyzer();
        
        private Dictionary<long, DateTime> _lastBehaviorUpdates = new Dictionary<long, DateTime>();

        /// <summary>
        /// Constructor with HeliosAI configuration
        /// </summary>
        public AiManager(HeliosAIConfig heliosConfig = null)
        {
            _config = heliosConfig ?? new HeliosAIConfig();
            
            if (_config.MaxSpeed <= 0) _config.MaxSpeed = 100f; 
            if (_config.ArriveDistance <= 0) _config.ArriveDistance = 50f; 
            
            _config.ConfigurationChanged += OnConfigurationChanged;
            
            Logger.Info("AiManager initialized with HeliosAI configuration and PredictiveAnalyzer");
        }

        private void OnConfigurationChanged(HeliosAIConfig newConfig)
        {
            try
            {
                Logger.Info("Applying configuration changes to AiManager");
                // Update config reference if needed
                // Apply changes that can be hot-reloaded
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to apply configuration changes");
            }
        }

        public void Update()
        {
            try
            {
                foreach (var plugin in _plugins.ToList())
                {
                    try
                    {
                        plugin.OnTick();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error ticking plugin: {plugin?.Name}");
                    }
                }

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
                        var entityId = npc.Grid.EntityId;
                        var lastUpdate = _lastBehaviorUpdates.TryGetValue(entityId, out var updateTime) ? updateTime : DateTime.MinValue;
                        
                        if (lastUpdate == DateTime.MinValue || 
                            DateTime.UtcNow.Subtract(lastUpdate).TotalSeconds > 30)
                        {
                            var context = BuildContextForNpc(npc);
                            var newBehavior = SelectOptimalBehavior(npc, context);
                            
                            if (npc.Behavior != null)
                            {
                                var success = EvaluateBehaviorSuccess(npc);
                                ReportBehaviorOutcome(npc, success, context);
                            }
                            
                            if (newBehavior != null && newBehavior.GetType() != npc.Behavior?.GetType())
                            {
                                SetBehavior(npc.Grid, newBehavior);
                            }
                            
                            _lastBehaviorUpdates[entityId] = DateTime.UtcNow;
                        }
                        
                        npc.Tick();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error ticking NPC {npc.Grid?.DisplayName}");
                        npcsToRemove.Add(npc);
                    }
                }

                foreach (var npc in npcsToRemove)
                {
                    _npcs.Remove(npc);
                    
                    if (npc?.Grid != null)
                    {
                        _lastBehaviorUpdates.Remove(npc.Grid.EntityId);
                    }
                    
                    NpcRemoved?.Invoke(npc);
                    npc?.Dispose();
                    Logger.Debug($"Removed invalid NPC entity");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Critical error in AiManager.Update()");
            }
        }

        public Dictionary<string, float> BuildContextForNpc(NpcEntity npc)
        {
            return npc != null ? npc.BuildIntelligentContext() : new Dictionary<string, float>();
        }

        private float CalculatePositionStability(NpcEntity npc)
        {
            try
            {
                var velocity = npc.Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                var speed = velocity.Length();
                
                if (speed < 0.1) return 1.0f; 
                if (speed > 100) return 0.3f; 
                
                return Math.Max(0.1f, 1.0f - (float)(speed / 100.0)); // Normalize to 0.1-1.0 range
            }
            catch
            {
                return 0.5f; // Default stability
            }
        }

        private bool EvaluateBehaviorSuccess(NpcEntity npc)
        {
            try
            {
                var baseSuccess = npc.Behavior switch
                {
                    AttackBehavior => EvaluateAttackSuccess(npc),
                    PatrolBehavior => EvaluatePatrolSuccess(npc),
                    RetreatBehavior => EvaluateRetreatSuccess(npc),
                    IdleBehavior => true, // Idle is always successful
                    _ => false
                };

                try
                {
                    var position = npc.Grid.PositionComp.GetPosition();
                    var effectiveness = _predictiveAnalyzer.PredictBehaviorEffectiveness(
                        npc.Grid.EntityId,
                        npc.Behavior?.GetType().Name ?? "Unknown",
                        new Dictionary<string, object>
                        {
                            ["Position"] = position,
                            ["Mood"] = npc.Mood.ToString(),
                            ["Health"] = GetGridHealthRatio(npc.Grid),
                            ["HasTarget"] = FindTarget(position, 2000, npc.Mood, npc.Grid.BigOwners.FirstOrDefault()) != null
                        }
                    );
                    
                    return baseSuccess && effectiveness > 0.5f;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error predicting behavior effectiveness");
                    return baseSuccess;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluateAttackSuccess(NpcEntity npc)
        {
            try
            {
                var position = npc.Grid.PositionComp.GetPosition();
                var target = FindTarget(position, 2000, npc.Mood, npc.Grid.BigOwners.FirstOrDefault());
                
                if (target == null) return false;
                
                var distanceToTarget = Vector3D.Distance(position, target.GetPosition());
                return distanceToTarget < 1500;
            }
            catch
            {
                return false;
            }
        }

        private bool EvaluatePatrolSuccess(NpcEntity npc)
        {
            try
            {
                var velocity = npc.Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                return velocity.Length() > 0.1; 
            }
            catch
            {
                return true; 
            }
        }

        private bool EvaluateRetreatSuccess(NpcEntity npc)
        {
            try
            {
                var position = npc.Grid.PositionComp.GetPosition();
                var nearestThreat = FindTarget(position, 1000, npc.Mood, npc.Grid.BigOwners.FirstOrDefault());
                
                return nearestThreat == null; 
            }
            catch
            {
                return true; 
            }
        }

        private float GetGridHealthRatio(IMyCubeGrid grid)
        {
            try
            {
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);
                if (blocks.Count == 0) return 0f;

                var currentIntegrity = blocks.Sum(b => b.Integrity);
                var maxIntegrity = blocks.Sum(b => b.MaxIntegrity);

                return maxIntegrity > 0 ? currentIntegrity / maxIntegrity : 0f;
            }
            catch
            {
                return 1.0f; 
            }
        }

        public void RegisterPlugin(IAiPlugin plugin)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AiManager));

            try
            {
                if (plugin == null)
                {
                    Logger.Warn("Attempted to register null plugin");
                    return;
                }

                if (_plugins.Count >= 20)
                {
                    Logger.Warn($"Maximum plugin limit reached: 20");
                    return;
                }

                if (!_plugins.Contains(plugin))
                {
                    _plugins.Add(plugin);
                    plugin.Initialize(this);
                    Logger.Info($"Registered AI plugin: {plugin.Name}");
                }
                else
                {
                    Logger.Debug($"Plugin {plugin.Name} already registered");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to register plugin: {plugin?.Name}");
            }
        }

        public async Task SpawnNpcAsync(Vector3D position, string prefab, NpcEntity.AiMood mood)
        {
            try
            {
                await Task.Run(() => SpawnNpc(position, prefab, mood));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to spawn NPC async: {prefab}");
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
                        
                        NpcSpawned?.Invoke(npc);
                        
                        Logger.Info($"Spawned NPC: {prefab} at {position} with mood {mood}");
                        
                        try
                        {
                            _predictiveAnalyzer.RecordEvent(grid.EntityId, "Spawned", new Dictionary<string, object>
                            {
                                ["Position"] = position,
                                ["Prefab"] = prefab,
                                ["Mood"] = mood.ToString(),
                                ["SpawnTime"] = DateTime.UtcNow
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error recording spawn event for predictive analyzer");
                        }
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

                        weaponCount += grid.GetFatBlocks().Count(b => 
                            b?.DefinitionDisplayNameText?.Contains("Turret") == true);

                        var wcapi = APIManager.WeaponCore;
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
                return _npcs.AsReadOnly(); 
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting all registered NPCs");
                return new List<NpcEntity>().AsReadOnly(); 
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
                
                try
                {
                    _predictiveAnalyzer.RecordEvent(grid.EntityId, "Registered", new Dictionary<string, object>
                    {
                        ["Position"] = grid.PositionComp.GetPosition(),
                        ["GridName"] = grid.DisplayName,
                        ["InitialBehavior"] = initialBehavior?.GetType().Name ?? "None",
                        ["RegistrationTime"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error recording registration event for predictive analyzer");
                }
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
                    _lastBehaviorUpdates.Remove(grid.EntityId);
                    NpcRemoved?.Invoke(npc);
                    
                    npc.Dispose();
                    Logger.Info($"Unregistered grid: {grid?.DisplayName}");
                    
                    try
                    {
                        _predictiveAnalyzer.RecordEvent(grid.EntityId, "Unregistered", new Dictionary<string, object>
                        {
                            ["Position"] = grid.PositionComp.GetPosition(),
                            ["GridName"] = grid.DisplayName,
                            ["UnregistrationTime"] = DateTime.UtcNow
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error recording unregistration event for predictive analyzer");
                    }
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
            Update();
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
                    _behaviorEngine.RegisterBehavior(behavior.GetType().Name);
                    
                    var oldBehavior = npc.Behavior;
                    npc.SetBehavior(behavior);
                    
                    if (oldBehavior != behavior)
                    {
                        NpcBehaviorChanged?.Invoke(npc, behavior);
                    }
                    
                    Logger.Debug($"Set behavior for {grid?.DisplayName}: {behavior?.GetType().Name}");
                    
                    try
                    {
                        _predictiveAnalyzer.RecordEvent(grid.EntityId, "BehaviorChanged", new Dictionary<string, object>
                        {
                            ["Position"] = grid.PositionComp.GetPosition(),
                            ["OldBehavior"] = oldBehavior?.GetType().Name ?? "None",
                            ["NewBehavior"] = behavior.GetType().Name,
                            ["ChangeTime"] = DateTime.UtcNow,
                            ["Mood"] = npc.Mood.ToString()
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error recording behavior change event for predictive analyzer");
                    }
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
        
        public AiBehavior SelectOptimalBehavior(NpcEntity npc, Dictionary<string, float> context)
        {
            try
            {
                var availableBehaviors = new List<string> 
                { 
                    "AttackBehavior", 
                    "PatrolBehavior", 
                    "IdleBehavior", 
                    "RetreatBehavior" 
                };
                
                try
                {
                    var behaviorPredictions = _predictiveAnalyzer.PredictOptimalBehavior(
                        npc.Grid.EntityId, 
                        context.ToDictionary(k => k.Key, v => (object)v.Value)
                    );
                    
                    foreach (var prediction in behaviorPredictions)
                    {
                        var predictionKey = $"Prediction_{prediction.Key}";
                        if (context.ContainsKey(predictionKey))
                            context[predictionKey] = prediction.Value;
                        else
                            context.Add(predictionKey, prediction.Value);
                    }
                    
                    Logger.Debug($"Applied {behaviorPredictions.Count} behavior predictions for NPC {npc.Grid.DisplayName}");
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error getting behavior predictions");
                }
                
                var selected = _behaviorEngine.SelectOptimalBehavior(
                    npc.Grid.EntityId, 
                    context, 
                    availableBehaviors
                );
                
                var gridPosition = npc.Grid.PositionComp.GetPosition();
                
                AiBehavior newBehavior = selected switch
                {
                    "AttackBehavior" => new AttackBehavior(npc.Grid, FindTarget(gridPosition, 2000, npc.Mood, npc.Grid.BigOwners.FirstOrDefault())),
                    "PatrolBehavior" => new PatrolBehavior(npc.Grid, GeneratePatrolPoints(gridPosition)),
                    "RetreatBehavior" => new RetreatBehavior(npc.Grid, FindNearestPlayer(gridPosition, 5000)),
                    _ => new IdleBehavior(npc.Grid)
                };
                
                try
                {
                    _predictiveAnalyzer.RecordEvent(npc.Grid.EntityId, "BehaviorSelected", new Dictionary<string, object>
                    {
                        ["Position"] = gridPosition,
                        ["SelectedBehavior"] = selected,
                        ["Context"] = context.ToDictionary(k => k.Key, v => (object)v.Value),
                        ["SelectionTime"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error recording behavior selection for predictive analyzer");
                }
                
                return newBehavior;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error selecting optimal behavior");
                return new IdleBehavior(npc.Grid);
            }
        }
        
        private List<Vector3D> GeneratePatrolPoints(Vector3D center)
        {
            return new List<Vector3D>
            {
                center + Vector3D.Forward * 1000,
                center + Vector3D.Right * 1000,
                center + Vector3D.Backward * 1000,
                center + Vector3D.Left * 1000
            };
        }
        
        public void ReportBehaviorOutcome(NpcEntity npc, bool success, Dictionary<string, float> context)
        {
            try
            {
                if (npc?.Behavior != null)
                {
                    _behaviorEngine.ReportBehaviorOutcome(
                        npc.Grid.EntityId,
                        npc.Behavior.GetType().Name,
                        success,
                        context
                    );
                    
                    try
                    {
                        _predictiveAnalyzer.RecordEvent(npc.Grid.EntityId, "BehaviorOutcome", new Dictionary<string, object>
                        {
                            ["Position"] = npc.Grid.PositionComp.GetPosition(),
                            ["Behavior"] = npc.Behavior.GetType().Name,
                            ["Success"] = success,
                            ["Context"] = context.ToDictionary(k => k.Key, v => (object)v.Value),
                            ["OutcomeTime"] = DateTime.UtcNow
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Error recording behavior outcome for predictive analyzer");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error reporting behavior outcome");
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
                    
                    if (npc?.Grid != null)
                    {
                        _lastBehaviorUpdates.Remove(npc.Grid.EntityId);
                    }
                    
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
                    Vector3D.Distance(n.Grid.PositionComp.GetPosition(), position) <= range
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
        
                stats.AttackingNpcs = _npcs.Count(n => n.Behavior is AttackBehavior);
                stats.PatrollingNpcs = _npcs.Count(n => n.Behavior is PatrolBehavior);
                stats.IdleNpcs = _npcs.Count(n => n.Behavior is IdleBehavior);
                stats.RetratingNpcs = _npcs.Count(n => n.Behavior is RetreatBehavior);
        
                var totalHealth = 0f;
                var healthCount = 0;
        
                foreach (var npc in _npcs)
                {
                    try
                    {
                        if (npc?.Grid != null && !npc.Grid.MarkedForClose)
                        {
                            var healthRatio = GetGridHealthRatio(npc.Grid);
                            totalHealth += healthRatio;
                            healthCount++;
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
                            SetNpcMood(npc, mood);
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

        public void SetNpcMood(NpcEntity npc, NpcEntity.AiMood mood)
        {
            try
            {
                if (npc?.Grid != null && !npc.Grid.MarkedForClose)
                {
                    var oldMood = npc.Mood;
                    npc.Mood = mood;
                    
                    if (oldMood != mood)
                    {
                        NpcMoodChanged?.Invoke(npc, mood);
                        
                        try
                        {
                            _predictiveAnalyzer.RecordEvent(npc.Grid.EntityId, "MoodChanged", new Dictionary<string, object>
                            {
                                ["Position"] = npc.Grid.PositionComp.GetPosition(),
                                ["OldMood"] = oldMood.ToString(),
                                ["NewMood"] = mood.ToString(),
                                ["ChangeTime"] = DateTime.UtcNow,
                                ["CurrentBehavior"] = npc.Behavior?.GetType().Name ?? "None"
                            });
                        }
                        catch (Exception ex)
                        {
                            Logger.Error(ex, "Error recording mood change for predictive analyzer");
                        }
                    }
                    
                    Logger.Debug($"Set mood for {npc.Grid?.DisplayName}: {mood}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to set mood for NPC: {npc?.Grid?.DisplayName}");
            }
        }

        public void UpdateNavigation(NpcEntity npc, Vector3D targetPosition)
        {
            if (npc?.Grid == null) return;
            
            try
            {
                NavigationService.Instance.Steer(
                    npc.Grid,
                    targetPosition,
                    _config.MaxSpeed,
                    _config.ArriveDistance
                );
                
                try
                {
                    _predictiveAnalyzer.RecordEvent(npc.Grid.EntityId, "NavigationUpdate", new Dictionary<string, object>
                    {
                        ["CurrentPosition"] = npc.Grid.PositionComp.GetPosition(),
                        ["TargetPosition"] = targetPosition,
                        ["MaxSpeed"] = _config.MaxSpeed,
                        ["ArriveDistance"] = _config.ArriveDistance,
                        ["UpdateTime"] = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error recording navigation update for predictive analyzer");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to update navigation for {npc.Grid.DisplayName}");
            }
        }

        #region IDisposable Implementation

        /// <summary>
        /// Disposes the AiManager and releases all resources
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                Logger.Info("Disposing AiManager...");

                if (_config != null)
                {
                    _config.ConfigurationChanged -= OnConfigurationChanged;
                }

                foreach (var npc in _npcs.ToList())
                {
                    try
                    {
                        npc?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error disposing NPC: {npc?.Grid?.DisplayName}");
                    }
                }

                foreach (var plugin in _plugins.ToList())
                {
                    try
                    {
                        plugin?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Error disposing plugin: {plugin?.Name}");
                    }
                }

                try
                {
                    _predictiveAnalyzer?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error disposing predictive analyzer");
                }

                _npcs.Clear();
                _plugins.Clear();
                _lastBehaviorUpdates.Clear();

                NpcSpawned = null;
                NpcRemoved = null;
                NpcMoodChanged = null;
                NpcBehaviorChanged = null;

                Logger.Info("AiManager disposed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error disposing AiManager");
            }
            finally
            {
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer to ensure resources are cleaned up
        /// </summary>
        ~AiManager()
        {
            Dispose();
        }

        #endregion

        public void SetGridOwnershipToNpcFaction(IMyCubeGrid grid, string factionTag = "SPRT")
        {
            var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
            if (faction == null)
            {
                Logger.Warn($"Faction '{factionTag}' not found.");
                return;
            }

            var npcOwnerId = faction.FounderId;
            if (npcOwnerId == 0 && faction.Members.Count > 0)
                npcOwnerId = faction.Members.Keys.First();

            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);

            foreach (var block in blocks)
            {
                var fatBlock = block.FatBlock as Sandbox.Game.Entities.MyCubeBlock;
                if (fatBlock != null && fatBlock.OwnerId != npcOwnerId)
                {
                    fatBlock.ChangeOwner(npcOwnerId, MyOwnershipShareModeEnum.Faction);
                }
            }

            grid.ChangeGridOwnership(npcOwnerId, MyOwnershipShareModeEnum.Faction);

            Logger.Info($"Changed ownership of grid '{grid.DisplayName}' to faction '{factionTag}' (ownerId: {npcOwnerId})");
        }

        public void SaveNpcStates(string path)
        {
            try
            {
                var states = _npcs.Select(npc => new NpcState
                {
                    GridEntityId = npc.Grid.EntityId,
                    BehaviorType = npc.Behavior?.GetType().Name,
                    Position = npc.Grid.GetPosition()
                    // Add more fields as needed or wanted
                }).ToList();

                using (var stream = File.Create(path))
                {
                    var serializer = new XmlSerializer(typeof(List<NpcState>));
                    serializer.Serialize(stream, states);
                }
                Logger.Info($"Saved {states.Count} NPC states.");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to save NPC states.");
            }
        }

        public void LoadNpcStates(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Logger.Info("No saved NPC state file found.");
                    return;
                }

                using var stream = File.OpenRead(path);
                var serializer = new XmlSerializer(typeof(List<NpcState>));
                var states = (List<NpcState>)serializer.Deserialize(stream);

                Logger.Info($"Loading {states.Count} saved NPC states...");
                foreach (var state in states)
                {
                    var grid = MyAPIGateway.Entities.GetEntityById(state.GridEntityId) as IMyCubeGrid;
                    if (grid != null)
                    {
                        var behavior = CreateBehaviorForGrid(grid, state.BehaviorType?.ToLower() ?? "idle");
                        RegisterGrid(grid, behavior);
                        // Optionally restore position, waypoints, etc.
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load NPC states.");
            }
        }

        private AiBehavior CreateBehaviorForGrid(IMyCubeGrid grid, string behaviorType)
        {
            switch (behaviorType)
            {
                case "attackbehavior":
                    return new AttackBehavior(grid, null);
                case "patrolbehavior":
                    return new PatrolBehavior(grid, new List<VRageMath.Vector3D>());
                case "retreatbehavior":
                    return new RetreatBehavior(grid, null);
                default:
                    return new IdleBehavior(grid);
            }
        }

        public NpcEntity GetNpcEntity(IMyCubeGrid grid)
        {
            return _npcs.FirstOrDefault(n => n.Grid == grid);
        }

        public string SelectAdaptiveBehavior(IMyCubeGrid grid)
        {
            var npc = GetNpcEntity(grid);
            if (npc == null)
                return "idle";
            var context = BuildContextForNpc(npc);
            var availableBehaviors = new List<string> { "AttackBehavior", "PatrolBehavior", "IdleBehavior", "RetreatBehavior" };
            var bestBehaviorId = AdaptiveBehaviorEngine.SelectOptimalBehavior(grid.EntityId, context, availableBehaviors);
            return bestBehaviorId?.Replace("Behavior", "").ToLower() ?? "idle";
        }

        public void RegisterNpc(NpcEntity npc) {
            if (npc == null) return;
            if (!_npcs.Contains(npc)) {
                _npcs.Add(npc);
                NpcSpawned?.Invoke(npc);
                Logger.Info($"Registered NPC entity: {npc.Grid?.DisplayName}");
            }
        }
    }
}