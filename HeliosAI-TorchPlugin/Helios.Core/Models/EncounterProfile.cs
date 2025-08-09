using System.Collections.Generic;
using Helios.Core.Interfaces;
using Helios.Modules.AI;
using VRageMath;

namespace HeliosAI.Models
{
    public class EncounterProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public EncounterDifficulty Difficulty { get; set; }
        public EncounterType EncounterType { get; set; }
        public string PrefabName { get; set; }
        public float SpawnChance { get; set; } = 0.1f;
        public int MaxInstances { get; set; } = 1;
        public double DespawnDistance { get; set; } = 10000;
        public double RequiredPlayerDistance { get; set; } = 2000;
        public List<string> RequiredMods { get; set; } = new List<string>();
        public Dictionary<string, object> CustomProperties { get; set; } = new Dictionary<string, object>();
        
        // AI Behavior settings
        public string DefaultBehavior { get; set; } = "Patrol";
        public NpcEntity.AiMood DefaultMood { get; set; } = NpcEntity.AiMood.Guard;
        
        // Spawn conditions
        public bool RequireEmptySpace { get; set; } = true;
        public double MinDistanceFromPlayers { get; set; } = 1000;
        public double MinDistanceFromGrids { get; set; } = 500;
        public List<string> AllowedEnvironments { get; set; } = new List<string> { "Space", "Atmosphere" };
        
        public string FactionTag { get; set; }
        public double DefenseRadius { get; set; } = 1000;
        public List<Vector3D> Waypoints { get; set; }
    }
}