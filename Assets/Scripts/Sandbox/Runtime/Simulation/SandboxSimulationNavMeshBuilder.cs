using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;
using UnityEngine.AI;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // Builds a single navmesh for the whole laid-out building. Per floor it lays a walkable slab over
    // the floor's footprint, then carves the wall spans out as Not-Walkable boxes. The wall spans
    // come straight from SandboxColliderRebuildService, which already removes door/escape-window
    // gaps, so routing flows through openings. Each floor is offset into its grid cell (no elevation
    // stacking), and the 2D world plane (x,y) maps to the nav plane (x, 0, y) to match SandboxEvacueeAgent.
    public sealed class SandboxSimulationNavMeshBuilder
    {
        private const int WalkableArea = 0;
        private const int NotWalkableArea = 1;
        private const int SoftObstacleArea = 3;
        private const float SoftObstacleAreaCost = 6f;
        private const float ImpassableThreshold = 0.99f;
        private const float WallHeight = 3f;
        private const float SlabHeight = 0.05f;
        private const float BoundsPadding = 2f;

        private readonly List<NavMeshDataInstance> instances = new();
        private NavMeshData navMeshData;

        public bool HasNavMesh => navMeshData != null;

        public bool Rebuild(
            IReadOnlyList<SandboxFloorPlacement> placements,
            BuildingProjectData project,
            IReadOnlyList<SandboxGeneratedColliderData> generatedColliders,
            float agentRadius)
        {
            Clear();
            if (placements == null || placements.Count == 0)
            {
                return false;
            }

            var floorsById = new Dictionary<string, FloorData>(StringComparer.Ordinal);
            if (project?.floors != null)
            {
                foreach (var floor in project.floors)
                {
                    floorsById[floor.floorId] = floor;
                }
            }

            // Soft obstacles cost extra to cross so agents prefer to route around them.
            NavMesh.SetAreaCost(SoftObstacleArea, SoftObstacleAreaCost);

            // Index colliders by floor for quick per-floor lookup.
            var collidersByFloor = new Dictionary<string, List<SandboxGeneratedColliderData>>();
            if (generatedColliders != null)
            {
                for (var i = 0; i < generatedColliders.Count; i += 1)
                {
                    var collider = generatedColliders[i];
                    if (!collidersByFloor.TryGetValue(collider.floorId, out var list))
                    {
                        list = new List<SandboxGeneratedColliderData>();
                        collidersByFloor[collider.floorId] = list;
                    }

                    list.Add(collider);
                }
            }

            var sources = new List<NavMeshBuildSource>();
            var bounds = new Bounds();
            var hasBounds = false;

            for (var i = 0; i < placements.Count; i += 1)
            {
                var placement = placements[i];
                var worldBounds = placement.WorldBounds;

                // Walkable slab covering the floor footprint.
                var slabCenter = new Vector3(worldBounds.center.x, 0f, worldBounds.center.y);
                var slabSize = new Vector3(worldBounds.width, SlabHeight, worldBounds.height);
                sources.Add(new NavMeshBuildSource
                {
                    shape = NavMeshBuildSourceShape.Box,
                    sourceObject = null,
                    transform = Matrix4x4.TRS(slabCenter, Quaternion.identity, Vector3.one),
                    size = slabSize,
                    area = WalkableArea
                });

                EncapsulateBox(ref bounds, ref hasBounds, slabCenter, slabSize);

                // Carve the wall spans (door/window gaps already removed by the collider builder).
                if (collidersByFloor.TryGetValue(placement.FloorId, out var floorColliders))
                {
                    for (var c = 0; c < floorColliders.Count; c += 1)
                    {
                        var collider = floorColliders[c];
                        var worldCenter = placement.ToWorld(collider.center);
                        var navCenter = new Vector3(worldCenter.x, WallHeight * 0.5f, worldCenter.y);
                        // A Z-rotation in the 2D (x,y) plane maps to a -Y rotation in the nav (x,z) plane.
                        var rotation = Quaternion.Euler(0f, -collider.rotationDegrees, 0f);
                        var size = new Vector3(Mathf.Max(0.01f, collider.size.x), WallHeight, Mathf.Max(0.01f, collider.size.y));
                        sources.Add(new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Box,
                            sourceObject = null,
                            transform = Matrix4x4.TRS(navCenter, rotation, Vector3.one),
                            size = size,
                            area = NotWalkableArea
                        });

                        EncapsulateBox(ref bounds, ref hasBounds, navCenter, size);
                    }
                }

                // Obstacles: impassable ones carve out (not-walkable); softer ones become a costly
                // area agents prefer to avoid (and are slowed in by the agent service).
                if (floorsById.TryGetValue(placement.FloorId, out var floorData))
                {
                    foreach (var obstacle in floorData.obstacles)
                    {
                        if (obstacle.discourageWeight <= 0f)
                        {
                            continue;
                        }

                        var area = obstacle.discourageWeight >= ImpassableThreshold ? NotWalkableArea : SoftObstacleArea;
                        var worldCenter = placement.ToWorld(obstacle.center);
                        var navCenter = new Vector3(worldCenter.x, WallHeight * 0.5f, worldCenter.y);
                        var rotation = Quaternion.Euler(0f, -obstacle.rotationDegrees, 0f);
                        var size = new Vector3(Mathf.Max(0.01f, obstacle.size.x), WallHeight, Mathf.Max(0.01f, obstacle.size.y));
                        sources.Add(new NavMeshBuildSource
                        {
                            shape = NavMeshBuildSourceShape.Box,
                            sourceObject = null,
                            transform = Matrix4x4.TRS(navCenter, rotation, Vector3.one),
                            size = size,
                            area = area
                        });

                        EncapsulateBox(ref bounds, ref hasBounds, navCenter, size);
                    }
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            bounds.Expand(BoundsPadding);
            var settings = NavMesh.GetSettingsByIndex(0);
            if (agentRadius > 0f)
            {
                settings.agentRadius = agentRadius;
            }

            navMeshData = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navMeshData == null)
            {
                return false;
            }

            instances.Add(NavMesh.AddNavMeshData(navMeshData));
            return true;
        }

        public void Clear()
        {
            for (var i = 0; i < instances.Count; i += 1)
            {
                instances[i].Remove();
            }

            instances.Clear();

            if (navMeshData != null)
            {
                if (Application.isPlaying)
                {
                    UnityEngine.Object.Destroy(navMeshData);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(navMeshData);
                }

                navMeshData = null;
            }
        }

        private static void EncapsulateBox(ref Bounds bounds, ref bool hasBounds, Vector3 center, Vector3 size)
        {
            var box = new Bounds(center, size);
            if (!hasBounds)
            {
                bounds = box;
                hasBounds = true;
                return;
            }

            bounds.Encapsulate(box.min);
            bounds.Encapsulate(box.max);
        }
    }
}
