using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Modules.AICommunication;
using HeliosAI;
using VRage.Game.ModAPI;
using HeliosAI.Behaviors;
using NLog;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Helios.Modules.AI
{
    public class NpcEntity
    {
        public MyCubeGrid Grid { get; private set; }
        private static readonly Logger Logger = LogManager.GetLogger("NpcEntity");
        public AiMood Mood { get; set; } = AiMood.Aggressive;
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
        
        private static AiCommunicationManager _commsManager = new AiCommunicationManager();

        public NpcEntity(IMyCubeGrid grid, AiMood initialMood)
        {
            Grid = (MyCubeGrid)grid ?? throw new ArgumentNullException(nameof(grid));
            Mood = initialMood;

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

            // Register behavior for communication
            if (Behavior != null && _commsManager != null)
                _commsManager.RegisterAgent(Behavior);
                
            InitializeHealth();
        }

        public NpcEntity(IMyCubeGrid grid)
        {
            Grid = (MyCubeGrid)grid ?? throw new ArgumentNullException(nameof(grid));
            InitializeHealth();
        }
        
        private List<Vector3D> GetGuardWaypoints()
        {
            var pos = Position; // Use the Position property
            return new List<Vector3D>
            {
                pos + new Vector3D(0, 0, 100),
                pos + new Vector3D(100, 0, 0),
                pos + new Vector3D(0, 0, -100),
                pos + new Vector3D(-100, 0, 0),
            };
        }

        public void Tick()
        {
            if (Grid == null || Grid.MarkedForClose)
                return;

            // Health monitoring and retreat
            var currentHealth = GetGridHealth();
            if (_initialHealth > 0 && currentHealth < _initialHealth * RetreatHealthThreshold)
            {
                if (Behavior is not RetreatBehavior)
                {
                    Behavior = new RetreatBehavior(Grid);
                    Logger.Info($"[{Grid.DisplayName}] Retreating: grid damaged!");

                    // Register new retreat behavior
                    if (_commsManager != null)
                    {
                        _commsManager.RegisterAgent(Behavior);
                        // Request backup from other agents
                        _commsManager.RequestBackup(Behavior, Position);
                    }
                }

                Behavior?.Tick();
                return;
            }

            switch (Behavior)
            {
                // DefenseBehavior logic
                case DefenseBehavior defense:
                {
                    var wc = HeliosAIPlugin.WeaponCoreManager;
                    if (wc != null)
                    {
                        wc.RegisterWeapons(Grid);
                        var target = wc.GetPriorityTarget(defense.DefensePosition, defense.DefenseRadius);
                        if (target != null)
                        {
                            Logger.Info($"[{Grid.DisplayName}] Hostile detected near defense zone: {target.DisplayName}");
                            Behavior = new AttackBehavior(Grid, target);
                        }
                        else
                        {
                            defense.Tick();
                        }
                    }
                    else
                    {
                        defense.Tick(); // Fallback when WeaponCore not available
                    }
                    return;
                }
        
                // Auto-return from AttackBehavior to DefenseBehavior
                case AttackBehavior attack when attack.TargetInvalid():
                {
                    Logger.Info($"[{Grid.DisplayName}] Target lost. Returning to defense.");
                    if (PatrolFallback is DefenseBehavior fallbackDefense)
                    {
                        Behavior = new DefenseBehavior(Grid, fallbackDefense.DefensePosition,
                            fallbackDefense.DefenseRadius);
                    }
                    else
                    {
                        Behavior = PatrolFallback ?? Behavior;
                    }
                    return;
                }
            }

            // Try to auto-acquire target from patrol - FIXED
            if (Mood != AiMood.Passive && Behavior is PatrolBehavior)
            {
                var target = AiManager.Instance.FindTarget(Position, 1000, Mood, Grid.BigOwners.FirstOrDefault());
                if (target != null)
                {
                    Behavior = new AttackBehavior(Grid, target);
                    Logger.Info($"[{Grid.DisplayName}] Engaging target: {target.DisplayName}");
                }
            }

            Behavior?.Tick();
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
            // Basic functionality for now - add advanced functionality later
            try
            {
                var remote = Grid.GetFatBlocks()
                    .OfType<IMyRemoteControl>()
                    .FirstOrDefault();

                if (remote == null)
                {
                    Logger.Warn($"[{Grid.DisplayName}] No remote control found for movement");
                    return;
                }
                
                remote.ClearWaypoints();
                remote.AddWaypoint(position, "AI_MoveTarget");
                remote.SetAutoPilotEnabled(true);
                
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
                var blocks = Grid.GetBlocks();
                return blocks.Sum(block => block.Integrity);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid.DisplayName}] Failed to calculate grid health");
                return _lastHealth; // Return last known health as fallback
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
                        var nearest = AiManager.Instance.FindNearestPlayer(Position, 1000);
                        if (nearest != null)
                            newBehavior = new AttackBehavior(Grid, nearest);
                        else
                            Logger.Warn($"[{Grid.DisplayName}] No target found for attack mode");
                        break;
                    default:
                        Logger.Warn($"[{Grid.DisplayName}] Invalid behavior mode: {mode}");
                        return;
                }
        
                if (newBehavior != null)
                    SetBehavior(newBehavior);
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
                behavior.Npc = this;
                Behavior = behavior;
                
                if (_commsManager != null)
                    _commsManager.RegisterAgent(Behavior);
                    
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