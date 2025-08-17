using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using HeliosAI.Phrases;
using NLog;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Ingame;
using VRageMath;

namespace Helios.Modules.AI
{
    public class BroadcastManager
    {
        private static readonly Logger Logger = LogManager.GetLogger("BroadcastManager");
        private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
        private TimeSpan _defaultCooldown = TimeSpan.FromSeconds(30);
        private const double DefaultBroadcastRange = 2000.0;

        public void Broadcast(NpcEntity npc, string trigger, Dictionary<string, string> context)
        {
            if (npc == null)
            {
                Logger.Warn("Attempted to broadcast with null NPC entity");
                return;
            }

            if (string.IsNullOrEmpty(trigger))
            {
                Logger.Warn("Attempted to broadcast with null or empty trigger");
                return;
            }

            try
            {
                var pack = NationPhrasePackLoader.GetPhrasePack(npc.NationTag ?? "Generic");
                if (pack == null)
                {
                    Logger.Warn($"No phrase pack found for nation '{npc.NationTag ?? "Generic"}'");
                    return;
                }

                var phrases = pack.GetPhrasesForTrigger(trigger);
                if (phrases == null || phrases.Count == 0)
                {
                    Logger.Warn($"No phrases found for trigger '{trigger}' in nation pack '{npc.NationTag ?? "Generic"}'");
                    return;
                }

                var message = SubstituteVariables(
                    phrases[new Random().Next(phrases.Count)], context ?? new Dictionary<string, string>()
                );

                SendToPlayersNearby(((IMyEntity)npc.Grid).GetPosition(), message);
                var key = $"{npc.Id}_{trigger}";

                _cooldowns[key] = DateTime.UtcNow;

                Logger.Info($"Broadcast sent from NPC {npc.Id} ({npc.NationTag}): {message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to broadcast from NPC {npc?.Id}, trigger: {trigger}");
            }
        }

        private bool IsCooldownOver(string key)
        {
            if (_cooldowns.TryGetValue(key, out var lastTime))
            {
                return DateTime.UtcNow - lastTime >= _defaultCooldown;
            }
            return true;
        }

        private static void SendToPlayersNearby(Vector3D position, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                Logger.Warn("Attempted to send empty message to players");
                return;
            }

            try
            {
                var players = new List<IMyPlayer>();
                MyAPIGateway.Players.GetPlayers(players, p =>
                    p?.Character != null && Vector3D.Distance(p.GetPosition(), position) <= DefaultBroadcastRange);

                if (!players.Any())
                {
                    Logger.Debug($"No players within range ({DefaultBroadcastRange}m) for broadcast");
                    return;
                }

                foreach (var player in players)
                {
                    try
                    {
                        MyAPIGateway.Utilities.ShowMessage("Helios AI", message);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to send message to player: {player?.DisplayName}");
                    }
                }

                Logger.Debug($"Message sent to {players.Count} nearby players");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to send message to nearby players");
            }
        }

        private string SubstituteVariables(string message, Dictionary<string, string> context)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            if (context == null || context.Count == 0)
                return message;

            try
            {
                foreach (var kv in context)
                {
                    if (!string.IsNullOrEmpty(kv.Key))
                    {
                        message = message.Replace($"{{{kv.Key}}}", kv.Value ?? string.Empty);
                    }
                }

                return message;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to substitute variables in message");
                return message;
            }
        }

        public void SetCooldown(TimeSpan newCooldown)
        {
            try
            {
                _defaultCooldown = newCooldown;
                Logger.Info($"Broadcast cooldown set to: {newCooldown.TotalSeconds} seconds");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set broadcast cooldown");
            }
        }

        public void ClearCooldowns()
        {
            try
            {
                _cooldowns.Clear();
                Logger.Info("All broadcast cooldowns cleared");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to clear broadcast cooldowns");
            }
        }

        public int GetActiveCooldownCount()
        {
            return _cooldowns.Count;
        }

        public void SendGlobal(string message) {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (var player in players) {
                MyAPIGateway.Utilities.ShowMessage("Helios", message);
            }
        }
    }
}