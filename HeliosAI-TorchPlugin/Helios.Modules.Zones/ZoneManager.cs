using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Helios.Core.Interfaces;
using Helios.Modules.Encounters;
using NLog;
using Torch.API;
using VRage.Utils;
using VRageMath;

namespace HeliosAI.NPCZones
{
    public class ZoneManager : IZoneManager
    {
        public List<ZoneProfile> Zones = new();
        public static string PluginFolder { get; private set; }
        public static string ZonesFolder { get; private set; }
        public static string EncountersFolder { get; private set; }
        private static readonly Logger Logger = LogManager.GetLogger("ZoneManager");
        
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
            // Load all JSON zone profiles from disk
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
            }
        }

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
    }
    
    public static class NexusIntegration
    {
        public static bool IsAvailable => HeliosAIPlugin.Instance?.NexusApi?.Enabled == true;

        public static string CurrentSector => HeliosAIPlugin.Instance?.NexusApi?.CurrentServerID.ToString() ?? "Unknown";

        public static bool CurrentSectorMatches(List<string> sectors)
        {
            if (!IsAvailable || sectors == null || sectors.Count == 0) return false;
            return sectors.Contains(CurrentSector);
        }
    }
}