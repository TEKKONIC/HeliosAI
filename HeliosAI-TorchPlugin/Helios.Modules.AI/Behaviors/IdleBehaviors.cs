using VRage.Game.ModAPI;
using System;
using System.Linq;
using NLog;
using Sandbox.ModAPI;

namespace HeliosAI.Behaviors
{
    public class IdleBehavior(IMyCubeGrid grid) : AiBehavior(grid)
    {
        private new static readonly Logger Logger = LogManager.GetLogger("IdleBehavior");
        
        public override string Name => "Idle";

        public override void Tick()
        {
            try
            {
                // Idle behavior - minimal activity
                // Could potentially do light housekeeping tasks here
                
                // Example: Stop autopilot if it's running
                if (Grid?.Physics != null && !Grid.MarkedForClose)
                {
                    try
                    {
                        var remote = Grid.GetFatBlocks<IMyRemoteControl>()
                            .FirstOrDefault(r => r?.IsFunctional == true);

                        if (remote?.IsAutoPilotEnabled == true)
                        {
                            remote.SetAutoPilotEnabled(false);
                            Logger.Debug($"[{Grid.DisplayName}] Disabled autopilot during idle");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"[{Grid?.DisplayName}] Error managing autopilot in idle behavior");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in IdleBehavior.Tick()");
            }
        }

        public override void Dispose()
        {
            try
            {
                Logger.Debug($"[{Grid?.DisplayName}] IdleBehavior disposed");
                base.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing IdleBehavior");
            }
        }
    }
}