using System;
using System.Collections.Generic;
using HeliosAI.Behaviors;
using Helios.Modules.Nations;
using HeliosAI.Utilities;
using Helios.Modules.AI.Navigation;
using Helios.Modules.AI.Combat;
using HeliosAI;
using NLog;
using VRage.Game.ModAPI;
using VRageMath;
using Sandbox.ModAPI;
using VRage.ModAPI;

namespace Helios.Modules.AI.Agents
{
    public class NpcAgent : AiBehavior
    {
        private static readonly Logger Log = LogManager.GetLogger("NpcAgent");

        public new IMyCubeGrid Grid { get; }
        public Vector3D? CurrentWaypoint { get; private set; }
        public HeliosAIConfig Config { get; }
        public TimeSpan NextScanAt { get; private set; } = TimeSpan.Zero;
        public override string Name => Grid?.DisplayName ?? $"NPC-{Grid?.EntityId}";
        private new PredictiveAnalyzer _predictiveAnalyzer;
        public Dictionary<long, Vector3D> LastEnemyPositions { get; set; } = new Dictionary<long, Vector3D>();
        private IMyEntity _currentTarget;
        private DateTime _lastBehaviorEvaluation = DateTime.MinValue;

        public NpcAgent(IMyCubeGrid grid, HeliosAIConfig config) : base(grid)
        {
            Grid = grid;
            Nation = NationHelper.GetNation(grid);
            Config = config;
            
            _predictiveAnalyzer = new PredictiveAnalyzer();
            
            Log.Info($"NpcAgent initialized for {Grid?.DisplayName} with predictive capabilities");
        }

        public new void Tick()
        {
            if (Grid == null || Grid.MarkedForClose) return;

            try
            {
                var now = MyAPIGateway.Session?.ElapsedPlayTime ?? TimeSpan.Zero;
                var currentTime = DateTime.UtcNow;

                _predictiveAnalyzer.UpdateMovementHistory(Grid);

                if (now >= NextScanAt)
                {
                    PerformIntelligentScan(currentTime);
                    NextScanAt = now + TimeSpan.FromMilliseconds(Config.UpdateInterval);
                }

                if (CurrentWaypoint.HasValue || _currentTarget != null)
                {
                    PerformIntelligentMovement();
                }

                if (_lastBehaviorEvaluation == DateTime.MinValue || 
                    currentTime.Subtract(_lastBehaviorEvaluation).TotalSeconds > 30)
                {
                    EvaluateAndAdaptBehavior(currentTime);
                    _lastBehaviorEvaluation = currentTime;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"NpcAgent.Tick failed for {Grid?.DisplayName}");
            }
        }

        private void PerformIntelligentScan(DateTime currentTime)
        {
            try
            {
                var enemy = EntityUtils.GetClosestEnemyGrid(
                    Grid.GetPosition(), (long)Nation, Config.SpawnRange);

                if (enemy != null)
                {
                    _predictiveAnalyzer.UpdateMovementHistory(enemy);

                    var enemyPos = enemy.GetPosition();
                    LastEnemyPositions[enemy.EntityId] = enemyPos;

                    _predictiveAnalyzer.RecordEvent(Grid.EntityId, "EnemyDetected", new Dictionary<string, object>
                    {
                        ["EnemyId"] = enemy.EntityId,
                        ["EnemyPosition"] = enemyPos,
                        ["Distance"] = Vector3D.Distance(Grid.GetPosition(), enemyPos),
                        ["ScanTime"] = currentTime
                    });

                    _currentTarget = enemy;

                    var predictedPosition = _predictiveAnalyzer.PredictEnemyPosition(enemy, 5.0f);
                    CurrentWaypoint = predictedPosition;
                    
                    Log.Debug($"Target acquired: {enemy.DisplayName}, predicted position: {predictedPosition}");
                }
                else
                {
                    var aiManager = AiManager.Instance;
                    if (aiManager != null)
                    {
                        var target = aiManager.FindTarget(
                            Grid.GetPosition(), 
                            Config.SpawnRange, 
                            NpcEntity.AiMood.Aggressive, 
                            (long)Nation
                        );

                        if (target != null)
                        {
                            _currentTarget = target;
                            CurrentWaypoint = target.GetPosition();
                        }
                        else
                        {
                            _currentTarget = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in intelligent scan");
            }
        }

        private void PerformIntelligentMovement()
        {
            try
            {
                Vector3D targetPosition;

                if (_currentTarget != null && !_currentTarget.MarkedForClose)
                {
                    targetPosition = _predictiveAnalyzer.PredictEnemyPosition(_currentTarget, 3.0f);

                    var distance = Vector3D.Distance(CurrentWaypoint ?? Vector3D.Zero, targetPosition);
                    if (distance > 100)
                    {
                        CurrentWaypoint = targetPosition;
                    }
                }
                else if (CurrentWaypoint.HasValue)
                {
                    targetPosition = CurrentWaypoint.Value;
                }
                else
                {
                    return;
                }

                var combatAnalysis = _predictiveAnalyzer.AnalyzeOptimalWeapons(_currentTarget);
                var optimalRange = combatAnalysis?.OptimalRange ?? Config.ArriveDistance;

                var currentDistance = Vector3D.Distance(Grid.GetPosition(), targetPosition);
                if (currentDistance > optimalRange * 1.2)
                {
                    NavigationService.Instance.Steer(Grid, targetPosition, Config.MaxSpeed, optimalRange);
                }
                else if (currentDistance < optimalRange * 0.8)
                {
                    var direction = Vector3D.Normalize(Grid.GetPosition() - targetPosition);
                    var retreatPosition = targetPosition + direction * optimalRange;
                    NavigationService.Instance.Steer(Grid, retreatPosition, Config.MaxSpeed * 0.7f, 50f);
                }
                else
                {
                    NavigationService.Instance.Steer(Grid, targetPosition, Config.MaxSpeed * 0.5f, Config.ArriveDistance);
                }

                _predictiveAnalyzer.RecordEvent(Grid.EntityId, "MovementDecision", new Dictionary<string, object>
                {
                    ["TargetPosition"] = targetPosition,
                    ["CurrentDistance"] = currentDistance,
                    ["OptimalRange"] = optimalRange,
                    ["Decision"] = currentDistance > optimalRange * 1.2 ? "Approach" : 
                                  currentDistance < optimalRange * 0.8 ? "Retreat" : "Maintain"
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in intelligent movement");
            }
        }

        private void EvaluateAndAdaptBehavior(DateTime currentTime)
        {
            try
            {
                var context = new Dictionary<string, object>
                {
                    ["Position"] = Grid.GetPosition(),
                    ["HasTarget"] = _currentTarget != null,
                    ["TargetDistance"] = _currentTarget != null ? 
                        Vector3D.Distance(Grid.GetPosition(), _currentTarget.GetPosition()) : double.MaxValue,
                    ["GridHealth"] = CalculateGridHealth(),
                    ["Nation"] = Nation.ToString(),
                    ["EvaluationTime"] = currentTime
                };

                var aiManager = AiManager.Instance;
                if (aiManager != null)
                {
                    var npc = aiManager.GetNpc(Grid);
                    if (npc != null)
                    {
                        var contextFloat = new Dictionary<string, float>
                        {
                            ["HasTarget"] = _currentTarget != null ? 1.0f : 0.0f,
                            ["TargetDistance"] = _currentTarget != null ? 
                                (float)Vector3D.Distance(Grid.GetPosition(), _currentTarget.GetPosition()) : float.MaxValue,
                            ["GridHealth"] = CalculateGridHealth(),
                            ["TimeOfDay"] = (float)(DateTime.UtcNow.Hour / 24.0)
                        };

                        var optimalBehavior = aiManager.SelectOptimalBehavior(npc, contextFloat);

                        if (optimalBehavior != null && optimalBehavior.GetType() != npc.Behavior?.GetType())
                        {
                            aiManager.SetBehavior(Grid, optimalBehavior);
                            Log.Info($"Behavior adapted to: {optimalBehavior.GetType().Name}");
                        }
                    }
                }

                _predictiveAnalyzer.RecordEvent(Grid.EntityId, "BehaviorEvaluation", context);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in behavior evaluation");
            }
        }

        private float CalculateGridHealth()
        {
            try
            {
                if (Grid == null) return 1.0f;

                var blocks = new List<IMySlimBlock>();
                Grid.GetBlocks(blocks);

                if (blocks.Count == 0) return 1.0f;

                var totalIntegrity = 0f;
                var maxIntegrity = 0f;

                foreach (var block in blocks)
                {
                    totalIntegrity += block.Integrity;
                    maxIntegrity += block.MaxIntegrity;
                }

                return maxIntegrity > 0 ? totalIntegrity / maxIntegrity : 1.0f;
            }
            catch
            {
                return 1.0f;
            }
        }

        public new void ReceiveBackupRequest(Vector3D location, string message)
        {
            CurrentWaypoint = location;
            _currentTarget = null;

            try
            {
                _predictiveAnalyzer.RecordEvent(Grid.EntityId, "BackupRequest", new Dictionary<string, object>
                {
                    ["RequestLocation"] = location,
                    ["Message"] = message,
                    ["CurrentPosition"] = Grid.GetPosition(),
                    ["ResponseTime"] = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error recording backup request");
            }

            Log.Info($"Backup request received: {message} at {location}");
        }

        protected new void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    _predictiveAnalyzer?.Dispose();
                    LastEnemyPositions?.Clear();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error disposing NpcAgent");
                }
            }
            base.Dispose(disposing);
        }
    }
}