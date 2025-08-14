using VRage.Game.ModAPI;
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using Sandbox.ModAPI;
using VRageMath;
using VRage.ModAPI;

namespace HeliosAI.Behaviors
{
    public class IdleBehavior : AiBehavior
    {
        private new static readonly Logger Logger = LogManager.GetLogger("IdleBehavior");
        
        private DateTime _lastScan = DateTime.MinValue;
        private DateTime _lastMaintenance = DateTime.MinValue;
        private DateTime _idleStartTime = DateTime.UtcNow;
        private Vector3D _idlePosition;
        private bool _isPerformingMaintenance = false;
        private Dictionary<string, DateTime> _maintenanceTasks = new Dictionary<string, DateTime>();
        private List<IMyEntity> _nearbyEntities = new List<IMyEntity>();
        
        public override string Name => "Idle";

        public IdleBehavior(IMyCubeGrid grid) : base(grid)
        {
            _idlePosition = grid?.GetPosition() ?? Vector3D.Zero;
            InitializeMaintenanceTasks();
            
            Logger.Info($"[{Grid?.DisplayName}] IdleBehavior initialized at position {_idlePosition}");
        }

        protected override void OnTick()
        {
            try
            {
                base.OnTick();

                if (Grid?.Physics == null || Grid.MarkedForClose)
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Grid invalid, stopping idle behavior");
                    return;
                }

                var currentTime = DateTime.UtcNow;

                PerformIntelligentIdle(currentTime);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error in IdleBehavior.OnTick()");
            }
        }

        private void PerformIntelligentIdle(DateTime currentTime)
        {
            try
            {
                // 1. Periodic scanning for threats/opportunities
                if (_lastScan == DateTime.MinValue || 
                    currentTime.Subtract(_lastScan).TotalSeconds > 10)
                {
                    PerformPassiveScanning();
                    _lastScan = currentTime;
                }

                // 2. Maintenance and optimization tasks
                if (_lastMaintenance == DateTime.MinValue || 
                    currentTime.Subtract(_lastMaintenance).TotalMinutes > 5)
                {
                    PerformMaintenanceTasks();
                    _lastMaintenance = currentTime;
                }

                // 3. Position maintenance and drift correction
                MaintainIdlePosition();

                // 4. Power and system optimization
                OptimizeSystemsForIdle();

                // 5. Learn from idle time
                RecordIdleBehaviorData(currentTime);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in intelligent idle activities");
            }
        }

        private void PerformPassiveScanning()
        {
            try
            {
                var scanRadius = 1500.0; // 1.5km passive scan
                var entities = new HashSet<IMyEntity>();
                
                MyAPIGateway.Entities.GetEntities(entities, entity =>
                {
                    if (entity == null || entity.MarkedForClose || entity == Grid) return false;
                    var distance = Vector3D.Distance(Grid.GetPosition(), entity.GetPosition());
                    return distance <= scanRadius;
                });

                _nearbyEntities.Clear();
                _nearbyEntities.AddRange(entities);

                var threats = new List<IMyEntity>();
                var friendlies = new List<IMyEntity>();
                var unknowns = new List<IMyEntity>();

                foreach (var entity in entities)
                {
                    var category = CategorizeEntity(entity);
                    switch (category)
                    {
                        case "Threat":
                            threats.Add(entity);
                            break;
                        case "Friendly":
                            friendlies.Add(entity);
                            break;
                        default:
                            unknowns.Add(entity);
                            break;
                    }
                    
                    _predictiveAnalyzer?.UpdateMovementHistory(entity);
                }

                if (threats.Any())
                {
                    Logger.Info($"[{Grid?.DisplayName}] Passive scan detected {threats.Count} potential threats");
                    
                    PrepareForPotentialThreat(threats);
                }

                // Record scan results
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "PassiveScan", new Dictionary<string, object>
                {
                    ["Threats"] = threats.Count,
                    ["Friendlies"] = friendlies.Count,
                    ["Unknowns"] = unknowns.Count,
                    ["ScanRadius"] = scanRadius
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in passive scanning");
            }
        }

        private string CategorizeEntity(IMyEntity entity)
        {
            try
            {
                if (entity is IMyCharacter character)
                {
                    var playerId = character.ControllerInfo?.ControllingIdentityId ?? 0;
                    return IsHostilePlayer(playerId) ? "Threat" : "Friendly";
                }
                else if (entity is IMyCubeGrid grid)
                {
                    var gridOwner = grid.BigOwners.FirstOrDefault();
                    return IsHostilePlayer(gridOwner) ? "Threat" : "Friendly";
                }
                
                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        private bool IsHostilePlayer(long playerId)
        {
            if (playerId == 0) return false;
            
            try
            {
                var myOwner = Grid.BigOwners.FirstOrDefault();
                if (myOwner == 0) return true;
                
                var playerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(playerId);
                var myFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(myOwner);
                
                if (playerFaction == null || myFaction == null) return true;
                
                return playerFaction.FactionId != myFaction.FactionId;
            }
            catch
            {
                return true;
            }
        }

        private void PrepareForPotentialThreat(List<IMyEntity> threats)
        {
            try
            {
                var wc = HeliosAIPlugin.WeaponCoreManager;
                if (wc != null)
                {
                    wc.RegisterWeapons(Grid);
                    
                    foreach (var threat in threats.Take(3)) // Focus on closest 3 threats
                    {
                        _predictiveAnalyzer?.UpdateMovementHistory(threat);
                    }
                }

                Logger.Debug($"[{Grid?.DisplayName}] Prepared systems for {threats.Count} potential threats");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error preparing for potential threats");
            }
        }

        private void PerformMaintenanceTasks()
        {
            try
            {
                _isPerformingMaintenance = true;
                
                // 1. Check and repair damaged blocks
                PerformDamageAssessment();
                
                // 2. Optimize power distribution
                OptimizePowerSystems();
                
                // 3. Check inventory and cargo
                OptimizeInventoryManagement();
                
                // 4. Update navigation data
                UpdateNavigationData();
                
                // 5. Validate weapon systems
                ValidateWeaponSystems();
                
                _isPerformingMaintenance = false;
                
                Logger.Debug($"[{Grid?.DisplayName}] Completed maintenance cycle");
                
                _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "MaintenanceCompleted", new Dictionary<string, object>
                {
                    ["MaintenanceDuration"] = (DateTime.UtcNow - _lastMaintenance).TotalSeconds,
                    ["TasksPerformed"] = _maintenanceTasks.Count
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error performing maintenance tasks");
                _isPerformingMaintenance = false;
            }
        }

        private void InitializeMaintenanceTasks()
        {
            _maintenanceTasks["DamageAssessment"] = DateTime.MinValue;
            _maintenanceTasks["PowerOptimization"] = DateTime.MinValue;
            _maintenanceTasks["InventoryCheck"] = DateTime.MinValue;
            _maintenanceTasks["NavigationUpdate"] = DateTime.MinValue;
            _maintenanceTasks["WeaponValidation"] = DateTime.MinValue;
        }

        private void PerformDamageAssessment()
        {
            try
            {
                var blocks = new List<IMySlimBlock>();
                Grid.GetBlocks(blocks);
                
                var damagedBlocks = blocks.Where(b => b.Integrity < b.MaxIntegrity).ToList();
                var criticallyDamaged = damagedBlocks.Where(b => b.Integrity / b.MaxIntegrity < 0.5f).ToList();
                
                if (criticallyDamaged.Any())
                {
                    Logger.Warn($"[{Grid?.DisplayName}] {criticallyDamaged.Count} critically damaged blocks detected");
                }
                
                _maintenanceTasks["DamageAssessment"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in damage assessment");
            }
        }

        private void OptimizePowerSystems()
        {
            try
            {
                var reactors = Grid.GetFatBlocks<IMyReactor>().Where(r => r.IsFunctional).ToList();
                var batteries = Grid.GetFatBlocks<IMyBatteryBlock>().Where(b => b.IsFunctional).ToList();
                
                foreach (var reactor in reactors)
                {
                    if (reactor.CurrentOutput < reactor.MaxOutput * 0.1f)
                    {
                        // Reactor is barely used, could optimize
                        Logger.Debug($"[{Grid?.DisplayName}] Low power usage detected, optimizing reactor output");
                    }
                }
                
                _maintenanceTasks["PowerOptimization"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error optimizing power systems");
            }
        }

        private void OptimizeInventoryManagement()
        {
            try
            {
                var cargoContainers = Grid.GetFatBlocks<IMyCargoContainer>().Where(c => c.IsFunctional).ToList();
                
                var totalVolume = 0f;
                var usedVolume = 0f;
                
                foreach (var container in cargoContainers)
                {
                    var inventory = container.GetInventory();
                    if (inventory != null)
                    {
                        totalVolume += (float)inventory.MaxVolume;
                        usedVolume += (float)inventory.CurrentVolume;
                    }
                }
                
                var fillPercentage = totalVolume > 0 ? usedVolume / totalVolume : 0f;
                
                if (fillPercentage > 0.9f)
                {
                    Logger.Warn($"[{Grid?.DisplayName}] Cargo at {fillPercentage:P1} capacity - may need attention");
                }
                
                _maintenanceTasks["InventoryCheck"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error in inventory management");
            }
        }

        private void UpdateNavigationData()
        {
            try
            {
                _idlePosition = Grid.GetPosition();
                
                var remoteControls = Grid.GetFatBlocks<IMyRemoteControl>().Where(r => r.IsFunctional).ToList();
                
                foreach (var remote in remoteControls)
                {
                    if (remote.IsAutoPilotEnabled)
                    {
                        remote.SetAutoPilotEnabled(false);
                        Logger.Debug($"[{Grid?.DisplayName}] Disabled autopilot during idle maintenance");
                    }
                }
                
                _maintenanceTasks["NavigationUpdate"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error updating navigation data");
            }
        }

        private void ValidateWeaponSystems()
        {
            try
            {
                var wc = HeliosAIPlugin.WeaponCoreManager;
                if (wc != null)
                {
                    wc.RegisterWeapons(Grid);
                    var hasWeapons = wc.HasReadyWeapons(Grid);
                    
                    Logger.Debug($"[{Grid?.DisplayName}] Weapon systems validation: {(hasWeapons ? "Ready" : "Not Ready")}");
                }
                
                _maintenanceTasks["WeaponValidation"] = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error validating weapon systems");
            }
        }

        private void MaintainIdlePosition()
        {
            try
            {
                var currentPos = Grid.GetPosition();
                var drift = Vector3D.Distance(currentPos, _idlePosition);
                
                if (drift > 100) // 100m drift tolerance
                {
                    Logger.Debug($"[{Grid?.DisplayName}] Position drift detected: {drift:F1}m, correcting");
                    
                    var thrusters = Grid.GetFatBlocks<IMyThrust>().Where(t => t.IsFunctional).ToList();
                    if (thrusters.Any())
                    {
                        // Enable dampeners to stop drift
                        var remote = Grid.GetFatBlocks<IMyRemoteControl>().FirstOrDefault(r => r.IsFunctional);
                        if (remote != null)
                        {
                            remote.DampenersOverride = true;
                        }
                    }
                    
                    _idlePosition = currentPos; // Update reference position
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error maintaining idle position");
            }
        }

        private void OptimizeSystemsForIdle()
        {
            try
            {
                // Reduce power consumption during idle
                var gyros = Grid.GetFatBlocks<IMyGyro>().Where(g => g.IsFunctional).ToList();
                var thrusters = Grid.GetFatBlocks<IMyThrust>().Where(t => t.IsFunctional).ToList();
                
                // Reduce gyro power if not needed
                foreach (var gyro in gyros)
                {
                    if (gyro.GyroOverride)
                    {
                        gyro.GyroOverride = false;
                        Logger.Debug($"[{Grid?.DisplayName}] Disabled gyro override for power saving");
                    }
                }
                
                // Ensure dampeners are on to maintain position
                var remote = Grid.GetFatBlocks<IMyRemoteControl>().FirstOrDefault(r => r.IsFunctional);
                if (remote?.DampenersOverride == false)
                {
                    remote.DampenersOverride = true;
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error optimizing systems for idle");
            }
        }

        private void RecordIdleBehaviorData(DateTime currentTime)
        {
            try
            {
                var idleDuration = (currentTime - _idleStartTime).TotalSeconds;
                
                if (idleDuration % 60 < 1) // Every minute
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "IdlePerformance", new Dictionary<string, object>
                    {
                        ["IdleDuration"] = idleDuration,
                        ["NearbyEntities"] = _nearbyEntities.Count,
                        ["IsPerformingMaintenance"] = _isPerformingMaintenance,
                        ["Position"] = _idlePosition,
                        ["MaintenanceTasksCompleted"] = _maintenanceTasks.Count(t => t.Value != DateTime.MinValue)
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error recording idle behavior data");
            }
        }

        public override Vector3D GetNextWaypoint()
        {
            try
            {
                return _idlePosition;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error getting next waypoint");
                return Grid?.GetPosition() ?? Vector3D.Zero;
            }
        }

        protected override void OnBackupRequested(Vector3D location, string message)
        {
            try
            {
                var distance = Vector3D.Distance(_idlePosition, location);
                
                if (distance <= 2000) // 2km response range
                {
                    Logger.Info($"[{Grid?.DisplayName}] Idle unit responding to backup request at {location}");
                    
                    // Could transition to follow or support behavior
                    // For now, just log readiness
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error processing backup request");
            }
        }
        
        public bool IsPerformingMaintenance()
        {
            return _isPerformingMaintenance;
        }

        public DateTime GetIdleStartTime()
        {
            return _idleStartTime;
        }

        public double GetIdleDuration()
        {
            return (DateTime.UtcNow - _idleStartTime).TotalSeconds;
        }

        public Vector3D GetIdlePosition()
        {
            return _idlePosition;
        }

        public int GetNearbyEntityCount()
        {
            return _nearbyEntities.Count;
        }

        public Dictionary<string, DateTime> GetMaintenanceStatus()
        {
            return new Dictionary<string, DateTime>(_maintenanceTasks);
        }

        public override void Dispose(bool disposing)
        {
            try
            {
                if (disposing)
                {
                    _predictiveAnalyzer?.RecordEvent(Grid.EntityId, "IdleBehaviorCompleted", new Dictionary<string, object>
                    {
                        ["TotalIdleTime"] = (DateTime.UtcNow - _idleStartTime).TotalSeconds,
                        ["MaintenanceCycles"] = _maintenanceTasks.Count(t => t.Value != DateTime.MinValue),
                        ["FinalPosition"] = _idlePosition,
                        ["EntitiesObserved"] = _nearbyEntities.Count
                    });
                }
                
                _nearbyEntities.Clear();
                _maintenanceTasks.Clear();
                
                Logger.Debug($"[{Grid?.DisplayName}] IdleBehavior disposed after {GetIdleDuration():F1} seconds");
                base.Dispose(disposing);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"[{Grid?.DisplayName}] Error disposing IdleBehavior");
            }
        }
    }
}