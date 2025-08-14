using System;
using System.Collections.Generic;
using System.Linq;
using VRageMath;

namespace Helios.Modules.AI.Behaviors
{
    public class AdaptiveBehaviorEngine
    {
        private Dictionary<string, BehaviorNode> _behaviorNodes = new Dictionary<string, BehaviorNode>();
        private Dictionary<long, EntityBehaviorHistory> _entityHistories = new Dictionary<long, EntityBehaviorHistory>();

        public class BehaviorNode
        {
            public string Id { get; set; }
            public float SuccessRate { get; set; } = 0.5f;
            public DateTime LastUsed { get; set; }
            public Dictionary<string, float> ContextWeights { get; set; } = new Dictionary<string, float>();
            public int UsageCount { get; set; }
            public float AdaptationRate { get; set; } = 0.1f;
            
            public void UpdateSuccess(bool success, Dictionary<string, float> context)
            {
                var newSuccess = success ? 1.0f : 0.0f;
                SuccessRate = SuccessRate * (1 - AdaptationRate) + newSuccess * AdaptationRate;
                
                foreach (var contextPair in context)
                {
                    if (!ContextWeights.ContainsKey(contextPair.Key))
                        ContextWeights[contextPair.Key] = 0.5f;
                    
                    var adjustment = success ? 0.05f : -0.05f;
                    ContextWeights[contextPair.Key] = MathHelper.Clamp(
                        ContextWeights[contextPair.Key] + adjustment * contextPair.Value, 
                        0.0f, 1.0f);
                }
                
                LastUsed = DateTime.UtcNow;
                UsageCount++;
            }
        }

        public class EntityBehaviorHistory
        {
            public long EntityId { get; set; }
            public List<BehaviorExecution> RecentExecutions { get; set; } = new List<BehaviorExecution>();
            public Dictionary<string, float> PersonalityWeights { get; set; } = new Dictionary<string, float>();
            
            public void AddExecution(BehaviorExecution execution)
            {
                RecentExecutions.Add(execution);
                if (RecentExecutions.Count > 100) // Keep last 100 executions
                    RecentExecutions.RemoveAt(0);
            }
        }

        public class BehaviorExecution
        {
            public string BehaviorId { get; set; }
            public DateTime ExecutionTime { get; set; }
            public bool Success { get; set; }
            public Dictionary<string, float> Context { get; set; }
            public float EffectivenessScore { get; set; }
        }

        public void RegisterBehavior(string behaviorId, float initialSuccessRate = 0.5f)
        {
            if (!_behaviorNodes.ContainsKey(behaviorId))
            {
                _behaviorNodes[behaviorId] = new BehaviorNode
                {
                    Id = behaviorId,
                    SuccessRate = initialSuccessRate
                };
            }
        }

        public string SelectOptimalBehavior(long entityId, Dictionary<string, float> currentContext, List<string> availableBehaviors)
        {
            if (!availableBehaviors.Any()) return null;

            var scores = new Dictionary<string, float>();

            foreach (var behaviorId in availableBehaviors)
            {
                if (!_behaviorNodes.ContainsKey(behaviorId))
                    RegisterBehavior(behaviorId);

                var node = _behaviorNodes[behaviorId];
                var score = CalculateBehaviorScore(entityId, node, currentContext);
                scores[behaviorId] = score;
            }

            var random = new Random();
            var topBehaviors = scores.OrderByDescending(x => x.Value).Take(3).ToList();
    
            if (topBehaviors.Count == 1)
                return topBehaviors[0].Key;

            var weights = topBehaviors.Select(x => (float)Math.Exp(x.Value * 2)).ToArray(); 
            var totalWeight = weights.Sum();
            var randomValue = (float)(random.NextDouble() * totalWeight); 

            float cumulative = 0;
            for (var i = 0; i < topBehaviors.Count; i++)
            {
                cumulative += weights[i];
                if (randomValue <= cumulative)
                    return topBehaviors[i].Key;
            }

            return topBehaviors[0].Key;
        }

        private float CalculateBehaviorScore(long entityId, BehaviorNode node, Dictionary<string, float> context)
        {
            var score = node.SuccessRate;

            foreach (var contextPair in context)
            {
                if (node.ContextWeights.ContainsKey(contextPair.Key))
                {
                    score += node.ContextWeights[contextPair.Key] * contextPair.Value * 0.3f;
                }
            }

            var timeSinceUse = DateTime.UtcNow - node.LastUsed;
            if (timeSinceUse.TotalMinutes > 5)
                score += 0.1f * Math.Min((float)timeSinceUse.TotalMinutes / 60, 1.0f);

            if (_entityHistories.ContainsKey(entityId))
            {
                var history = _entityHistories[entityId];
                var recentUse = history.RecentExecutions
                    .Where(x => x.BehaviorId == node.Id && x.ExecutionTime > DateTime.UtcNow.AddMinutes(-10))
                    .Count();
                
                if (recentUse > 2)
                    score *= 0.7f; 
            }

            return MathHelper.Clamp(score, 0.0f, 2.0f);
        }

        public void ReportBehaviorOutcome(long entityId, string behaviorId, bool success, 
            Dictionary<string, float> context, float effectivenessScore = 1.0f)
        {
            if (_behaviorNodes.ContainsKey(behaviorId))
            {
                _behaviorNodes[behaviorId].UpdateSuccess(success, context);
            }

            if (!_entityHistories.ContainsKey(entityId))
                _entityHistories[entityId] = new EntityBehaviorHistory { EntityId = entityId };

            _entityHistories[entityId].AddExecution(new BehaviorExecution
            {
                BehaviorId = behaviorId,
                ExecutionTime = DateTime.UtcNow,
                Success = success,
                Context = new Dictionary<string, float>(context),
                EffectivenessScore = effectivenessScore
            });
        }

        public BehaviorAnalytics GetAnalytics(string behaviorId = null)
        {
            if (behaviorId != null && _behaviorNodes.ContainsKey(behaviorId))
            {
                var node = _behaviorNodes[behaviorId];
                return new BehaviorAnalytics
                {
                    BehaviorId = behaviorId,
                    SuccessRate = node.SuccessRate,
                    UsageCount = node.UsageCount,
                    LastUsed = node.LastUsed,
                    ContextWeights = new Dictionary<string, float>(node.ContextWeights)
                };
            }

            return new BehaviorAnalytics
            {
                TotalBehaviors = _behaviorNodes.Count,
                AverageSuccessRate = _behaviorNodes.Values.Average(x => x.SuccessRate),
                TotalExecutions = _behaviorNodes.Values.Sum(x => x.UsageCount)
            };
        }
    }

    public class BehaviorAnalytics
    {
        public string BehaviorId { get; set; }
        public float SuccessRate { get; set; }
        public int UsageCount { get; set; }
        public DateTime LastUsed { get; set; }
        public Dictionary<string, float> ContextWeights { get; set; }
        public int TotalBehaviors { get; set; }
        public float AverageSuccessRate { get; set; }
        public int TotalExecutions { get; set; }
    }
}