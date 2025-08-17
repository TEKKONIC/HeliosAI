using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Modules.API;
using NLog;
using VRageMath;

namespace HeliosAI.Nexus
{
    public enum NexusZone
    {
        /// <summary>
        /// Default zone with standard behavior
        /// </summary>
        Default = 0,
        
        /// <summary>
        /// Combat zone with aggressive AI behavior
        /// </summary>
        Combat = 1,
        
        /// <summary>
        /// Safe zone with peaceful AI behavior
        /// </summary>
        Safe = 2,
        
        /// <summary>
        /// Trade zone with merchant AI behavior
        /// </summary>
        Trade = 3
    }

    public static class NexusZoneManager
    {
        private static readonly Logger Logger = LogManager.GetLogger("NexusZoneManager");
        private static readonly Dictionary<string, NexusZone> _sectorZoneCache = new();
        private static DateTime _lastCacheUpdate = DateTime.MinValue;
        private static readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public static NexusZone GetZoneForPosition(Vector3D position)
        {
            try
            {
                var nexusApi = APIManager.Nexus;
                if (nexusApi == null)
                {
                    Logger.Debug("Nexus API not available, returning default zone");
                    return NexusZone.Default;
                }

                if (!nexusApi.Enabled)
                {
                    Logger.Debug("Nexus API disabled, returning default zone");
                    return NexusZone.Default;
                }

                if (nexusApi.Sectors == null)
                {
                    Logger.Warn("Nexus sectors collection is null");
                    return NexusZone.Default;
                }

                var sectorId = nexusApi.GetTargetSector(position);
                var sector = nexusApi.Sectors.Find(s => s.SectorID == sectorId);

                if (sector == null)
                {
                    Logger.Debug($"No sector found for position {position}, sector ID: {sectorId}");
                    return NexusZone.Default;
                }

                var zone = GetZoneFromSectorName(sector.SectorName);
                Logger.Debug($"Position {position} assigned to zone: {zone} (sector: {sector.SectorName})");
                
                return zone;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get zone for position: {position}");
                return NexusZone.Default;
            }
        }

        private static NexusZone GetZoneFromSectorName(string sectorName)
        {
            if (string.IsNullOrWhiteSpace(sectorName))
                return NexusZone.Default;

            try
            {
                if (_sectorZoneCache.TryGetValue(sectorName, out var cachedZone) && 
                    DateTime.UtcNow - _lastCacheUpdate < _cacheExpiry)
                {
                    return cachedZone;
                }

                var zone = sectorName.ToLowerInvariant() switch
                {
                    var name when name.Contains("combat") || name.Contains("war") || name.Contains("conflict") => NexusZone.Combat,
                    var name when name.Contains("safe") || name.Contains("peace") || name.Contains("sanctuary") => NexusZone.Safe,
                    var name when name.Contains("trade") || name.Contains("market") || name.Contains("commerce") => NexusZone.Trade,
                    _ => NexusZone.Default
                };

                _sectorZoneCache[sectorName] = zone;
                _lastCacheUpdate = DateTime.UtcNow;

                return zone;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to determine zone from sector name: {sectorName}");
                return NexusZone.Default;
            }
        }

        public static bool IsNexusAvailable()
        {
            try
            {
                var nexusApi = APIManager.Nexus;
                return nexusApi != null && nexusApi.Enabled;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to check Nexus availability");
                return false;
            }
        }

        public static IEnumerable<string> GetAvailableSectors()
        {
            try
            {
                var nexusApi = APIManager.Nexus;
                if (nexusApi?.Sectors == null)
                    return Enumerable.Empty<string>();

                return nexusApi.Sectors.Select(s => s.SectorName).Where(name => !string.IsNullOrWhiteSpace(name));
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to get available sectors");
                return Enumerable.Empty<string>();
            }
        }

        public static void ClearCache()
        {
            try
            {
                _sectorZoneCache.Clear();
                _lastCacheUpdate = DateTime.MinValue;
                Logger.Info("Nexus zone cache cleared");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clear Nexus zone cache");
            }
        }

        public static void SetSectorZone(string sectorName, NexusZone zone)
        {
            if (string.IsNullOrWhiteSpace(sectorName))
            {
                Logger.Warn("Attempted to set zone for null or empty sector name");
                return;
            }

            try
            {
                _sectorZoneCache[sectorName] = zone;
                _lastCacheUpdate = DateTime.UtcNow;
                Logger.Info($"Manually set sector '{sectorName}' to zone: {zone}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to set zone for sector: {sectorName}");
            }
        }

        public static int GetCachedSectorCount()
        {
            return _sectorZoneCache.Count;
        }
    }
}