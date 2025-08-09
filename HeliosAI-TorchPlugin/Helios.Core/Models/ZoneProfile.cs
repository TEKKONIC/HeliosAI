using System;
using System.Collections.Generic;
using VRageMath;
using VRage.Utils;

namespace HeliosAI.Models
{
    public class ZoneProfile
    {
        public string ZoneId { get; set; }
        public string DisplayName { get; set; }
        public bool Active { get; set; }
        public bool Persistent { get; set; }
        public Vector3D Center { get; set; }
        public double Radius { get; set; }
        public List<string> EncounterProfiles { get; set; } = new List<string>();
        public int SpawnIntervalSeconds { get; set; } = 600;
        public int MaxSpawns { get; set; } = 3;
        public bool NoSpawnZone { get; set; } = false;
        public List<string> NexusSectors { get; set; } = new List<string>();

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
    }
}