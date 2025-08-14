using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;

namespace Helios.Modules.Nations
{
    public static class NationHelper
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationHelper");

        // Optional external alliances API (e.g., Crunch Alliances) injected by plugin
        private static object _alliancesApi;
        public static bool _checkedForAlliances;
        private static Func<long, long, bool> _alliancesAreAllied; 
        private static readonly ConcurrentDictionary<long, NationType> FactionIdToNation = new();
        private static readonly ConcurrentDictionary<string, NationType> TagToNation =
            new(StringComparer.OrdinalIgnoreCase);
        private static DateTime _lastCacheClean = DateTime.UtcNow;
        private const int CACHE_CLEAN_INTERVAL_MINUTES = 30;

        static NationHelper()
        {
            TagToNation.TryAdd("MCRN", NationType.MCRN);
            TagToNation.TryAdd("MARS", NationType.MCRN);  // Alternative
            TagToNation.TryAdd("UNN", NationType.UNN);
            TagToNation.TryAdd("EARTH", NationType.UNN);  // Alternative
            TagToNation.TryAdd("OPA", NationType.OPA);
            TagToNation.TryAdd("BELT", NationType.OPA);   // Alternative
            TagToNation.TryAdd("UNKNOWN", NationType.Unknown);
            TagToNation.TryAdd("UNK", NationType.Unknown);
            TagToNation.TryAdd("PIRATE", NationType.Unknown); // Pirates are unknown
            TagToNation.TryAdd("INDIE", NationType.Unknown);  // Independents
        }

        /// <summary>
        /// Optional: Inject an external alliances API instance (dynamic).
        /// </summary>
        public static void ConfigureAlliancesApi(object alliancesApi)
        {
            _alliancesApi = alliancesApi;
            _checkedForAlliances = true;

            _alliancesAreAllied = TryBindAreAllied(alliancesApi);
            if (_alliancesAreAllied != null)
                Logger.Info("Alliances API configured in NationHelper (AreAllied bound)");
            else
                Logger.Warn("Alliances API provided, but AreAllied(long,long) could not be bound");
        }

        /// <summary>
        /// Optional: direct setter to avoid reflection if you have the delegate.
        /// </summary>
        public static void SetAreAlliedResolver(Func<long, long, bool> resolver)
        {
            _alliancesAreAllied = resolver;
            _checkedForAlliances = true;
            Logger.Info("Alliances AreAllied resolver set");
        }

        private static Func<long, long, bool> TryBindAreAllied(object api)
        {
            if (api == null) return null;

            try
            {
                var t = api.GetType();
                var mi = t.GetMethod("AreAllied", new[] { typeof(long), typeof(long) });
                if (mi == null || mi.ReturnType != typeof(bool))
                    return null;
                var del = Delegate.CreateDelegate(typeof(Func<long, long, bool>), api, mi, false) as Func<long, long, bool>;
                if (del != null) return del;
                return (a, b) => (bool)mi.Invoke(api, new object[] { a, b });
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to bind AreAllied(long,long) from alliances API");
                return null;
            }
        }

        /// <summary>
        /// Resolve NationType from an entity (character/grid).
        /// </summary>
        public static NationType GetNation(IMyEntity entity)
        {
            try
            {
                if (entity == null) return NationType.Unknown;

                if (entity is IMyCharacter ch)
                    return GetNationFromIdentity(ch.ControllerInfo?.ControllingIdentityId ?? 0);

                if (entity is IMyCubeGrid grid)
                    return GetNation(grid);

                return NationType.Unknown;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNation failed for entity: {entity?.DisplayName}");
                return NationType.Unknown;
            }
        }

        /// <summary>
        /// Resolve NationType from a grid by its primary owner’s faction.
        /// </summary>
        public static NationType GetNation(IMyCubeGrid grid)
        {
            try
            {
                if (grid == null || grid.MarkedForClose) return NationType.Unknown;

                var ownerId = grid.BigOwners?.FirstOrDefault() ?? 0;
                if (ownerId == 0) return NationType.Unknown;

                var faction = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(ownerId);
                return GetNation(faction);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNation failed for grid: {(grid as IMyEntity)?.DisplayName}");
                return NationType.Unknown;
            }
        }

        /// <summary>
        /// Resolve NationType from a player.
        /// </summary>
        public static NationType GetNation(IMyPlayer player)
        {
            try
            {
                if (player?.IdentityId == null) return NationType.Unknown;
                return GetNationFromIdentity(player.IdentityId);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNation failed for player: {player?.DisplayName}");
                return NationType.Unknown;
            }
        }

        /// <summary>
        /// Resolve NationType from an identityId via faction tag.
        /// </summary>
        public static NationType GetNationFromIdentity(long identityId)
        {
            try
            {
                if (identityId == 0) return NationType.Unknown;

                var faction = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(identityId);
                return GetNation(faction);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNationFromIdentity failed for identity: {identityId}");
                return NationType.Unknown;
            }
        }

        /// <summary>
        /// Resolve NationType from an IMyFaction by cached mapping or tag.
        /// </summary>
        public static NationType GetNation(IMyFaction faction)
        {
            if (faction == null) return NationType.Unknown;

            try
            {
                CleanCacheIfNeeded();

                if (FactionIdToNation.TryGetValue(faction.FactionId, out var cached))
                    return cached;

                var tag = (faction.Tag ?? faction.Name ?? string.Empty).Trim();
                var nation = GetNationByTag(tag);

                FactionIdToNation[faction.FactionId] = nation;
                return nation;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNation failed for faction: {faction?.Tag}");
                return NationType.Unknown;
            }
        }

        /// <summary>
        /// Map a faction tag to NationType using case-insensitive dictionary.
        /// </summary>
        public static NationType GetNationByTag(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return NationType.Unknown;

            try
            {
                var cleanTag = tag.Trim().ToUpperInvariant();
                
                if (TagToNation.TryGetValue(cleanTag, out var found))
                    return found;

                var nation = DetectNationByPattern(cleanTag);
                if (nation != NationType.Unknown)
                {
                    TagToNation[cleanTag] = nation;
                    Logger.Debug($"Auto-detected nation for tag '{tag}': {nation}");
                    return nation;
                }

                TagToNation[cleanTag] = NationType.Unknown;
                return NationType.Unknown;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetNationByTag failed for tag: {tag}");
                return NationType.Unknown;
            }
        }

        private static NationType DetectNationByPattern(string tag)
        {
            try
            {
                // MCRN patterns
                if (tag.Contains("MARS") || tag.Contains("MCRN") || tag.Contains("MCR"))
                    return NationType.MCRN;
                
                // UNN patterns  
                if (tag.Contains("EARTH") || tag.Contains("UNN") || tag.Contains("UNITED"))
                    return NationType.UNN;
                
                // OPA patterns
                if (tag.Contains("BELT") || tag.Contains("OPA") || tag.Contains("OUTER"))
                    return NationType.OPA;
                
                return NationType.Unknown;
            }
            catch
            {
                return NationType.Unknown;
            }
        }

        private static void CleanCacheIfNeeded()
        {
            try
            {
                var now = DateTime.UtcNow;
                if ((now - _lastCacheClean).TotalMinutes < CACHE_CLEAN_INTERVAL_MINUTES)
                    return;

                var invalidEntries = FactionIdToNation.Keys
                    .Where(factionId => MyAPIGateway.Session?.Factions?.TryGetFactionById(factionId) == null)
                    .ToList();

                foreach (var invalidId in invalidEntries)
                {
                    FactionIdToNation.TryRemove(invalidId, out _);
                }

                _lastCacheClean = now;
                
                if (invalidEntries.Any())
                    Logger.Debug($"Cleaned {invalidEntries.Count} invalid faction cache entries");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error during cache cleanup");
            }
        }

        /// <summary>
        /// Check if two factions are allies using (1) external alliances API if present, else (2) vanilla faction relations.
        /// </summary>
        public static bool AreFactionsAllied(long factionIdA, long factionIdB)
        {
            try
            {
                if (factionIdA == 0 || factionIdB == 0) return false;

                if (_alliancesAreAllied != null)
                {
                    try
                    {
                        return _alliancesAreAllied(factionIdA, factionIdB);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "Alliances AreAllied resolver threw; falling back to vanilla relations");
                    }
                }

                var rel = MyAPIGateway.Session?.Factions?
                    .GetRelationBetweenFactions(factionIdA, factionIdB) ?? MyRelationsBetweenFactions.Neutral;

                return rel == MyRelationsBetweenFactions.Friends;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"AreFactionsAllied failed for {factionIdA} vs {factionIdB}");
                return false;
            }
        }

        /// <summary>
        /// Returns true if factions are enemies via vanilla relations (ignores alliances API).
        /// </summary>
        public static bool AreFactionsEnemies(long factionIdA, long factionIdB)
        {
            try
            {
                if (factionIdA == 0 || factionIdB == 0) return false;

                var rel = MyAPIGateway.Session?.Factions?
                    .GetRelationBetweenFactions(factionIdA, factionIdB) ?? MyRelationsBetweenFactions.Neutral;

                return rel == MyRelationsBetweenFactions.Enemies;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"AreFactionsEnemies failed for {factionIdA} vs {factionIdB}");
                return false;
            }
        }

        public static bool AreNationsHostile(NationType a, NationType b)
        {
            if (a == NationType.Unknown || b == NationType.Unknown) return false;
            if (a == b) return false;

            return (a, b) switch
            {
                (NationType.MCRN, NationType.UNN) => true,  // Mars vs Earth tension
                (NationType.UNN, NationType.MCRN) => true,  // Earth vs Mars tension
                (NationType.OPA, NationType.MCRN) => true,  // Belters vs Mars
                (NationType.OPA, NationType.UNN) => true,   // Belters vs Earth
                (NationType.MCRN, NationType.OPA) => true,  // Mars vs Belters
                (NationType.UNN, NationType.OPA) => true,   // Earth vs Belters
                _ => false // Unknown relationships default to non-hostile
            };
        }

        /// <summary>
        /// Entities hostile check using faction relations primarily; falls back to nation rules.
        /// </summary>
        public static bool AreEntitiesHostile(IMyEntity a, IMyEntity b)
        {
            try
            {
                if (a == null || b == null) return false;

                var facA = GetPrimaryFactionId(a);
                var facB = GetPrimaryFactionId(b);

                if (facA != 0 && facB != 0)
                {
                    if (AreFactionsAllied(facA, facB)) return false;
                    if (AreFactionsEnemies(facA, facB)) return true;
                }

                var na = GetNation(a);
                var nb = GetNation(b);
                return AreNationsHostile(na, nb);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "AreEntitiesHostile failed");
                return false;
            }
        }

        /// <summary>
        /// Get primary factionId for an entity (character/grid).
        /// </summary>
        public static long GetPrimaryFactionId(IMyEntity entity)
        {
            try
            {
                if (entity is IMyCharacter ch)
                {
                    var id = ch.ControllerInfo?.ControllingIdentityId ?? 0;
                    var f = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(id);
                    return f?.FactionId ?? 0;
                }

                if (entity is IMyCubeGrid grid)
                {
                    var owner = grid.BigOwners?.FirstOrDefault() ?? 0;
                    var f = MyAPIGateway.Session?.Factions?.TryGetPlayerFaction(owner);
                    return f?.FactionId ?? 0;
                }

                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"GetPrimaryFactionId failed for entity: {entity?.DisplayName}");
                return 0;
            }
        }

        /// <summary>
        /// Clear caches (e.g., on faction/tag changes).
        /// </summary>
        public static void ClearCaches()
        {
            FactionIdToNation.Clear();
            Logger.Debug("NationHelper caches cleared");
        }

        public static float GetAllianceScore(IMyEntity a, IMyEntity b)
        {
            try
            {
                if (a == null || b == null) return 0f;

                var facA = GetPrimaryFactionId(a);
                var facB = GetPrimaryFactionId(b);

                if (facA != 0 && facA == facB) return 1.0f;

                if (facA != 0 && facB != 0 && AreFactionsAllied(facA, facB)) return 0.8f;

                var nationA = GetNation(a);
                var nationB = GetNation(b);
                if (nationA != NationType.Unknown && nationA == nationB) return 0.6f;

                if (AreNationsHostile(nationA, nationB)) return -0.8f;

                if (facA != 0 && facB != 0 && AreFactionsEnemies(facA, facB)) return -1.0f;

                return 0f; // Neutral
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error calculating alliance score");
                return 0f;
            }
        }

        public static Dictionary<long, NationType> GetNationsForPlayers(IEnumerable<long> playerIds)
        {
            var results = new Dictionary<long, NationType>();
            
            try
            {
                foreach (var playerId in playerIds)
                {
                    results[playerId] = GetNationFromIdentity(playerId);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in bulk nation lookup");
            }
            
            return results;
        }

        public static (int FactionCacheSize, int TagCacheSize) GetCacheStats()
        {
            return (FactionIdToNation.Count, TagToNation.Count);
        }

        /// <summary>
        /// Alias for GetNation(IMyCubeGrid) to match AI behavior usage patterns
        /// </summary>
        public static NationType GetGridNation(IMyCubeGrid grid)
        {
            return GetNation(grid);
        }
        
        /// <summary>
        /// Check if two nations are allied or friendly
        /// </summary>
        public static bool AreNationsAllied(NationType a, NationType b)
        {
            if (a == NationType.Unknown || b == NationType.Unknown) return false;
            if (a == b) return true; // Same nation = allied
    
            // Add specific alliance rules here if needed
            // For now, same nation is the only alliance
            return false;
        }

        /// <summary>
        /// Alias for consistency with AI behavior usage
        /// </summary>
        public static bool AreAllied(NationType a, NationType b)
        {
            return AreNationsAllied(a, b);
        }

        /// <summary>
        /// Get short code for nation (useful for encounter tags, etc.)
        /// </summary>
        public static string GetShortCode(this NationType nation)
        {
            return nation switch
            {
                NationType.MCRN => "MCRN",
                NationType.UNN => "UNN", 
                NationType.OPA => "OPA",
                NationType.Unknown => "UNK",
                _ => "UNK"
            };
        }

        /// <summary>
        /// Get display name for nation
        /// </summary>
        public static string GetDisplayName(this NationType nation)
        {
            return nation switch
            {
                NationType.MCRN => "Martian Congressional Republic Navy",
                NationType.UNN => "United Nations Navy",
                NationType.OPA => "Outer Planets Alliance", 
                NationType.Unknown => "Unknown Faction",
                _ => "Unknown"
            };
        }

        /// <summary>
        /// Initialize nation relationships and mappings
        /// Called by main plugin on startup
        /// </summary>
        public static void InitializeRelationships()
        {
            try
            {
                Logger.Info("Initializing nation relationship system...");
                ClearCaches();
                
                Logger.Debug($"Nation tag mappings initialized: {TagToNation.Count} entries");
                
                if (_alliancesAreAllied != null)
                {
                    Logger.Info("External alliances API is active");
                }
                else
                {
                    Logger.Debug("Using vanilla faction relations only");
                }
                
                Logger.Info("Nation relationship system initialized successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to initialize nation relationships");
            }
        }
    }
}