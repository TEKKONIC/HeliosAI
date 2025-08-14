using System.Collections.Generic;
using Helios.Modules.AI.Agents;
using NLog;

namespace Helios.Modules.AI.Runtime
{
    public class AiUpdateScheduler
    {
        private static readonly Logger Log = LogManager.GetLogger("AiUpdateScheduler");
        private readonly List<NpcAgent> _agents = new();

        public void Register(NpcAgent agent) => _agents.Add(agent);
        public void Unregister(NpcAgent agent) => _agents.Remove(agent);

        public void UpdateAll()
        {
            foreach (var agent in _agents)
            {
                try { agent.Tick(); } catch (System.Exception ex) { Log.Error(ex, "Agent update failed"); }
            }
        }
    }
}