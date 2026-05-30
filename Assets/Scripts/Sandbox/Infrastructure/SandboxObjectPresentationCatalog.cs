using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace EvacLogix.Sandbox.Infrastructure
{
    [Serializable]
    public sealed class SandboxObjectPresentationDefinition
    {
        public SandboxVisualObjectType objectType;
        public string label = string.Empty;
        public string description = string.Empty;
        public string advancedFoldoutKey = string.Empty;
        public Color color = Color.white;
    }

    public static class SandboxObjectPresentationCatalog
    {
        private static readonly SandboxObjectPresentationDefinition[] definitions =
        {
            new()
            {
                objectType = SandboxVisualObjectType.Wall,
                label = "Walls",
                description = "Structural boundaries traced from the blueprint and used for collision and routing.",
                advancedFoldoutKey = "wall.advanced",
                color = new Color(0.85f, 0.32f, 0.18f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Door,
                label = "Doors",
                description = "Openings placed on walls with state metadata that affects preview passability.",
                advancedFoldoutKey = "door.advanced",
                color = new Color(0.18f, 0.55f, 1f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Window,
                label = "Windows",
                description = "Wall openings with optional escape-use metadata for preview interpretation.",
                advancedFoldoutKey = "window.advanced",
                color = new Color(0.72f, 0.3f, 1f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Exit,
                label = "Exits",
                description = "Named egress zones with width, priority, and capacity inputs.",
                advancedFoldoutKey = "exit.advanced",
                color = new Color(0.95f, 0.85f, 0.2f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Stair,
                label = "Stairs",
                description = "Linked floor-to-floor portals that preserve direction and travel cost.",
                advancedFoldoutKey = "stair.advanced",
                color = new Color(0.95f, 0.3f, 0.85f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Teleport,
                label = "Teleporters",
                description = "Paired stair, elevator, escalator, and other transitions that link rectangles across floors.",
                advancedFoldoutKey = "teleport.advanced",
                color = new Color(0.3f, 0.95f, 0.95f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Obstacle,
                label = "Obstacles",
                description = "Blocking or slowing objects that shape traversable areas during validation and preview.",
                advancedFoldoutKey = "obstacle.advanced",
                color = new Color(0.85f, 0.25f, 0.2f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.Spawn,
                label = "Spawns",
                description = "Intentional spawn points used to place runtime agents during preview setup.",
                advancedFoldoutKey = "spawn.advanced",
                color = new Color(0.2f, 0.9f, 0.5f, 1f)
            },
            new()
            {
                objectType = SandboxVisualObjectType.FireStart,
                label = "Fire Starts",
                description = "Fire origin points that seed the spreading fire during simulation.",
                advancedFoldoutKey = "firestart.advanced",
                color = new Color(0.9f, 0.2f, 0.1f, 1f)
            }
        };

        public static IReadOnlyList<SandboxObjectPresentationDefinition> Definitions => definitions;

        public static SandboxObjectPresentationDefinition GetDefinition(SandboxVisualObjectType objectType)
        {
            return definitions.First(definition => definition.objectType == objectType);
        }

        public static IEnumerable<string> GetRequiredAdvancedFoldoutKeys()
        {
            yield return "project.advanced";
            yield return "floor.advanced";
            yield return "preview.advanced";
            yield return "scenario.advanced";

            foreach (var definition in definitions)
            {
                if (!string.IsNullOrWhiteSpace(definition.advancedFoldoutKey))
                {
                    yield return definition.advancedFoldoutKey;
                }
            }
        }

        public static bool HasCompleteLegendCoverage(IEnumerable<SandboxVisualLegendEntry> legendEntries)
        {
            if (legendEntries == null)
            {
                return false;
            }

            var representedTypes = legendEntries.Select(entry => entry.objectType).Distinct().ToList();
            return definitions.All(definition => representedTypes.Contains(definition.objectType));
        }
    }
}
