using System;
using System.Collections.Generic;
using NLog;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace HeliosAI.Chat
{
    public static class NationBroadcastManager
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationBroadcastManager");
        private static DateTime _lastBroadcast = DateTime.MinValue;
        private static TimeSpan _cooldown = TimeSpan.FromSeconds(20);
        private static readonly Random _random = new Random();

        public static void BroadcastWarning(NationType nation, IMyCubeGrid sourceGrid)
        {
            if (sourceGrid == null)
            {
                Logger.Warn("Attempted to broadcast warning with null source grid");
                return;
            }

            if (!IsCooldownOver())
            {
                Logger.Debug($"Warning broadcast blocked by cooldown for nation: {nation}");
                return;
            }

            try
            {
                var messages = NationMessageLibrary.WarningMessages.GetValueOrDefault(nation);
                if (messages == null || messages.Count == 0)
                {
                    Logger.Warn($"No warning messages found for nation: {nation}");
                    return;
                }

                var message = GetRandom(messages);
                MyAPIGateway.Utilities.ShowMessage("AI Broadcast", message);
                _lastBroadcast = DateTime.UtcNow;
                
                Logger.Info($"Warning broadcast sent for nation {nation}: {message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to broadcast warning for nation: {nation}");
            }
        }

        public static void BroadcastReinforcementCall(NationType nation, IMyCubeGrid sourceGrid)
        {
            if (sourceGrid == null)
            {
                Logger.Warn("Attempted to broadcast reinforcement call with null source grid");
                return;
            }

            if (!IsCooldownOver())
            {
                Logger.Debug($"Reinforcement broadcast blocked by cooldown for nation: {nation}");
                return;
            }

            try
            {
                var messages = NationMessageLibrary.ReinforcementMessages.GetValueOrDefault(nation);
                if (messages == null || messages.Count == 0)
                {
                    Logger.Warn($"No reinforcement messages found for nation: {nation}");
                    return;
                }

                var message = GetRandom(messages);
                MyAPIGateway.Utilities.ShowMessage("AI Broadcast", message);
                _lastBroadcast = DateTime.UtcNow;
                
                Logger.Info($"Reinforcement broadcast sent for nation {nation}: {message}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to broadcast reinforcement call for nation: {nation}");
            }
        }

        private static bool IsCooldownOver()
        {
            return DateTime.UtcNow - _lastBroadcast > _cooldown;
        }

        private static string GetRandom(List<string> list)
        {
            if (list == null || list.Count == 0)
                return string.Empty;
                
            return list[_random.Next(list.Count)];
        }

        public static void SetCooldown(TimeSpan newCooldown)
        {
            try
            {
                _cooldown = newCooldown;
                Logger.Info($"Broadcast cooldown set to: {newCooldown.TotalSeconds} seconds");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set broadcast cooldown");
            }
        }

        public static TimeSpan GetRemainingCooldown()
        {
            var elapsed = DateTime.UtcNow - _lastBroadcast;
            return elapsed < _cooldown ? _cooldown - elapsed : TimeSpan.Zero;
        }
    }
}