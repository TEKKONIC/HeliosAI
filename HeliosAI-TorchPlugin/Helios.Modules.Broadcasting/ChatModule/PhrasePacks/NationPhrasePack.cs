using System.Collections.Generic;
using NLog;

namespace HeliosAI.Broadcasting
{
    /// <summary>
    /// Represents a phrase pack for a specific nation, including triggers and broadcast phrases.
    /// </summary>
    public class NationPhrasePack
    {
        private static readonly Logger Logger = LogManager.GetLogger("NationPhrasePack");

        private string _nation;
        private Dictionary<string, List<string>> _phrases = new();
        private Dictionary<string, List<string>> _triggers = new();

        /// <summary>
        /// Nation identifier (e.g., "MCRN", "UNN", "OPA")
        /// </summary>
        public string Nation
        {
            get => _nation;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    Logger.Warn("Attempted to set empty nation identifier.");
                    value = "Unknown";
                }
                _nation = value;
            }
        }

        /// <summary>
        /// Dictionary of phrase categories and their associated phrases.
        /// </summary>
        public Dictionary<string, List<string>> Phrases
        {
            get => _phrases;
            set => _phrases = value ?? new Dictionary<string, List<string>>();
        }

        /// <summary>
        /// Dictionary of triggers and their associated phrases.
        /// </summary>
        public Dictionary<string, List<string>> Triggers
        {
            get => _triggers;
            set => _triggers = value ?? new Dictionary<string, List<string>>();
        }

        /// <summary>
        /// Gets phrases for a specific trigger.
        /// </summary>
        public List<string> GetPhrasesForTrigger(string trigger)
        {
            if (string.IsNullOrWhiteSpace(trigger))
            {
                Logger.Warn("Trigger is null or empty.");
                return new List<string>();
            }

            if (_triggers.TryGetValue(trigger, out var phrases))
                return phrases ?? new List<string>();

            Logger.Debug($"No phrases found for trigger: {trigger}");
            return new List<string>();
        }

        /// <summary>
        /// Gets phrases for a specific category (alias for trigger).
        /// </summary>
        public List<string> GetPhrasesForCategory(string category)
        {
            return GetPhrasesForTrigger(category);
        }

        /// <summary>
        /// Adds a phrase to a trigger.
        /// </summary>
        public void AddPhrase(string trigger, string phrase)
        {
            if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(phrase))
            {
                Logger.Warn("Cannot add phrase: trigger or phrase is empty.");
                return;
            }

            if (!_triggers.ContainsKey(trigger))
                _triggers[trigger] = new List<string>();

            _triggers[trigger].Add(phrase);
            Logger.Debug($"Added phrase to trigger '{trigger}': {phrase}");
        }
    }
}