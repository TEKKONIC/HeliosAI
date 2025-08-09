using System;
using System.Collections.Generic;
using System.Linq;
using Helios.Core;
using Helios.Modules.AI;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;
using HeliosAI.Behaviors;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRageMath;

[Category("helios")]
public class AiDebugCommands : CommandModule
{
    [Command("ai register", "Register the targeted grid with Helios AI system")]
    [Permission(MyPromoteLevel.Admin)]
    public void RegisterTargetedGrid()
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

        if (aiManager.IsRegistered(target))
        {
            Context.Respond($"Grid '{target.DisplayName}' is already registered.");
            return;
        }

        aiManager.RegisterGrid(target, new IdleBehavior(target));
        Context.Respond($"Grid '{target.DisplayName}' registered with Idle behavior.");
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
        var aiManager = AiManager.Instance; // Use the static Instance property instead
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

        // Search for grid by name using GetEntities with a filter
        var allEntities = new HashSet<IMyEntity>();
        MyAPIGateway.Entities.GetEntities(allEntities, entity => entity is IMyCubeGrid);

        return allEntities
            .OfType<IMyCubeGrid>()
            .FirstOrDefault(g => string.Equals(g.DisplayName, gridName, StringComparison.OrdinalIgnoreCase));
    }
}