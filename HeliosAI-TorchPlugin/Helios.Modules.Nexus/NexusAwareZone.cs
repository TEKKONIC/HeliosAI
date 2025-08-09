using System;
using NLog;
using VRageMath;
using VRage.Game.ModAPI;

namespace Helios.Modules.Nexus
{
    public class NexusAwareZone
    {
        private static readonly Logger Logger = LogManager.GetLogger("NexusAwareZone");
        
        public string Name { get; private set; }
        public Vector3D Center { get; private set; }
        public double Radius { get; private set; }
        public string ServerId { get; private set; }
        public bool IsActive { get; set; } = true;
        public DateTime LastUpdate { get; private set; }

        public NexusAwareZone(string name, Vector3D center, double radius, string serverId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Zone name cannot be null or empty", nameof(name));
            
            if (radius <= 0)
                throw new ArgumentException("Zone radius must be positive", nameof(radius));

            try
            {
                Name = name;
                Center = center;
                Radius = radius;
                ServerId = serverId ?? "local";
                LastUpdate = DateTime.UtcNow;
                
                Logger.Info($"Created Nexus-aware zone: {Name} at {Center} with radius {Radius}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to create Nexus-aware zone: {name}");
                throw;
            }
        }

        public bool ContainsPosition(Vector3D position)
        {
            try
            {
                var distance = Vector3D.Distance(Center, position);
                return distance <= Radius;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check if position {position} is in zone {Name}");
                return false;
            }
        }

        public bool ContainsGrid(IMyCubeGrid grid)
        {
            if (grid == null)
            {
                Logger.Warn("Attempted to check zone containment for null grid");
                return false;
            }

            try
            {
                var gridPosition = grid.GetPosition();
                return ContainsPosition(gridPosition);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to check if grid {grid.DisplayName} is in zone {Name}");
                return false;
            }
        }

        public double GetDistanceToPosition(Vector3D position)
        {
            try
            {
                return Vector3D.Distance(Center, position);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to calculate distance to position {position} from zone {Name}");
                return double.MaxValue;
            }
        }

        public void UpdateCenter(Vector3D newCenter)
        {
            try
            {
                var oldCenter = Center;
                Center = newCenter;
                LastUpdate = DateTime.UtcNow;
                
                Logger.Debug($"Zone {Name} center updated from {oldCenter} to {newCenter}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to update center for zone {Name}");
            }
        }

        public void UpdateRadius(double newRadius)
        {
            if (newRadius <= 0)
            {
                Logger.Warn($"Attempted to set invalid radius {newRadius} for zone {Name}");
                return;
            }

            try
            {
                var oldRadius = Radius;
                Radius = newRadius;
                LastUpdate = DateTime.UtcNow;
                
                Logger.Debug($"Zone {Name} radius updated from {oldRadius} to {newRadius}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to update radius for zone {Name}");
            }
        }

        public void SetServerId(string serverId)
        {
            try
            {
                var oldServerId = ServerId;
                ServerId = serverId ?? "local";
                LastUpdate = DateTime.UtcNow;
                
                Logger.Debug($"Zone {Name} server ID updated from {oldServerId} to {ServerId}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to update server ID for zone {Name}");
            }
        }

        public bool IsOnSameServer(string serverId)
        {
            return string.Equals(ServerId, serverId, StringComparison.OrdinalIgnoreCase);
        }

        public override string ToString()
        {
            return $"Zone '{Name}' [{ServerId}] - Center: {Center}, Radius: {Radius}m, Active: {IsActive}";
        }

        public override bool Equals(object obj)
        {
            if (obj is NexusAwareZone other)
            {
                return string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(ServerId, other.ServerId, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Name?.ToLowerInvariant()?.GetHashCode() ?? 0);
                hash = hash * 23 + (ServerId?.ToLowerInvariant()?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}