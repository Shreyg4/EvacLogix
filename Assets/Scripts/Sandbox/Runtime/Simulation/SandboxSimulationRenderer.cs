using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;

namespace EvacLogix.Sandbox.Runtime.Simulation
{
    // Dedicated world-space rendering for the simulation: draws every floor's geometry as flat
    // sprites offset into its grid cell, so all floors are visible side-by-side at once. Built once
    // at sim start from the project + layout + generated wall colliders; agents draw themselves.
    // Floor name labels are drawn by the controller's HUD (screen-space), not here.
    public sealed class SandboxSimulationRenderer : MonoBehaviour
    {
        private static readonly Color SlabColor = new(0.13f, 0.16f, 0.22f, 1f);
        private static readonly Color WallColor = new(0.82f, 0.86f, 0.92f, 1f);
        private static readonly Color ExitColor = new(0.27f, 0.78f, 0.4f, 0.75f);
        private static readonly Color ObstacleColor = new(0.5f, 0.42f, 0.32f, 0.7f);
        private static readonly Color DoorColor = new(0.95f, 0.78f, 0.35f, 1f);
        private static readonly Color WindowColor = new(0.45f, 0.75f, 0.95f, 1f);

        private static readonly Color[] PairColors =
        {
            new(0.4f, 0.8f, 1f, 0.85f),
            new(1f, 0.6f, 0.35f, 0.85f),
            new(0.75f, 0.55f, 1f, 0.85f),
            new(1f, 0.45f, 0.7f, 0.85f),
            new(0.55f, 0.9f, 0.6f, 0.85f),
        };

        private const int SlabOrder = 0;
        private const int FillOrder = 5;
        private const int WallOrder = 10;
        private const int OpeningOrder = 15;

        private const int FireOrder = 20;
        private static readonly Color FireLowColor = new(1f, 0.62f, 0.12f, 0.6f);
        private static readonly Color FireHighColor = new(1f, 0.22f, 0.05f, 0.95f);

        private readonly List<GameObject> spawned = new();
        private readonly List<GameObject> fireSprites = new();
        private GameObject fireRoot;
        private Sprite squareSprite;

        public void Build(BuildingProjectData project, SandboxFloorLayoutService layoutService, IReadOnlyList<SandboxGeneratedColliderData> generatedColliders)
        {
            Clear();
            if (project?.floors == null || layoutService == null)
            {
                return;
            }

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

            for (var f = 0; f < project.floors.Count; f += 1)
            {
                var floor = project.floors[f];
                if (!layoutService.TryGetPlacement(floor.floorId, out var placement))
                {
                    continue;
                }

                var worldBounds = placement.WorldBounds;
                DrawQuad("Slab", worldBounds.center, worldBounds.size, 0f, SlabColor, SlabOrder);

                if (collidersByFloor.TryGetValue(floor.floorId, out var floorColliders))
                {
                    for (var c = 0; c < floorColliders.Count; c += 1)
                    {
                        var collider = floorColliders[c];
                        DrawQuad("Wall", placement.ToWorld(collider.center), collider.size, collider.rotationDegrees, WallColor, WallOrder);
                    }
                }

                foreach (var exit in floor.exits)
                {
                    DrawQuad("Exit", placement.ToWorld(exit.center), exit.size, exit.rotationDegrees, ExitColor, FillOrder);
                }

                foreach (var obstacle in floor.obstacles)
                {
                    DrawQuad("Obstacle", placement.ToWorld(obstacle.center), obstacle.size, obstacle.rotationDegrees, ObstacleColor, FillOrder);
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    DrawQuad("Stair", placement.ToWorld(stairPortal.localPosition), stairPortal.size, stairPortal.rotationDegrees, PairColors[0], FillOrder);
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    DrawQuad("Teleport", placement.ToWorld(teleportPortal.localPosition), teleportPortal.size, teleportPortal.rotationDegrees, ResolvePairColor(teleportPortal.pairColorIndex), FillOrder);
                }

                DrawOpenings(floor, placement);
            }
        }

        // Per-frame update of fire cell sprites (the fire spreads dynamically). Pools sprites so it
        // doesn't churn GameObjects every frame; unused ones are deactivated.
        public void UpdateFire(IReadOnlyList<SandboxFireCellData> cells, SandboxFloorLayoutService layoutService)
        {
            if (layoutService == null)
            {
                return;
            }

            EnsureFireRoot();
            var count = cells?.Count ?? 0;
            while (fireSprites.Count < count)
            {
                var fireObject = new GameObject("Fire");
                fireObject.transform.SetParent(fireRoot.transform, false);
                var spriteRenderer = fireObject.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = GetSquareSprite();
                spriteRenderer.sortingOrder = FireOrder;
                fireSprites.Add(fireObject);
            }

            for (var i = 0; i < fireSprites.Count; i += 1)
            {
                if (i >= count || !layoutService.TryGetPlacement(cells[i].floorId, out var placement))
                {
                    fireSprites[i].SetActive(false);
                    continue;
                }

                var cell = cells[i];
                var world = placement.ToWorld(cell.position);
                var fireObject = fireSprites[i];
                fireObject.SetActive(true);
                fireObject.transform.position = new Vector3(world.x, world.y, 0f);
                fireObject.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
                fireObject.GetComponent<SpriteRenderer>().color = Color.Lerp(FireLowColor, FireHighColor, Mathf.Clamp01(cell.intensity));
            }
        }

        public void Clear()
        {
            for (var i = 0; i < spawned.Count; i += 1)
            {
                if (spawned[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(spawned[i]);
                }
                else
                {
                    DestroyImmediate(spawned[i]);
                }
            }

            spawned.Clear();

            for (var i = 0; i < fireSprites.Count; i += 1)
            {
                if (fireSprites[i] == null)
                {
                    continue;
                }

                if (Application.isPlaying)
                {
                    Destroy(fireSprites[i]);
                }
                else
                {
                    DestroyImmediate(fireSprites[i]);
                }
            }

            fireSprites.Clear();
        }

        private void EnsureFireRoot()
        {
            if (fireRoot == null)
            {
                fireRoot = new GameObject("FireRoot");
                fireRoot.transform.SetParent(transform, false);
            }
        }

        private void DrawOpenings(FloorData floor, SandboxFloorPlacement placement)
        {
            foreach (var door in floor.doors)
            {
                if (TryResolveOpeningCenter(floor, door.wallSegmentId, door.offsetAlongWall, out var localCenter))
                {
                    DrawQuad("Door", placement.ToWorld(localCenter), new Vector2(0.4f, 0.4f), 0f, DoorColor, OpeningOrder);
                }
            }

            foreach (var window in floor.windows)
            {
                if (TryResolveOpeningCenter(floor, window.wallSegmentId, window.offsetAlongWall, out var localCenter))
                {
                    DrawQuad("Window", placement.ToWorld(localCenter), new Vector2(0.35f, 0.35f), 0f, WindowColor, OpeningOrder);
                }
            }
        }

        private void DrawQuad(string label, Vector2 worldCenter, Vector2 size, float rotationDegrees, Color color, int sortingOrder)
        {
            var quad = new GameObject(label);
            quad.transform.SetParent(transform, false);
            quad.transform.position = new Vector3(worldCenter.x, worldCenter.y, 0f);
            quad.transform.rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
            quad.transform.localScale = new Vector3(Mathf.Max(0.01f, size.x), Mathf.Max(0.01f, size.y), 1f);

            var spriteRenderer = quad.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = GetSquareSprite();
            spriteRenderer.color = color;
            spriteRenderer.sortingOrder = sortingOrder;
            spawned.Add(quad);
        }

        private static bool TryResolveOpeningCenter(FloorData floor, string wallSegmentId, float offsetAlongWall, out Vector2 center)
        {
            center = Vector2.zero;
            for (var i = 0; i < floor.wallSegments.Count; i += 1)
            {
                var wall = floor.wallSegments[i];
                if (!string.Equals(wall.wallSegmentId, wallSegmentId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var direction = wall.endPoint - wall.startPoint;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    return false;
                }

                center = wall.startPoint + direction.normalized * offsetAlongWall;
                return true;
            }

            return false;
        }

        private static Color ResolvePairColor(int pairColorIndex)
        {
            if (pairColorIndex < 0)
            {
                pairColorIndex = 0;
            }

            return PairColors[pairColorIndex % PairColors.Length];
        }

        private Sprite GetSquareSprite()
        {
            if (squareSprite == null)
            {
                var texture = Texture2D.whiteTexture;
                squareSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
            }

            return squareSprite;
        }
    }
}
