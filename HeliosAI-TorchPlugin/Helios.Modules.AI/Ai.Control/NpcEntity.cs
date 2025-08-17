using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Modules.AI.Behaviors;
using Helios.Modules.AICommunication;
using Helios.Modules.AI.Combat;
using Helios.Modules.API; // Add for PredictiveAnalyzer
using HeliosAI;
using VRage.Game.ModAPI;
using HeliosAI.Behaviors;
using NLog;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Helios.Modules.AI
{
    public class NpcEntity
    {
        public IMyCubeGrid Grid { get; private set; }
        private static readonly Logger Logger = LogManager.GetLogger("NpcEntity");
        public AiMood Mood { get; set; } = AiMood.Aggressive;
        public DateTime LastBehaviorUpdate { get; set; }
        public AiBehavior Behavior { get; set; }
        public Vector3D Position => ((VRage.Game.ModAPI.Ingame.IMyEntity)Grid).GetPosition();
        
        public AiBehavior PatrolFallback { get; set; }
        public List<Vector3D> Waypoints { get; set; } = new List<Vector3D>();
        public bool HasWarned { get; set; } = false;
        public bool EngagedRecently { get; set; } = false;
        public bool CalledReinforcements { get; set; } = false;
        public bool NeedsHelp { get; set; } = false;
        public long Id { get; set; }
        public string NationTag { get; set; }
        private float _initialHealth;
        private float _lastHealth;
        private const float RetreatHealthThreshold = 0.5f;
        public bool RadarEnabled { get; set; } = true;
        public string SpawnedPrefab { get; set; }
        private PredictiveAnalyzer _predictiveAnalyzer;
        private AdaptiveBehaviorEngine _behaviorEngine;
        private Dictionary<string, float> _lastContext = new Dictionary<string, float>();
        private DateTime _lastIntelligentEvaluation = DateTime.MinValue;
        private IMyEntity _lastKnownTarget;
        private Vector3D _lastTargetPosition;
        private DateTime _lastTargetSeen = DateTime.MinValue;
        
        private static AiCommunicationManager _commsManager = new AiCommunicationManager();

        public NpcEntity(IMyCubeGrid grid, AiMood initialMood)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            Mood = initialMood;

            _predictiveAnalyzer = new PredictiveAnalyzer();
            _behaviorEngine = new AdaptiveBehaviorEngine();

            _behaviorEngine.RegisterBehavior("AttackBehavior");
            _behaviorEngine.RegisterBehavior("PatrolBehavior");
            _behaviorEngine.RegisterBehavior("IdleBehavior");
            _behaviorEngine.RegisterBehavior("RetreatBehavior");
            _behaviorEngine.RegisterBehavior("DefenseBehavior");

            switch (initialMood)
            {
                case AiMood.Aggressive:
                    Behavior = new AttackBehavior(Grid, null);
                    break;
                case AiMood.Passive:
                    Behavior = new IdleBehavior(Grid);
                    break;
                case AiMood.Guard:
                    var waypoints = GetGuardWaypoints();
                    Behavior = new PatrolBehavior(Grid, waypoints);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(initialMood), initialMood, null);
            }

            if (Behavior != null && _commsManager != null)
                _commsManager.RegisterAgent(Behavior);
                
            InitializeHealth();

            RecordEvent("EntitySpawned", new Dictionary<string, object>
            {
                ["InitialMood"] = initialMood.ToString(),
                ["InitialBehavior"] = Behavior.GetType().Name,
                ["SpawnPosition"] = Position,
                ["GridName"] = Grid.DisplayName
            });

            Logger.Info($"NpcEntity created with adaptive AI: {Grid.DisplayName}");
        }

        public NpcEntity(IMyCubeGrid grid)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            
            _predictiveAnalyzer = new PredictiveAnalyzer();
            _behaviorEngine = new AdaptiveBehaviorEngine();
            
            InitializeHealth();
        }

        public void Tick()
        {
            if (Grid == null || Grid.MarkedForClose)
                return;

            try
            {
                var currentTime = DateTime.UtcNow;

                _predictiveAnalyzer?.UpdateMovementHistory(Grid);

                if (_lastIntelligentEvaluation == DateTime.MinValue || 
                    currentTime.Subtract(_lastIntelligentEvaluation).TotalSeconds > 15)
                {
                    PerformIntelligentEvaluation(currentTime);
                    _lastIntelligentEvaluation = currentTime;
                }

                var currentHealth = GetGridHealth();
                var healthRatio = _initialHealth > 0 ? currentHealth / _initialHealth : 1.0f;
                
                if (healthRatio < RetreatHealthThreshold && !(Behavior is RetreatBehavior))
                {
                    var retreatPosition = PredictBestRetreatPosition();
    
                    var nearestThreat = FindNearbyThreats(1500).FirstOrDefault();
    
                    Behavior = new RetreatBehavior(Grid, nearestThreat);
                    Logger.Info($"[{Grid.DisplayName}] Intelligent retreat initiated: health {healthRatio:P0}");

                    RecordEvent("RetreatDecision", new Dictionary<string, object>
                    {
                        ["HealthRatio"] = healthRatio,
                        ["RetreatPosition"] = retreatPosition,
                        ["ThreatLevel"] = CalculateThreatLevel(),
                        ["ThreatTarget"] = nearestThreat?.DisplayName ?? "None"
                    });

                    if (_commsManager != null)
                    {
                        _commsManager.RegisterAgent(Behavior);
                        _commsManager.RequestBackup(Behavior, Position);
                    }
                }

                switch (Behavior)
                {
                    case DefenseBehavior defense:
                        HandleDefenseBehaviorIntelligently(defense);
                        break;
                        
                    case AttackBehavior attack when attack.TargetInvalid():
                        HandleLostTargetIntelligently(attack);
                        break;
                        
                    case PatrolBehavior when Mood != AiMood.Passive:
                        HandlePatrolWithTargetAcquisition();
                        break;
                }

                MyLog.Default.WriteLine($"[HeliosAI] Ticking NPC {Grid.DisplayName} with behavior {Behavior?.GetType().Name}");
                Behavior?.Tick();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error in NpcEntity.Tick for {Grid?.DisplayName}");
            }
        }

        private void PerformIntelligentEvaluation(DateTime currentTime)
        {
            try
            {
                var context = BuildIntelligentContext();
                
                var availableBehaviors = new List<string> { "AttackBehavior", "PatrolBehavior", "IdleBehavior", "RetreatBehavior", "DefenseBehavior" };
                var recommendedBehavior = _behaviorEngine.SelectOptimalBehavior(Grid.EntityId, context, availableBehaviors);

                if (Behavior != null)
                {
                    var success = EvaluateBehaviorSuccess();
                    _behaviorEngine.ReportBehaviorOutcome(Grid.EntityId, Behavior.GetType().Name, success, context);
                    
                    RecordEvent("BehaviorEvaluation", new Dictionary<string, object>
                    {
                        ["CurrentBehavior"] = Behavior.GetType().Name,
                        ["Success"] = success,
                        ["Context"] = context.ToDictionary(k => k.Key, v => (object)v.Value)
                    });
                }

                if (recommendedBehavior != Behavior?.GetType().Name)
                {
                    var newBehavior = CreateBehaviorFromName(recommendedBehavior);
                    if (newBehavior != null)
                    {
                        SetBehavior(newBehavior);
                        Logger.Info($"[{Grid.DisplayName}] Intelligent behavior change: {recommendedBehavior}");
                    }
                }

                _lastContext = context;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error in intelligent evaluation for {Grid?.DisplayName}");
            }
        }

        internal Dictionary<string, float> BuildIntelligentContext()
        {
            var context = new Dictionary<string, float>();

            try
            {
                var healthRatio = _initialHealth > 0 ? GetGridHealth() / _initialHealth : 1.0f;
                context["HealthRatio"] = healthRatio;
                context["LowHealth"] = healthRatio < 0.3f ? 1.0f : 0.0f;

                var target = FindCurrentTarget();
                if (target != null)
                {
                    var distance = Vector3D.Distance(Position, target.GetPosition());
                    context["HasTarget"] = 1.0f;
                    context["TargetDistance"] = (float)distance;
                    context["TargetClose"] = distance < 1000 ? 1.0f : 0.0f;
                    
                    _lastKnownTarget = target;
                    _lastTargetPosition = target.GetPosition();
                    _lastTargetSeen = DateTime.UtcNow;
                }
                else
                {
                    context["HasTarget"] = 0.0f;
                    context["TargetDistance"] = float.MaxValue;
                    context["TargetClose"] = 0.0f;
                    
                    if (_lastTargetSeen != DateTime.MinValue && 
                        DateTime.UtcNow.Subtract(_lastTargetSeen).TotalSeconds < 30)
                    {
                        context["RecentTargetLoss"] = 1.0f;
                    }
                }

                // Threat assessment
                context["ThreatLevel"] = CalculateThreatLevel();

                // Mood influence
                context["AggressiveMood"] = Mood == AiMood.Aggressive ? 1.0f : 0.0f;
                context["PassiveMood"] = Mood == AiMood.Passive ? 1.0f : 0.0f;
                context["GuardMood"] = Mood == AiMood.Guard ? 1.0f : 0.0f;

                // Temporal context
                context["TimeOfDay"] = (float)(DateTime.UtcNow.Hour / 24.0);

                // Movement context
                var velocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
                context["Speed"] = (float)velocity.Length();
                context["IsMoving"] = velocity.Length() > 1.0 ? 1.0f : 0.0f;

                // Communication context
                context["NeedsHelp"] = NeedsHelp ? 1.0f : 0.0f;
                context["CalledReinforcements"] = CalledReinforcements ? 1.0f : 0.0f;

            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error building intelligent context");
            }

            return context;
        }

        private float CalculateThreatLevel()
        {
            try
            {
                var threatLevel = 0.0f;
                var scanRange = 2000.0;

                var hostiles = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(hostiles, entity =>
                {
                    if (entity == null || entity.MarkedForClose) return false;
                    
                    var distance = Vector3D.Distance(Position, entity.GetPosition());
                    if (distance > scanRange) return false;

                    if (entity is IMyCharacter character)
                    {
                        var playerId = character.ControllerInfo?.ControllingIdentityId ?? 0;
                        return IsHostilePlayer(playerId);
                    }
                    else if (entity is IMyCubeGrid grid)
                    {
                        var gridOwner = grid.BigOwners.FirstOrDefault();
                        return IsHostilePlayer(gridOwner);
                    }

                    return false;
                });

                foreach (var hostile in hostiles)
                {
                    var distance = Vector3D.Distance(Position, hostile.GetPosition());
                    var proximityThreat = Math.Max(0, 1.0f - (float)(distance / scanRange));
                    
                    if (hostile is IMyCharacter)
                        threatLevel += proximityThreat * 0.3f; 
                    else if (hostile is IMyCubeGrid)
                        threatLevel += proximityThreat * 0.7f; 
                }

                return MathHelper.Clamp(threatLevel, 0.0f, 2.0f);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating threat level");
                return 0.5f; 
            }
        }

        private bool IsHostilePlayer(long playerId)
        {
            if (playerId == 0) return false;
            
            try
            {
                var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                var ownFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(Grid.BigOwners.FirstOrDefault());
                
                if (playerFaction == null || ownFaction == null) return true; // Unknown = hostile
                
                return playerFaction.FactionId != ownFaction.FactionId;
            }
            catch
            {
                return true; // Error = assume hostile
            }
        }

        private IMyEntity FindCurrentTarget()
        {
            try
            {
                if (Behavior is AttackBehavior attack && attack.Target != null && !attack.Target.MarkedForClose)
                {
                    return attack.Target;
                }

                var aiManager = AiManager.Instance;
                if (aiManager != null)
                {
                    return aiManager.FindTarget(Position, 2000, Mood, Grid.BigOwners.FirstOrDefault());
                }

                return null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding current target");
                return null;
            }
        }

        private Vector3D PredictBestRetreatPosition()
        {
            try
            {
                var currentPos = Position;
                var threatDirection = Vector3D.Zero;

                var threats = FindNearbyThreats(1500);
                if (threats.Any())
                {
                    threatDirection = threats.Aggregate(Vector3D.Zero, (sum, threat) => 
                        sum + Vector3D.Normalize(threat.GetPosition() - currentPos)) / threats.Count;
                }

                // Retreat opposite to threat direction
                var retreatDirection = threatDirection.Length() > 0 ? -Vector3D.Normalize(threatDirection) : Vector3D.Forward;
                var retreatDistance = 2000; // 2km retreat

                return currentPos + retreatDirection * retreatDistance;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error predicting retreat position");
                return Position + Vector3D.Forward * 1000; 
            }
        }

        private List<IMyEntity> FindNearbyThreats(double range)
        {
            var threats = new List<IMyEntity>();
            
            try
            {
                var entities = new HashSet<IMyEntity>();
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose) return false;
                    var distance = Vector3D.Distance(Position, entity.GetPosition());
                    return distance <= range;
                });

                foreach (var entity in entities)
                {
                    if (entity is IMyCharacter character)
                    {
                        var playerId = character.ControllerInfo?.ControllingIdentityId ?? 0;
                        if (IsHostilePlayer(playerId)) threats.Add(entity);
                    }
                    else if (entity is IMyCubeGrid grid)
                    {
                        var gridOwner = grid.BigOwners.FirstOrDefault();
                        if (IsHostilePlayer(gridOwner)) threats.Add(entity);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error finding nearby threats");
            }

            return threats;
        }

        private bool EvaluateBehaviorSuccess()
        {
            try
            {
                return Behavior switch
                {
                    AttackBehavior attack => EvaluateAttackSuccess(attack),
                    PatrolBehavior patrol => EvaluatePatrolSuccess(patrol),
                    DefenseBehavior defense => EvaluateDefenseSuccess(defense),
                    RetreatBehavior retreat => EvaluateRetreatSuccess(retreat),
                    IdleBehavior => true, // Idle is always successful
                    _ => false
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error evaluating behavior success");
                return false;
            }
        }

        private bool EvaluateAttackSuccess(AttackBehavior attack)
        {
            if (attack.Target == null || attack.Target.MarkedForClose) return false;
            
            var distance = Vector3D.Distance(Position, attack.Target.GetPosition());
            return distance < 1500; // Success if within engagement range
        }

        private bool EvaluatePatrolSuccess(PatrolBehavior patrol)
        {
            var velocity = Grid.Physics?.LinearVelocity ?? Vector3D.Zero;
            return velocity.Length() > 0.1;
        }

        private bool EvaluateDefenseSuccess(DefenseBehavior defense)
        {
            var threatsInZone = FindNearbyThreats(defense.DefenseRadius);
            return threatsInZone.Count == 0;
        }

        private bool EvaluateRetreatSuccess(RetreatBehavior retreat)
        {
            var threats = FindNearbyThreats(1000);
            return threats.Count == 0;
        }

        private AiBehavior CreateBehaviorFromName(string behaviorName)
        {
            try
            {
                return behaviorName switch
                {
                    "AttackBehavior" => new AttackBehavior(Grid, FindCurrentTarget()),
                    "PatrolBehavior" => new PatrolBehavior(Grid, GetGuardWaypoints()),
                    "DefenseBehavior" => new DefenseBehavior(Grid, Position, 1000),
                    "RetreatBehavior" => new RetreatBehavior(Grid, FindNearbyThreats(1500).FirstOrDefault()), 
                    "IdleBehavior" => new IdleBehavior(Grid),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error creating behavior: {behaviorName}");
                return null;
            }
        }

        private void HandleDefenseBehaviorIntelligently(DefenseBehavior defense)
        {
            try
            {
                var wc = APIManager.WeaponCoreManager;
                if (wc != null)
                {
                    wc.RegisterWeapons(Grid);
                    var target = wc.GetPriorityTarget(defense.DefensePosition, defense.DefenseRadius);
                    if (target != null)
                    {
                        // Use predictive analysis for target engagement
                        var predictedPosition = _predictiveAnalyzer?.PredictEnemyPosition(target, 2.0f) ?? target.GetPosition();
                        
                        Logger.Info($"[{Grid.DisplayName}] Intelligent defense: engaging {target.DisplayName}");
                        Behavior = new AttackBehavior(Grid, target);
                        
                        RecordEvent("DefenseEngagement", new Dictionary<string, object>
                        {
                            ["TargetId"] = target.EntityId,
                            ["PredictedPosition"] = predictedPosition,
                            ["EngagementRange"] = Vector3D.Distance(Position, target.GetPosition())
                        });
                    }
                    else
                    {
                        defense.Tick();
                    }
                }
                else
                {
                    defense.Tick();
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent defense handling");
                defense?.Tick(); // Fallback
            }
        }

        private void HandleLostTargetIntelligently(AttackBehavior attack)
        {
            try
            {
                Logger.Info($"[{Grid.DisplayName}] Intelligent target loss handling");
                
                if (_lastKnownTarget != null && _predictiveAnalyzer != null)
                {
                    var predictedPosition = _predictiveAnalyzer.PredictEnemyPosition(_lastKnownTarget, 5.0f);
                    var searchDistance = Vector3D.Distance(Position, predictedPosition);
                    
                    if (searchDistance < 3000) // Within reasonable search range
                    {
                        var searchWaypoints = new List<Vector3D>
                        {
                            predictedPosition,
                            predictedPosition + Vector3D.Forward * 500,
                            predictedPosition + Vector3D.Right * 500,
                            predictedPosition + Vector3D.Backward * 500,
                            predictedPosition + Vector3D.Left * 500
                        };
                        
                        Behavior = new PatrolBehavior(Grid, searchWaypoints);
                        Logger.Info($"[{Grid.DisplayName}] Searching predicted target location");
                        
                        RecordEvent("TargetSearchInitiated", new Dictionary<string, object>
                        {
                            ["PredictedPosition"] = predictedPosition,
                            ["SearchDistance"] = searchDistance
                        });
                        
                        return;
                    }
                }

                if (PatrolFallback is DefenseBehavior fallbackDefense)
                {
                    Behavior = new DefenseBehavior(Grid, fallbackDefense.DefensePosition, fallbackDefense.DefenseRadius);
                }
                else
                {
                    Behavior = PatrolFallback ?? new IdleBehavior(Grid);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent target loss handling");
                Behavior = PatrolFallback ?? new IdleBehavior(Grid); // Safe fallback
            }
        }

        private void HandlePatrolWithTargetAcquisition()
        {
            try
            {
                var aiManager = AiManager.Instance;
                var target = aiManager?.FindTarget(Position, 1500, Mood, Grid.BigOwners.FirstOrDefault());
                
                if (target != null)
                {
                    var predictedPosition = _predictiveAnalyzer?.PredictEnemyPosition(target, 3.0f) ?? target.GetPosition();
                    
                    Behavior = new AttackBehavior(Grid, target);
                    Logger.Info($"[{Grid.DisplayName}] Intelligent target acquisition: {target.DisplayName}");
                    
                    RecordEvent("TargetAcquisition", new Dictionary<string, object>
                    {
                        ["TargetId"] = target.EntityId,
                        ["AcquisitionRange"] = Vector3D.Distance(Position, target.GetPosition()),
                        ["PredictedPosition"] = predictedPosition
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in patrol target acquisition");
            }
        }

        private void RecordEvent(string eventType, Dictionary<string, object> data)
        {
            try
            {
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, eventType, data);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Error recording event: {eventType}");
            }
        }

        private List<Vector3D> GetGuardWaypoints()
        {
            var pos = Position;
            return
            [
                pos + new Vector3D(0, 0, 1000),
                pos + new Vector3D(1000, 0, 0),
                pos + new Vector3D(0, 0, -1000),
                pos + new Vector3D(-1000, 0, 0)
            ];
        }

        public AiBehavior CurrentBehavior => Behavior;

        public IMyEntity Target
        {
            get
            {
                if (Behavior is AttackBehavior attack)
                    return attack.Target;
                return null;
            }
        }
        
        public void InitializeHealth()
        {
            _initialHealth = GetGridHealth();
            _lastHealth = _initialHealth;
        }
        
        public void MoveTo(Vector3D position)
        {
            try
            {
                var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                    .FirstOrDefault();

                if (remote == null)
                {
                    Logger.Warn($"[{Grid.DisplayName}] No remote control found for movement");
                    return;
                }
                
                remote.ClearWaypoints();
                remote.AddWaypoint(position, "AI_MoveTarget");
                remote.SetAutoPilotEnabled(true);
                
                RecordEvent("MovementCommand", new Dictionary<string, object>
                {
                    ["TargetPosition"] = position,
                    ["CurrentPosition"] = Position
                });
                
                Logger.Debug($"[{Grid.DisplayName}] Moving to position: {position}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Failed to execute MoveTo command");
            }
        }

        private float GetGridHealth()
        {
            try
            {
                var slimBlocks = new List<IMySlimBlock>();
                Grid.GetBlocks(slimBlocks); 

                if (slimBlocks.Count == 0) return _lastHealth;

                return slimBlocks.Sum(block => block.Integrity);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Failed to calculate grid health");
                return _lastHealth;
            }
        }

        public void SetBehaviorByMode(int mode)
        {
            AiBehavior newBehavior = null;
            try
            {
                switch (mode)
                {
                    case 0: 
                        newBehavior = new IdleBehavior(Grid); 
                        break;
                    case 1:
                        var waypoints = GetGuardWaypoints();
                        newBehavior = new PatrolBehavior(Grid, waypoints);
                        break;
                    case 2:
                        var target = AiManager.Instance?.FindNearestPlayer(Position, 2000);
                        if (target != null)
                            newBehavior = new AttackBehavior(Grid, target);
                        else
                            Logger.Warn($"[{Grid.DisplayName}] No target found for attack mode");
                        break;
                    default:
                        Logger.Warn($"[{Grid.DisplayName}] Invalid behavior mode: {mode}");
                        return;
                }
        
                if (newBehavior != null)
                {
                    SetBehavior(newBehavior);
                    
                    RecordEvent("ManualBehaviorChange", new Dictionary<string, object>
                    {
                        ["Mode"] = mode,
                        ["NewBehavior"] = newBehavior.GetType().Name
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Failed to set behavior mode {mode}");
            }
        }

        public void SetBehavior(AiBehavior behavior)
        {
            if (behavior == null) 
            {
                Logger.Warn($"[{Grid.DisplayName}] Attempted to set null behavior");
                return;
            }
            
            try
            {
                var oldBehavior = Behavior?.GetType().Name ?? "None";
                
                behavior.Npc = this;
                Behavior = behavior;
                
                if (_commsManager != null)
                    _commsManager.RegisterAgent(Behavior);
                    
                RecordEvent("BehaviorChange", new Dictionary<string, object>
                {
                    ["OldBehavior"] = oldBehavior,
                    ["NewBehavior"] = behavior.GetType().Name,
                    ["ChangeReason"] = "Manual"
                });
                    
                Logger.Debug($"[{Grid.DisplayName}] Behavior set to: {behavior.GetType().Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Failed to set behavior");
            }
        }

        public void Dispose()
        {
            try
            {
                if (Behavior != null && _commsManager != null)
                {
                    _commsManager.UnregisterAgent(Behavior);
                }
                
                _predictiveAnalyzer?.Dispose();
                _behaviorEngine = null;
                _lastContext?.Clear();
                
                Behavior = null;
                Grid = null;
                
                Logger.Debug("NpcEntity disposed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to dispose NpcEntity properly");
            }
        }

        public enum AiMood
        {
            Passive,    // Do nothing
            Guard,      // Only defend against threats
            Aggressive  // Attack any nearby enemies
        }

        public class NpcData
        {
            public string Prefab { get; set; }
            public Vector3D Position { get; set; }
            public AiMood Mood { get; set; }
        }
    }
}