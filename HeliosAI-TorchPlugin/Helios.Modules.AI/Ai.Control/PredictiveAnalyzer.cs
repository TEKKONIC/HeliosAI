using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Helios.Modules.AI.Combat
{
    public static class DictionaryExtensions
    {
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
        {
            TValue value;
            return dictionary.TryGetValue(key, out value) ? value : defaultValue;
        }
    }

    public class PredictiveAnalyzer : IDisposable
    {
        private Dictionary<long, MovementHistory> _movementHistories = new Dictionary<long, MovementHistory>();
        private Dictionary<long, CombatStats> _combatStats = new Dictionary<long, CombatStats>();
        private Dictionary<long, List<EventRecord>> _eventHistories = new Dictionary<long, List<EventRecord>>();
        private bool _disposed = false;

        public class EventRecord
        {
            public string EventType { get; set; }
            public DateTime Timestamp { get; set; }
            public Dictionary<string, object> Data { get; set; } = new Dictionary<string, object>();
        }

        public class MovementHistory
        {
            public List<MovementFrame> Positions { get; set; } = new List<MovementFrame>();
            public Vector3D PredictedVelocity { get; set; }
            public Vector3D PredictedAcceleration { get; set; }
            public float PredictabilityScore { get; set; }
            
            public void AddPosition(Vector3D position, DateTime timestamp)
            {
                Positions.Add(new MovementFrame { Position = position, Timestamp = timestamp });
                if (Positions.Count > 50) // Keep last 50 positions
                    Positions.RemoveAt(0);
                
                CalculatePredictedMovement();
            }

            private void CalculatePredictedMovement()
            {
                if (Positions.Count < 3) return;

                var recent = Positions.Skip(Math.Max(0, Positions.Count - 10)).Take(10).ToList();
                var velocities = new List<Vector3D>();
    
                for (var i = 1; i < recent.Count; i++)
                {
                    var deltaTime = (recent[i].Timestamp - recent[i-1].Timestamp).TotalSeconds;
                    if (deltaTime > 0)
                    {
                        var velocity = (recent[i].Position - recent[i-1].Position) / deltaTime;
                        velocities.Add(velocity);
                    }
                }

                if (velocities.Any())
                {
                    PredictedVelocity = velocities.Aggregate((v1, v2) => v1 + v2) / velocities.Count;
        
                    if (velocities.Count > 1)
                    {
                        var accelerations = new List<Vector3D>();
                        for (var i = 1; i < velocities.Count; i++)
                        {
                            accelerations.Add(velocities[i] - velocities[i-1]);
                        }
                        PredictedAcceleration = accelerations.Aggregate((a1, a2) => a1 + a2) / accelerations.Count;
                    }

                    var variance = velocities.Select(v => (v - PredictedVelocity).LengthSquared()).Average();
                    PredictabilityScore = 1.0f / (1.0f + (float)Math.Sqrt(variance) * 0.1f);
                }
            }
        }

        public class MovementFrame
        {
            public Vector3D Position { get; set; }
            public DateTime Timestamp { get; set; }
        }

        public class CombatStats
        {
            public float DamagePerSecond { get; set; }
            public float Accuracy { get; set; }
            public float EffectiveRange { get; set; }
            public Dictionary<string, float> WeaponEffectiveness { get; set; } = new Dictionary<string, float>();
            public float ArmorRating { get; set; }
            public float ShieldStrength { get; set; }
            public DateTime LastCombat { get; set; }
        }

        public class WeaponPriority
        {
            public string WeaponType { get; set; }
            public float Priority { get; set; }
            public float EffectivenessScore { get; set; }
            public float OptimalRange { get; set; }
            public string Reasoning { get; set; }
        }

        public class CombatOutcomePrediction
        {
            public float VictoryProbability { get; set; }
            public TimeSpan PredictedDuration { get; set; }
            public float ExpectedLosses { get; set; }
            public List<TacticalRecommendation> Recommendations { get; set; } = new List<TacticalRecommendation>();
            public float ConfidenceLevel { get; set; }
        }

        public class TacticalRecommendation
        {
            public string Action { get; set; }
            public float Priority { get; set; }
            public string Reasoning { get; set; }
            public Vector3D? TargetPosition { get; set; }
        }

        public void RecordEvent(long entityId, string eventType, Dictionary<string, object> data)
        {
            try
            {
                if (!_eventHistories.ContainsKey(entityId))
                    _eventHistories[entityId] = new List<EventRecord>();

                _eventHistories[entityId].Add(new EventRecord
                {
                    EventType = eventType,
                    Timestamp = DateTime.UtcNow,
                    Data = new Dictionary<string, object>(data ?? new Dictionary<string, object>())
                });

                var history = _eventHistories[entityId];
                if (history.Count > 100)
                {
                    history.RemoveRange(0, history.Count - 100);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recording event: {ex.Message}");
            }
        }

        public Dictionary<string, float> PredictOptimalBehavior(long entityId, Dictionary<string, object> context)
        {
            try
            {
                var predictions = new Dictionary<string, float>();

                List<EventRecord> history;
                if (!_eventHistories.TryGetValue(entityId, out history))
                    history = new List<EventRecord>();

                var behaviorHistory = history.Where(e => e.EventType == "BehaviorSelected" || e.EventType == "BehaviorOutcome").ToList();
                
                if (behaviorHistory.Count > 0)
                {
                    var behaviorGroups = behaviorHistory
                        .Where(e => e.Data.ContainsKey("SelectedBehavior") || e.Data.ContainsKey("Behavior"))
                        .GroupBy(e => e.Data.ContainsKey("SelectedBehavior") ? 
                            e.Data["SelectedBehavior"].ToString() : 
                            e.Data["Behavior"].ToString())
                        .ToList();

                    foreach (var group in behaviorGroups)
                    {
                        var behaviorName = group.Key;
                        var outcomes = group.Where(e => e.EventType == "BehaviorOutcome" && e.Data.ContainsKey("Success"));
                        
                        if (outcomes.Any())
                        {
                            var successRate = outcomes.Count(o => Convert.ToBoolean(o.Data["Success"])) / (float)outcomes.Count();
                            predictions[behaviorName] = successRate;
                        }
                        else
                        {
                            predictions[behaviorName] = 0.5f; // Default neutral prediction
                        }
                    }
                }

                if (context != null)
                {
                    if (context.ContainsKey("ThreatPresent") && Convert.ToSingle(context["ThreatPresent"]) > 0.5f)
                    {
                        predictions["AttackBehavior"] = Math.Min(predictions.GetValueOrDefault("AttackBehavior", 0.5f) + 0.3f, 1.0f);
                        predictions["RetreatBehavior"] = Math.Min(predictions.GetValueOrDefault("RetreatBehavior", 0.5f) + 0.2f, 1.0f);
                    }

                    if (context.ContainsKey("Health"))
                    {
                        var health = Convert.ToSingle(context["Health"]);
                        if (health < 0.3f)
                        {
                            predictions["RetreatBehavior"] = Math.Min(predictions.GetValueOrDefault("RetreatBehavior", 0.5f) + 0.4f, 1.0f);
                            predictions["AttackBehavior"] = Math.Max(predictions.GetValueOrDefault("AttackBehavior", 0.5f) - 0.3f, 0.1f);
                        }
                    }

                    if (context.ContainsKey("PlayerDistance"))
                    {
                        var distance = Convert.ToSingle(context["PlayerDistance"]);
                        if (distance < 1000)
                        {
                            predictions["AttackBehavior"] = Math.Min(predictions.GetValueOrDefault("AttackBehavior", 0.5f) + 0.2f, 1.0f);
                        }
                        else if (distance > 3000)
                        {
                            predictions["PatrolBehavior"] = Math.Min(predictions.GetValueOrDefault("PatrolBehavior", 0.5f) + 0.3f, 1.0f);
                        }
                    }
                }

                var standardBehaviors = new[] { "AttackBehavior", "PatrolBehavior", "IdleBehavior", "RetreatBehavior" };
                foreach (var behavior in standardBehaviors)
                {
                    if (!predictions.ContainsKey(behavior))
                        predictions[behavior] = 0.5f; // Default neutral prediction
                }

                return predictions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error predicting optimal behavior: {ex.Message}");
                return new Dictionary<string, float>
                {
                    ["AttackBehavior"] = 0.5f,
                    ["PatrolBehavior"] = 0.5f,
                    ["IdleBehavior"] = 0.5f,
                    ["RetreatBehavior"] = 0.5f
                };
            }
        }

        public float PredictBehaviorEffectiveness(long entityId, string behaviorType, Dictionary<string, object> context)
        {
            try
            {
                List<EventRecord> history;
                if (!_eventHistories.TryGetValue(entityId, out history))
                    return 0.5f;

                var behaviorOutcomes = history
                    .Where(e => e.EventType == "BehaviorOutcome" && 
                               e.Data.ContainsKey("Behavior") && 
                               e.Data["Behavior"].ToString() == behaviorType)
                    .ToList();

                if (behaviorOutcomes.Count == 0)
                    return 0.5f; // No history, assume neutral effectiveness

                var successCount = behaviorOutcomes.Count(o => 
                    o.Data.ContainsKey("Success") && Convert.ToBoolean(o.Data["Success"]));
                
                var baseEffectiveness = successCount / (float)behaviorOutcomes.Count;

                if (context != null)
                {
                    if (context.ContainsKey("Health"))
                    {
                        var health = Convert.ToSingle(context["Health"]);
                        if (behaviorType == "AttackBehavior" && health < 0.3f)
                            baseEffectiveness *= 0.7f; // Less effective when damaged
                        else if (behaviorType == "RetreatBehavior" && health < 0.5f)
                            baseEffectiveness *= 1.3f; // More effective when retreating while damaged
                    }

                    if (context.ContainsKey("HasTarget"))
                    {
                        var hasTarget = Convert.ToBoolean(context["HasTarget"]);
                        if (behaviorType == "AttackBehavior" && !hasTarget)
                            baseEffectiveness *= 0.5f; // Can't attack without target
                        else if (behaviorType == "IdleBehavior" && hasTarget)
                            baseEffectiveness *= 0.3f; // Idle is bad when threats are present
                    }
                }

                return MathHelper.Clamp(baseEffectiveness, 0.1f, 1.0f);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error predicting behavior effectiveness: {ex.Message}");
                return 0.5f; 
            }
        }

        public void UpdateMovementHistory(IMyEntity entity)
        {
            if (entity?.Physics == null) return;

            if (!_movementHistories.ContainsKey(entity.EntityId))
                _movementHistories[entity.EntityId] = new MovementHistory();

            _movementHistories[entity.EntityId].AddPosition(entity.PositionComp.WorldAABB.Center, DateTime.UtcNow);
        }

        public Vector3D PredictEnemyPosition(IMyEntity enemy, float timeAhead)
        {
            if (!_movementHistories.ContainsKey(enemy.EntityId))
                return enemy.PositionComp.WorldAABB.Center;

            var history = _movementHistories[enemy.EntityId];
            var currentPos = enemy.PositionComp.WorldAABB.Center;
            
            var predictedPos = currentPos + history.PredictedVelocity * timeAhead + 
                              0.5 * history.PredictedAcceleration * timeAhead * timeAhead;

            var uncertainty = (1.0f - history.PredictabilityScore) * timeAhead * 100;
            var random = new Random();
            var offset = new Vector3D(
                (random.NextDouble() - 0.5) * uncertainty,
                (random.NextDouble() - 0.5) * uncertainty,
                (random.NextDouble() - 0.5) * uncertainty
            );

            return predictedPos + offset;
        }

        public WeaponPriority AnalyzeOptimalWeapons(IMyEntity target)
        {
            if (!_combatStats.ContainsKey(target.EntityId))
                AnalyzeEntityCombatCapabilities(target);

            CombatStats targetStats;
            if (!_combatStats.TryGetValue(target.EntityId, out targetStats))
                targetStats = new CombatStats();

            var distance = Vector3D.Distance(target.PositionComp.WorldAABB.Center, Vector3D.Zero); // Placeholder

            var weaponScores = new Dictionary<string, float>
            {
                ["Railgun"] = CalculateWeaponScore("Railgun", targetStats, distance),
                ["Missile"] = CalculateWeaponScore("Missile", targetStats, distance),
                ["Gatling"] = CalculateWeaponScore("Gatling", targetStats, distance),
                ["Laser"] = CalculateWeaponScore("Laser", targetStats, distance),
                ["Plasma"] = CalculateWeaponScore("Plasma", targetStats, distance)
            };

            var bestWeapon = weaponScores.OrderByDescending(x => x.Value).First();

            return new WeaponPriority
            {
                WeaponType = bestWeapon.Key,
                Priority = bestWeapon.Value,
                EffectivenessScore = bestWeapon.Value,
                OptimalRange = GetOptimalRange(bestWeapon.Key),
                Reasoning = GenerateWeaponReasoning(bestWeapon.Key, targetStats, distance)
            };
        }

        private float CalculateWeaponScore(string weaponType, CombatStats targetStats, double distance)
        {
            var score = 1.0f;

            switch (weaponType)
            {
                case "Railgun":
                    score = targetStats.ArmorRating > 0.7f ? 1.5f : 0.8f; // Good vs armor
                    if (distance > 2000) score *= 1.3f; // Long range bonus
                    break;
                case "Missile":
                    score = targetStats.ShieldStrength < 0.3f ? 1.4f : 0.6f; // Good vs low shields
                    if (distance > 1000) score *= 1.2f;
                    break;
                case "Gatling":
                    score = targetStats.ArmorRating < 0.5f ? 1.3f : 0.7f; // Good vs light armor
                    if (distance < 800) score *= 1.4f; // Close range bonus
                    break;
                case "Laser":
                    score = targetStats.ShieldStrength > 0.5f ? 1.6f : 1.0f; // Good vs shields
                    break;
                case "Plasma":
                    score = 1.2f; // Balanced weapon
                    break;
            }

            return MathHelper.Clamp(score, 0.1f, 2.0f);
        }

        private void AnalyzeEntityCombatCapabilities(IMyEntity entity)
        {
            var stats = new CombatStats();
            
            if (entity is IMyCubeGrid grid)
            {
                var blocks = new List<IMySlimBlock>();
                grid.GetBlocks(blocks);

                var weaponBlocks = blocks.Where(b => IsWeaponBlock(b)).ToList();
                stats.DamagePerSecond = weaponBlocks.Count * 100; 
                
                var armorBlocks = blocks.Where(b => b.BlockDefinition.Id.SubtypeName.Contains("Armor")).Count();
                stats.ArmorRating = Math.Min(armorBlocks / (float)Math.Max(blocks.Count, 1), 1.0f);
            }

            stats.LastCombat = DateTime.UtcNow;
            _combatStats[entity.EntityId] = stats;
        }

        private bool IsWeaponBlock(IMySlimBlock block)
        {
            var subtype = block.BlockDefinition.Id.SubtypeName;
            return subtype.Contains("Gatling") || 
                   subtype.Contains("Missile") || 
                   subtype.Contains("Laser") || 
                   subtype.Contains("Railgun") ||
                   subtype.Contains("Turret");
        }

        public CombatOutcomePrediction PredictCombatOutcome(List<IMyEntity> allies, List<IMyEntity> enemies)
        {
            var allyPower = allies.Sum(a => GetCombatPower(a));
            var enemyPower = enemies.Sum(e => GetCombatPower(e));

            var powerRatio = allyPower / Math.Max(enemyPower, 1.0f);
            var victoryProbability = MathHelper.Clamp(powerRatio / (1.0f + powerRatio), 0.05f, 0.95f);

            var prediction = new CombatOutcomePrediction
            {
                VictoryProbability = victoryProbability,
                PredictedDuration = TimeSpan.FromSeconds(60 + (enemyPower / Math.Max(allyPower, 1)) * 120),
                ExpectedLosses = (1.0f - victoryProbability) * allies.Count,
                ConfidenceLevel = Math.Min(allies.Count + enemies.Count, 10) / 10.0f
            };

            if (victoryProbability < 0.3f)
            {
                prediction.Recommendations.Add(new TacticalRecommendation
                {
                    Action = "Retreat",
                    Priority = 0.9f,
                    Reasoning = "Overwhelming enemy force detected"
                });
            }
            else if (victoryProbability < 0.6f)
            {
                prediction.Recommendations.Add(new TacticalRecommendation
                {
                    Action = "Defensive_Formation",
                    Priority = 0.7f,
                    Reasoning = "Even match - maintain defensive posture"
                });
            }
            else
            {
                prediction.Recommendations.Add(new TacticalRecommendation
                {
                    Action = "Aggressive_Advance",
                    Priority = 0.8f,
                    Reasoning = "Superior force - press advantage"
                });
            }

            return prediction;
        }

        internal float GetCombatPower(IMyEntity entity)
        {
            if (_combatStats.ContainsKey(entity.EntityId))
                return _combatStats[entity.EntityId].DamagePerSecond;

            AnalyzeEntityCombatCapabilities(entity);
            
            CombatStats stats;
            if (_combatStats.TryGetValue(entity.EntityId, out stats))
                return stats.DamagePerSecond;
            
            return 0f;
        }

        private float GetOptimalRange(string weaponType)
        {
            switch (weaponType)
            {
                case "Railgun":
                    return 3000f;
                case "Missile":
                    return 2000f;
                case "Gatling":
                    return 800f;
                case "Laser":
                    return 1500f;
                case "Plasma":
                    return 1200f;
                default:
                    return 1000f;
            }
        }

        private string GenerateWeaponReasoning(string weaponType, CombatStats targetStats, double distance)
        {
            return string.Format("{0} selected: Target armor {1:P0}, shields {2:P0}, range {3:F0}m", 
                weaponType, targetStats.ArmorRating, targetStats.ShieldStrength, distance);
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                _movementHistories?.Clear();
                _combatStats?.Clear();
                _eventHistories?.Clear();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing PredictiveAnalyzer: {ex.Message}");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}