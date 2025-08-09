using System.ComponentModel;

namespace HeliosAI.Chat
{
    /// <summary>
    /// Represents the different nation types in The Expanse universe
    /// </summary>
    public enum NationType
    {
        /// <summary>
        /// Martian Congressional Republic Navy
        /// </summary>
        [Description("Martian Congressional Republic Navy")]
        MCRN = 0,

        /// <summary>
        /// United Nations Navy (Earth)
        /// </summary>
        [Description("United Nations Navy")]
        UNN = 1,

        /// <summary>
        /// Outer Planets Alliance (Belt)
        /// </summary>
        [Description("Outer Planets Alliance")]
        OPA = 2,

        /// <summary>
        /// Unknown or unidentified faction
        /// </summary>
        [Description("Unknown Faction")]
        Unknown = 99
    }

    /// <summary>
    /// Extension methods for NationType enum
    /// </summary>
    public static class NationTypeExtensions
    {
        /// <summary>
        /// Gets the display name for a nation type
        /// </summary>
        public static string GetDisplayName(this NationType nation)
        {
            return nation switch
            {
                NationType.MCRN => "Martian Congressional Republic Navy",
                NationType.UNN => "United Nations Navy",
                NationType.OPA => "Outer Planets Alliance",
                NationType.Unknown => "Unknown Faction",
                _ => nation.ToString()
            };
        }

        /// <summary>
        /// Gets the short code for a nation type
        /// </summary>
        public static string GetShortCode(this NationType nation)
        {
            return nation switch
            {
                NationType.MCRN => "MCRN",
                NationType.UNN => "UNN",
                NationType.OPA => "OPA",
                NationType.Unknown => "UNK",
                _ => "???"
            };
        }

        /// <summary>
        /// Checks if the nation is a recognized faction
        /// </summary>
        public static bool IsKnownFaction(this NationType nation)
        {
            return nation != NationType.Unknown;
        }
    }
}