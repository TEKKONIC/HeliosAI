using System.Collections.Generic;
using System.Security.Policy;
using Helios.Modules.AI;
using Helios.Modules.AI.Combat;
using Helios.Modules.Encounters;
using HeliosAI.NPCZones;

namespace Helios.Core {
    public static class HeliosAPI
    {
        public static AiManager AI => (AiManager)HeliosContext.Instance.AiManager;
        public static PredictiveAnalyzer Analyzer => AiManager.Instance._predictiveAnalyzer;
        public static BroadcastManager Broadcast => HeliosContext.Instance.BroadcastManager;
        public static EncounterManager Encounters => HeliosContext.Instance.EncounterManager as EncounterManager;
        public static ZoneManager Zones => HeliosContext.Instance.ZoneManager as ZoneManager;
        // Add more internal managers/services as needed
        
        /// <summary>
        /// Register a new NPC with the AI system.
        /// </summary>
        public static void RegisterNpc(NpcEntity npc) => AI.RegisterNpc(npc);

        /// <summary>
        /// Set the mood of an NPC.
        /// </summary>
        public static void SetNpcMood(NpcEntity npc, NpcEntity.AiMood mood) => AI.SetNpcMood(npc, mood);

        /// <summary>
        /// Get all active NPCs.
        /// </summary>
        public static IReadOnlyList<NpcEntity> GetActiveNpcs() => AI.ActiveNpcs;

        /// <summary>
        /// Broadcast a message to all players.
        /// </summary>
        public static void BroadcastMessage(string message) => Broadcast?.SendGlobal(message);

        /// <summary>
        /// Get the zone for a given position.
        /// </summary>
        public static ZoneProfile GetZoneForPosition(VRageMath.Vector3D position) => Zones?.GetZoneForPosition(position);

        // Add more methods as needed for your mod's features!
    }
}