using System;
using System.Collections.Generic;
using System.Linq;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using VRageMath;
using HeliosAI.Behaviors;
using Helios.Core.Interfaces;

namespace Helios.Modules.AI.Ai.Control
{
    public class CustomGridManager : ICustomGridManager
    {
        private Dictionary<string, GridTemplate> customGrids = new Dictionary<string, GridTemplate>();
        private Dictionary<long, List<string>> playerTemplates = new Dictionary<long, List<string>>();

        public bool RegisterPlayerGrid(string templateName, IMyCubeGrid grid, long playerId, string behaviorType = "idle")
        {
            if (customGrids.ContainsKey(templateName))
                return false;

            try
            {
                var template = new GridTemplate
                {
                    Name = templateName,
                    Owner = playerId,
                    BehaviorType = behaviorType,
                    BlockCount = GetBlockCount(grid),
                    Mass = grid.Physics?.Mass ?? 0,
                    Capabilities = AnalyzeGridCapabilities(grid),
                    CreatedDate = DateTime.UtcNow,
                    GridData = SerializeGrid(grid) 
                };

                customGrids[templateName] = template;

                if (!playerTemplates.ContainsKey(playerId))
                    playerTemplates[playerId] = new List<string>();
                
                playerTemplates[playerId].Add(templateName);

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public IMyCubeGrid SpawnFromTemplate(string templateName, Vector3D position, long requesterId)
        {
            if (!customGrids.TryGetValue(templateName, out var template))
                return null;

            if (template.Owner != requesterId && !IsAdmin(requesterId))
                return null;

            try
            {
                return CreateGridFromTemplate(template, position);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public AiBehavior GetBehaviorForTemplate(string templateName, IMyCubeGrid grid)
        {
            if (!customGrids.TryGetValue(templateName, out var template))
                return new IdleBehavior(grid);

            return template.BehaviorType.ToLower() switch
            {
                "idle" => new IdleBehavior(grid),
                "patrol" => new PatrolBehavior(grid, new List<Vector3D>()),
                "attack" => new AttackBehavior(grid, null),
                "defense" => new DefenseBehavior(grid, grid.GetPosition()),
                _ => new IdleBehavior(grid)
            };
        }

        public List<GridTemplate> GetPlayerTemplates(long playerId)
        {
            if (!playerTemplates.TryGetValue(playerId, out var templateNames))
                return new List<GridTemplate>();

            return templateNames
                .Where(name => customGrids.ContainsKey(name))
                .Select(name => customGrids[name])
                .ToList();
        }

        public bool RemovePlayerTemplate(string templateName, long playerId)
        {
            if (!customGrids.TryGetValue(templateName, out var template))
                return false;

            if (template.Owner != playerId && !IsAdmin(playerId))
                return false;

            customGrids.Remove(templateName);
            
            if (playerTemplates.TryGetValue(playerId, out var templates))
            {
                templates.Remove(templateName);
            }

            return true;
        }

        private int GetBlockCount(IMyCubeGrid grid)
        {
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);
            return blocks.Count;
        }

        private GridCapabilities AnalyzeGridCapabilities(IMyCubeGrid grid)
        {
            var capabilities = new GridCapabilities();
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks);

            capabilities.BlockCount = blocks.Count;
            capabilities.Mass = grid.Physics?.Mass ?? 0;

            foreach (var block in blocks)
            {
                var cubeBlock = block.FatBlock;
                if (cubeBlock == null) continue;

                switch (cubeBlock)
                {
                    case IMyLargeTurretBase:
                    case IMyUserControllableGun:
                        capabilities.HasWeapons = true;
                        break;
                    case IMyThrust:
                        capabilities.HasThrusters = true;
                        break;
                    case IMyJumpDrive:
                        capabilities.HasJumpDrive = true;
                        break;
                    case IMyRadioAntenna:
                    case IMyLaserAntenna:
                        capabilities.HasRadar = true;
                        break;
                }
            }

            return capabilities;
        }

        private string SerializeGrid(IMyCubeGrid grid)
        {
            // In a real implementation, you'd serialize the grid structure
            // For now, just store the grid's display name and basic info
            return $"{grid.DisplayName}|{grid.EntityId}";
        }

        private IMyCubeGrid CreateGridFromTemplate(GridTemplate template, Vector3D position)
        {
            // This is a simplified implementation
            // In reality, you'd need to recreate the grid from stored data
            // or use Space Engineers' blueprint system
            
            // For now, return null - this would need proper grid recreation logic
            return null;
        }

        private bool IsAdmin(long playerId)
        {
            var allPlayers = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(allPlayers);
            var player = allPlayers.FirstOrDefault(p => p.IdentityId == playerId);

            return player?.PromoteLevel >= MyPromoteLevel.Admin ||
                   player?.PromoteLevel >= MyPromoteLevel.SpaceMaster;
        }
    }
}