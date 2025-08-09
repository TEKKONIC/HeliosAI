using System;
using System.Collections.Concurrent;
using System.Linq;
using HeliosAI.Behaviors;
using NLog;
using VRageMath;

namespace Helios.Modules.AICommunication
{
    public class AiCommunicationManager
    {
        private readonly ConcurrentDictionary<AiBehavior, byte> _agents = new ConcurrentDictionary<AiBehavior, byte>();
        private static readonly Logger Logger = LogManager.GetLogger("AiCommunicationManager");

        public void RegisterAgent(AiBehavior agent)
        {
            if (agent == null)
            {
                Logger.Warn("Attempted to register null agent");
                return;
            }

            try
            {
                Logger.Debug(_agents.TryAdd(agent, 0)
                    ? $"Agent registered: {agent.GetType().Name}"
                    : $"Agent already registered: {agent.GetType().Name}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to register agent");
            }
        }

        public void UnregisterAgent(AiBehavior agent)
        {
            if (agent == null) return;

            try
            {
                if (_agents.TryRemove(agent, out _))
                {
                    Logger.Debug($"Agent unregistered: {agent.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to unregister agent");
            }
        }

        public void RequestBackup(AiBehavior requester, Vector3D location)
        {
            if (requester == null)
            {
                Logger.Warn("Backup request from null requester");
                return;
            }

            try
            {
                var availableAgents = _agents.Keys.Where(a => a != requester && a.CanAssist).ToList();
                
                if (!availableAgents.Any())
                {
                    Logger.Debug("No available agents for backup request");
                    return;
                }

                foreach (var agent in availableAgents)
                {
                    try
                    {
                        agent.ReceiveBackupRequest(location);
                        Logger.Debug($"Backup request sent to: {agent.GetType().Name}");
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, $"Failed to send backup request to agent: {agent.GetType().Name}");
                    }
                }

                Logger.Info($"Backup requested at {location} by {requester.GetType().Name}, {availableAgents.Count} agents notified");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to process backup request");
            }
        }

        public int GetRegisteredAgentCount()
        {
            return _agents.Count;
        }

        public void Dispose()
        {
            try
            {
                _agents.Clear();
                Logger.Debug("AiCommunicationManager disposed successfully");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to dispose AiCommunicationManager properly");
            }
        }
    }
}