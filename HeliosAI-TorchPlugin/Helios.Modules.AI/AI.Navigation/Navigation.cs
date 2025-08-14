using System;
using System.Linq;
using NLog;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Helios.Modules.AI.Navigation
{
    public sealed class NavigationService
    {
        private static readonly Logger Log = LogManager.GetLogger("NavigationService");
        public static NavigationService Instance { get; } = new NavigationService();

        private NavigationService() { }

        public void Steer(IMyCubeGrid grid, Vector3D target, float maxSpeed, float arriveDist)
        {
            if (grid == null || grid.MarkedForClose) return;

            try
            {
                var dist = Vector3D.Distance(grid.GetPosition(), target);
                if (dist <= arriveDist) return;

                var rc = grid.GetFatBlocks<IMyRemoteControl>().FirstOrDefault();
                if (rc == null)
                {
                    Log.Warn($"No remote control found on grid '{grid.DisplayName}'");
                    return;
                }

                rc.ClearWaypoints();
                rc.AddWaypoint(target, "AI_Target");
                rc.SetAutoPilotEnabled(true);
                rc.FlightMode = Sandbox.ModAPI.Ingame.FlightMode.OneWay;
                rc.SpeedLimit = maxSpeed;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Steer failed for grid: {grid?.DisplayName}");
            }
        }
    }
}