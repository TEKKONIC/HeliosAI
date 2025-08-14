using System.ComponentModel;

namespace Helios.Modules.Nations
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
}