using System;
using System.Collections.Generic;
using System.Linq;
using NLog;

namespace HeliosAI.Chat
{
    public static class NationMessageLibrary
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationMessageLibrary");

        public static readonly Dictionary<NationType, List<string>> WarningMessages = new()
        {
            [NationType.MCRN] = new()
            {
                "MCRN Command to intruder: You are in restricted space. Change course.",
                "Martian Navy: Cease approach or face engagement.",
                "MCRN Vessel: This is Martian territory. Alter course immediately.",
                "Mars Naval Command: Unauthorized vessel detected. Comply or be destroyed."
            },
            [NationType.UNN] = new()
            {
                "UNN Patrol: This sector is under Earth jurisdiction. Identify yourself.",
                "United Nations Navy: Stand down or be fired upon.",
                "Earth Command: You are violating UN space. Turn back now.",
                "UNN Security: Final warning. Change heading or face consequences."
            },
            [NationType.OPA] = new()
            {
                "OPA says turn 'round, beltalowda. Dis na your turf.",
                "You crossin' lines, inners. We watchin' ya.",
                "Belt says back off, coyo. Dis our rock.",
                "OPA warning: Stay clear or get spaced, sabe ke?"
            },
            [NationType.Unknown] = new()
            {
                "Unknown transmission received...",
                "Unidentified signal detected. Standby for response.",
                "Static interference... hostile intent detected...",
                "Encrypted warning broadcast intercepted..."
            }
        };

        public static readonly Dictionary<NationType, List<string>> ReinforcementMessages = new()
        {
            [NationType.MCRN] = new()
            {
                "MCRN broadcasting: Engaged with hostile, requesting backup.",
                "Martian ship requesting immediate reinforcement.",
                "Mars Command: Code Red. All available units respond.",
                "MCRN Mayday: Under attack. Requesting immediate assistance."
            },
            [NationType.UNN] = new()
            {
                "UNN channel: Enemy contact confirmed. Calling reinforcements.",
                "United Nations Command: Assistance requested at grid coordinates.",
                "Earth Fleet: All ships respond. Hostile engagement in progress.",
                "UNN Emergency: Backup required. Repeat, backup required."
            },
            [NationType.OPA] = new()
            {
                "Belter frendi, we need backup fast! Inners close by!",
                "OPA sending call to arm! Enemy sighted!",
                "Belt brothers, we under fire! Come quick, sabe ke?",
                "OPA distress: Need help now! Inners attacking our position!"
            },
            [NationType.Unknown] = new()
            {
                "Unknown transmission: Engaging target. Backup required.",
                "Static crackles across open comms...",
                "Encrypted distress signal detected...",
                "Garbled transmission... requesting assistance..."
            }
        };

        public static List<string> GetWarningMessages(NationType nation)
        {
            try
            {
                if (WarningMessages.TryGetValue(nation, out var messages))
                {
                    return messages ?? new List<string>();
                }

                Logger.Warn($"No warning messages found for nation: {nation}");
                return WarningMessages.GetValueOrDefault(NationType.Unknown, new List<string>());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get warning messages for nation: {nation}");
                return new List<string>();
            }
        }

        public static List<string> GetReinforcementMessages(NationType nation)
        {
            try
            {
                if (ReinforcementMessages.TryGetValue(nation, out var messages))
                {
                    return messages ?? new List<string>();
                }

                Logger.Warn($"No reinforcement messages found for nation: {nation}");
                return ReinforcementMessages.GetValueOrDefault(NationType.Unknown, new List<string>());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to get reinforcement messages for nation: {nation}");
                return new List<string>();
            }
        }

        public static bool HasMessages(NationType nation)
        {
            return WarningMessages.ContainsKey(nation) && ReinforcementMessages.ContainsKey(nation);
        }

        public static int GetTotalMessageCount()
        {
            try
            {
                var warningCount = WarningMessages.Values.Sum(list => list?.Count ?? 0);
                var reinforcementCount = ReinforcementMessages.Values.Sum(list => list?.Count ?? 0);
                return warningCount + reinforcementCount;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to calculate total message count");
                return 0;
            }
        }
    }
}