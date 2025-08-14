using System;
using System.Collections.Generic;
using VRage.Game.ModAPI;
using VRageMath;
using HeliosAI.Behaviors;

namespace Helios.Core.Interfaces
{
    public interface ICustomGridManager
    {
        bool RegisterPlayerGrid(string templateName, IMyCubeGrid grid, long playerId, string behaviorType = "idle");
        IMyCubeGrid SpawnFromTemplate(string templateName, Vector3D position, long requesterId);
        AiBehavior GetBehaviorForTemplate(string templateName, IMyCubeGrid grid);
        List<GridTemplate> GetPlayerTemplates(long playerId);
        bool RemovePlayerTemplate(string templateName, long playerId);
    }

    public class GridTemplate
    {
        public string Name { get; set; }
        public long Owner { get; set; }
        public string BehaviorType { get; set; }
        public int BlockCount { get; set; }
        public double Mass { get; set; }
        public GridCapabilities Capabilities { get; set; }
        public DateTime CreatedDate { get; set; }
        public string GridData { get; set; }
    }

    public class GridCapabilities
    {
        public int BlockCount { get; set; }
        public double Mass { get; set; }
        public bool HasWeapons { get; set; }
        public bool HasThrusters { get; set; }
        public bool HasJumpDrive { get; set; }
        public bool HasRadar { get; set; }
    }
}