using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Core;
using Helios.Modules.AI;
using HeliosAI.Behaviors;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Helios.Plugin.Base.Commands
{
    [Category("helios")]
    public class AiDebugCommands : CommandModule
    {
        [Command("ai register", "Register the targeted grid with Helios AI system")]
        [Permission(MyPromoteLevel.Admin)]
        public void RegisterTargetedGrid(string behaviorType = "idle")
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("This command must be run by a player.");
                return;
            }

            if (player.Controller == null || player.Controller.ControlledEntity == null)
            {
                Context.Respond("You must be seated in a cockpit or control block.");
                return;
            }

            var entity = player.Controller.ControlledEntity.Entity;
            if (entity == null)
            {
                Context.Respond("No controlled entity found.");
                return;
            }

            var target = entity.GetTopMostParent() as IMyCubeGrid;
            if (target == null)
            {
                Context.Respond("No valid grid targeted. Make sure you're controlling a grid.");
                return;
            }

            var aiManager = HeliosContext.Instance?.AiManager;
            if (aiManager == null)
            {
                Context.Respond("AI Manager not available.");
                return;
            }

            if (aiManager.IsRegistered(target))
            {
                Context.Respond($"Grid '{target.DisplayName}' is already registered.");
                return;
            }

            var behavior = CreateBehaviorForGrid(target, behaviorType?.ToLower());
            if (behavior == null)
            {
                Context.Respond($"Invalid behavior type '{behaviorType}'. Use: idle, patrol, attack, defense");
                return;
            }

            var capabilities = AnalyzeGridCapabilities(target);
            if (!capabilities.HasRemoteControl)
            {
                Context.Respond("This grid does not contain a Remote Control block and cannot be registered with Helios AI.");
                return;
            }

            Context.Respond($"Grid Analysis: {capabilities.BlockCount} blocks, " +
                            $"Weapons: {(capabilities.HasWeapons ? "Yes" : "No")}, " +
                            $"Thrusters: {(capabilities.HasThrusters ? "Yes" : "No")}, " +
                            $"Remote Control: {(capabilities.HasRemoteControl ? "Yes" : "No")}");

            try
            {
                aiManager.RegisterGrid(target, behavior);
                Context.Respond($"Grid '{target.DisplayName}' registered with {behaviorType} behavior.");
            }
            catch (Exception ex)
            {
                Context.Respond($"Failed to register grid: {ex.Message}");
            }
        }
        
        [Command("ai unregister", "Unregister the targeted grid from Helios AI system")]
        [Permission(MyPromoteLevel.Admin)]
        public void UnregisterTargetedGrid()
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("This command must be run by a player.");
                return;
            }

            var target = player.Controller.ControlledEntity?.Entity?.GetTopMostParent() as IMyCubeGrid;
            if (target == null)
            {
                Context.Respond("No valid grid targeted.");
                return;
            }

            var aiManager = HeliosContext.Instance.AiManager;

            if (!aiManager.IsRegistered(target))
            {
                Context.Respond($"Grid '{target.DisplayName}' is not currently registered.");
                return;
            }

            aiManager.UnregisterGrid(target);
            Context.Respond($"Grid '{target.DisplayName}' unregistered from AI system.");
        }
        
        [Command("ai setbehavior", "Set the AI behavior for a grid. Usage: /helios ai setbehavior [behavior] [optional gridName]")]
        [Permission(MyPromoteLevel.Admin)]
        public void SetBehavior(string behaviorType, string gridName = null)
        {
            var aiManager = HeliosContext.Instance.AiManager;

            var grid = FindGrid(gridName, Context.Player);
            if (grid == null)
            {
                Context.Respond("Unable to find target grid.");
                return;
            }

            if (!aiManager.IsRegistered(grid))
            {
                Context.Respond($"Grid '{grid.DisplayName}' is not registered with Helios AI.");
                return;
            }

            AiBehavior newBehavior = behaviorType.ToLower() switch
            {
                "idle"    => new IdleBehavior(grid),
                "patrol"  => new PatrolBehavior(grid, new List<Vector3D>()),
                "attack"  => new AttackBehavior(grid, null),
                "defense" => new DefenseBehavior(grid , grid.GetPosition()),
                _         => null
            };

            if (newBehavior == null)
            {
                Context.Respond($"Unknown behavior type '{behaviorType}'. Use: idle, patrol, attack, defense.");
                return;
            }

            aiManager.SetBehavior(grid, newBehavior);
            Context.Respond($"Behavior set to '{behaviorType}' for grid '{grid.DisplayName}'.");
        }

        
        [Command("ai list", "List registered AI grids. Optionally filter by name.")]
        [Permission(MyPromoteLevel.Admin)]
        public void ListRegisteredGrids(string filter = null)
        {
            var aiManager = AiManager.Instance; 
            var all = aiManager.ActiveNpcs;

            var results = string.IsNullOrEmpty(filter)
                ? all
                : all.Where(a => a.Grid?.DisplayName?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            if (results.Count == 0)
            {
                Context.Respond("No matching AI-enabled grids found.");
                return;
            }

            Context.Respond($"Found {results.Count} AI grid(s):");
            foreach (var ai in results)
            {
                var name = ai.Grid?.DisplayName ?? "Unknown";
                var type = ai.Behavior?.GetType().Name ?? "None";
                Context.Respond($"• {name} ({type})");
            }
        }
        
        private IMyCubeGrid FindGrid(string gridName, IMyPlayer player)
        {
            // If no grid name provided, use player's controlled grid
            if (string.IsNullOrEmpty(gridName))
            {
                if (player?.Controller?.ControlledEntity?.Entity == null)
                    return null;
    
                return player.Controller.ControlledEntity.Entity.GetTopMostParent() as IMyCubeGrid;
            }

            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities, entity => entity is IMyCubeGrid);

            return allEntities
                .OfType<IMyCubeGrid>()
                .FirstOrDefault(g => string.Equals(g.DisplayName, gridName, StringComparison.OrdinalIgnoreCase));
        }

        [Command("ai clone", "Clone your current grid as an AI-controlled copy")]
        [Permission(MyPromoteLevel.SpaceMaster)]
        public void CloneAsAI(string behaviorType = "idle", double distance = 100)
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("This command must be run by a player.");
                return;
            }

            var sourceGrid = player.Controller.ControlledEntity?.Entity?.GetTopMostParent() as IMyCubeGrid;
            if (sourceGrid == null)
            {
                Context.Respond("You must be controlling a grid to clone it.");
                return;
            }

            try
            {
                var spawnPos = player.GetPosition() + player.Character.WorldMatrix.Forward * distance;
                
                // Create a copy of the grid (this would need blueprint functionality)
                var clonedGrid = CloneGrid(sourceGrid, spawnPos);
                
                if (clonedGrid != null)
                {
                    var behavior = CreateBehaviorForGrid(clonedGrid, behaviorType);
                    var aiManager = HeliosContext.Instance.AiManager;
                    aiManager.RegisterGrid(clonedGrid, behavior);
                    
                    Context.Respond($"Grid cloned and registered as AI with {behaviorType} behavior.");
                }
                else
                {
                    Context.Respond("Failed to clone grid.");
                }
            }
            catch (Exception ex)
            {
                Context.Respond($"Error cloning grid: {ex.Message}");
            }
        }

        [Command("ai convert", "Convert ownership of target grid to NPC faction and register with AI")]
        [Permission(MyPromoteLevel.Admin)]
        public void ConvertToNPC(string behaviorType = "idle", string factionTag = "SPRT")
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("This command must be run by a player.");
                return;
            }

            var target = player.Controller.ControlledEntity?.Entity?.GetTopMostParent() as IMyCubeGrid;
            if (target == null)
            {
                Context.Respond("No valid grid targeted.");
                return;
            }

            try
            {
                ConvertGridToNPC(target, factionTag);
                
                // Register with AI
                var behavior = CreateBehaviorForGrid(target, behaviorType);
                var aiManager = HeliosContext.Instance.AiManager;
                aiManager.RegisterGrid(target, behavior);
                
                Context.Respond($"Grid '{target.DisplayName}' converted to NPC faction '{factionTag}' and registered with AI.");
            }
            catch (Exception ex)
            {
                Context.Respond($"Error converting grid: {ex.Message}");
            }
        }

        [Command("ai analyze", "Analyze the capabilities of your current grid")]
        [Permission(MyPromoteLevel.None)]
        public void AnalyzeGrid()
        {
            var player = Context.Player;
            if (player == null)
            {
                Context.Respond("This command must be run by a player.");
                return;
            }

            var grid = player.Controller.ControlledEntity?.Entity?.GetTopMostParent() as IMyCubeGrid;
            if (grid == null)
            {
                Context.Respond("You must be controlling a grid to analyze it.");
                return;
            }

            var capabilities = AnalyzeGridCapabilities(grid);
            
            Context.Respond($"=== Grid Analysis: {grid.DisplayName} ===");
            Context.Respond($"Block Count: {capabilities.BlockCount}");
            Context.Respond($"Mass: {capabilities.Mass:F1} kg");
            Context.Respond($"Weapons: {(capabilities.HasWeapons ? "Yes" : "No")}");
            Context.Respond($"Thrusters: {(capabilities.HasThrusters ? "Yes" : "No")}");
            Context.Respond($"Jump Drive: {(capabilities.HasJumpDrive ? "Yes" : "No")}");
            Context.Respond($"Radar: {(capabilities.HasRadar ? "Yes" : "No")}");
            Context.Respond($"Recommended AI Behavior: {RecommendBehavior(capabilities)}");
        }

        // Helper methods
        private AiBehavior CreateBehaviorForGrid(IMyCubeGrid grid, string behaviorType)
        {
            
            if (behaviorType == "adaptive")
                behaviorType = AiManager.Instance.SelectAdaptiveBehavior(grid);

            switch (behaviorType)
            {
                case "idle":
                    return new IdleBehavior(grid);
                case "patrol":
                    return new PatrolBehavior(grid, new List<Vector3D>());
                case "attack":
                    var target = FindBestTarget(grid, true); // ignoreOwnership = true
                    if (target == null)
                        Context.Respond("No valid enemy grid found to attack. Try spawning a grid with a different owner/faction.");
                    return new AttackBehavior(grid, target);
                case "defense":
                    return new DefenseBehavior(grid, grid.GetPosition());
                default:
                    return null;
            }
        }

        private GridCapabilities AnalyzeGridCapabilities(IMyCubeGrid grid)
        {
            var capabilities = new GridCapabilities();
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);

            capabilities.BlockCount = blocks.Count;
            capabilities.Mass = grid.Physics != null ? (float)grid.Physics.Mass : 0f;

            foreach (var block in blocks)
            {
                var cubeBlock = block.FatBlock as IMyCubeBlock;
                if (cubeBlock == null) continue;

                // Vanilla weapons
                if (cubeBlock is IMyLargeTurretBase || cubeBlock is IMyUserControllableGun)
                    capabilities.HasWeapons = true;

                // WeaponCore or modded weapons (by block name or subtype, case-insensitive)
                var typeId = cubeBlock.BlockDefinition.TypeIdString.ToLower();
                var subtype = cubeBlock.BlockDefinition.SubtypeName.ToLower();
                var displayName = cubeBlock.DefinitionDisplayNameText.ToLower();

                if (typeId.Contains("weapon") ||
                    typeId.Contains("turret") ||
                    typeId.Contains("cannon") ||
                    typeId.Contains("gun") ||
                    subtype.Contains("weapon") ||
                    subtype.Contains("turret") ||
                    subtype.Contains("cannon") ||
                    subtype.Contains("gun") ||
                    displayName.Contains("weapon") ||
                    displayName.Contains("turret") ||
                    displayName.Contains("cannon") ||
                    displayName.Contains("gun"))
                    capabilities.HasWeapons = true;

                if (cubeBlock.GetType().Name.Contains("Weapon") || cubeBlock.GetType().Name.Contains("Turret"))
                    capabilities.HasWeapons = true;

                // Try to detect blocks with a "Shoot" action (common for modded weapons)
                var terminalBlock = cubeBlock as IMyTerminalBlock;
                if (terminalBlock != null)
                {
                    var actions = new List<Sandbox.ModAPI.Interfaces.ITerminalAction>();
                    terminalBlock.GetActions(actions, a => a.Id != null && a.Id.ToLower().Contains("shoot"));
                    if (actions.Count > 0)
                        capabilities.HasWeapons = true;
                }

                if (cubeBlock is IMyThrust)
                    capabilities.HasThrusters = true;
                if (cubeBlock is IMyJumpDrive)
                    capabilities.HasJumpDrive = true;
                if (cubeBlock is IMyRemoteControl)
                    capabilities.HasRemoteControl = true;
            }

            return capabilities;
        }

        private static string RecommendBehavior(GridCapabilities capabilities)
        {
            if (capabilities.HasWeapons && capabilities.HasThrusters)
                return "attack or patrol";
            if (capabilities.HasWeapons)
                return "defense";
            if (capabilities.HasThrusters)
                return "patrol";
            return "idle";
        }

        private static IMyCubeGrid CloneGrid(IMyCubeGrid sourceGrid, Vector3D position)
        {
            // This would require blueprint functionality to properly implement
            // For now, return null - you'd need to integrate with SE's blueprint system???
            // TODO: Implement grid cloning logic
            return null;
        }

        private void ConvertGridToNPC(IMyCubeGrid grid, string factionTag)
        {
            AiManager.Instance.SetGridOwnershipToNpcFaction(grid, factionTag);

            // (Optional) You can add additional logic here if you want too or if needed
        }

        private class GridCapabilities
        {
            public int BlockCount { get; set; }
            public float Mass { get; set; }
            public bool HasWeapons { get; set; }
            public bool HasThrusters { get; set; }
            public bool HasJumpDrive { get; set; }
            public bool HasRadar { get; set; }
            public bool HasRemoteControl { get; set; }
        }

        private static IMyCubeGrid FindNearestEnemyGrid(IMyCubeGrid sourceGrid, bool ignoreOwnership = false)
        {
            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities, e => e is IMyCubeGrid);

            var sourcePos = sourceGrid.GetPosition();
            var sourceOwners = sourceGrid.BigOwners;

            return allEntities
                .OfType<IMyCubeGrid>()
                .Where(g => g != sourceGrid && (ignoreOwnership || !g.BigOwners.Any(o => sourceOwners.Contains(o))))
                .OrderBy(g => Vector3D.DistanceSquared(g.GetPosition(), sourcePos))
                .FirstOrDefault();
        }

        private IMyCubeGrid FindBestTarget(IMyCubeGrid sourceGrid, bool ignoreOwnership = false)
        {
            var allEntities = new HashSet<IMyEntity>();
            MyAPIGateway.Entities.GetEntities(allEntities, e => e is IMyCubeGrid);

            var analyzer = AiManager.Instance._predictiveAnalyzer;
            var sourceOwners = sourceGrid.BigOwners;

            IMyCubeGrid bestGrid = null;
            var bestScore = float.MinValue;

            foreach (var grid in allEntities.OfType<IMyCubeGrid>())
            {
                Context.Respond($"Checking grid: {grid.DisplayName} Owners: {string.Join(",", grid.BigOwners)}");

                if (grid == sourceGrid)
                {
                    Context.Respond($"Skipping {grid.DisplayName}: is source grid.");
                    continue;
                }
                if (!ignoreOwnership && grid.BigOwners.Any(o => sourceOwners.Contains(o)))
                {
                    Context.Respond($"Skipping {grid.DisplayName}: ownership matches source.");
                    continue;
                }

                var score = analyzer.GetCombatPower(grid);
                Context.Respond($"Grid {grid.DisplayName} score: {score}");

                if (score > bestScore)
                {
                    bestScore = score;
                    bestGrid = grid;
                }
            }

            return bestGrid;
        }
    }
}