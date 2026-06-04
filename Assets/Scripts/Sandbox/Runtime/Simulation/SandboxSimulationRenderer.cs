using System;
using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using EvacLogix.Sandbox.Rendering;
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
        private GameObject fireRoot;
        // Fire is drawn as ONE textured quad per floor (a heatmap) rather than one sprite per burning cell,
        // so a whole-floor fire costs the same to render as a small one — O(floors), not O(burning cells).
        // The texture only rewrites when the hazard field actually steps (~3x/sec); the cached revision +
        // count skips frames where nothing changed.
        private readonly Dictionary<string, FloorHeatmap> heatmapsByFloor = new(StringComparer.Ordinal);
        private int lastRenderedHazardRevision = -1;
        private int lastRenderedFireCount = -1;
        private Sprite squareSprite;

        // One per floor that has ever caught fire: a grid texture whose texels map 1:1 to fire cells.
        private sealed class FloorHeatmap
        {
            public GameObject Quad;
            public Texture2D Texture;
            public Color32[] Pixels;
            public int MinIx;
            public int MinIy;
            public int Width;
            public int Height;
        }

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
                        DrawQuad("Wall", placement.ToWorld(collider.center), collider.size, collider.rotationDegrees, WallColor, WallOrder, true);
                    }
                }

                foreach (var exit in floor.exits)
                {
                    DrawQuad("Exit", placement.ToWorld(exit.center), exit.size, exit.rotationDegrees, ExitColor, FillOrder);
                }

                foreach (var obstacle in floor.obstacles)
                {
                    DrawQuad("Obstacle", placement.ToWorld(obstacle.center), obstacle.size, obstacle.rotationDegrees, ObstacleColor, FillOrder, obstacle.discourageWeight >= 0.99f);
                }

                foreach (var stairPortal in floor.stairPortals)
                {
                    DrawQuad("Stair", placement.ToWorld(stairPortal.localPosition), stairPortal.size, stairPortal.rotationDegrees, PairColors[0], FillOrder);
                }

                foreach (var teleportPortal in floor.teleportPortals)
                {
                    var teleportColor = SandboxTeleportPairColor.Resolve(teleportPortal.teleportPortalId, teleportPortal.targetTeleportPortalId);
                    teleportColor.a = 0.85f;
                    DrawQuad("Teleport", placement.ToWorld(teleportPortal.localPosition), teleportPortal.size, teleportPortal.rotationDegrees, teleportColor, FillOrder);
                }

                DrawOpenings(floor, placement);
            }
        }

        // Repaints the per-floor fire heatmap textures when the hazard field has changed. Each burning
        // cell writes one texel; rendering is then one quad per floor regardless of how big the fire is.
        public void UpdateFire(IReadOnlyList<SandboxFireCellData> cells, SandboxFloorLayoutService layoutService)
        {
            if (layoutService == null)
            {
                return;
            }

            var fireService = GetComponent<SandboxFireSimulationService>();
            var revision = fireService != null ? fireService.HazardRevision : 0;
            var rawCount = cells?.Count ?? 0;
            // Nothing changed since the last draw — skip the rewrite.
            if (revision == lastRenderedHazardRevision && rawCount == lastRenderedFireCount)
            {
                return;
            }

            lastRenderedHazardRevision = revision;
            lastRenderedFireCount = rawCount;
            if (fireService == null)
            {
                return;
            }

            EnsureFireRoot();
            var cellSize = fireService.CellSize;
            var threshold = fireService.VisibleFlameThreshold;

            // Wipe every floor's texture, then repaint the currently-burning cells.
            foreach (var pair in heatmapsByFloor)
            {
                Array.Clear(pair.Value.Pixels, 0, pair.Value.Pixels.Length);
            }

            if (cells != null)
            {
                for (var i = 0; i < cells.Count; i += 1)
                {
                    var cell = cells[i];
                    if (cell == null)
                    {
                        continue;
                    }

                    var intensity = Mathf.Clamp01(cell.intensity);
                    if (intensity < threshold)
                    {
                        continue;
                    }

                    var heatmap = GetOrCreateHeatmap(cell.floorId, layoutService, cellSize);
                    if (heatmap == null)
                    {
                        continue;
                    }

                    var px = Mathf.RoundToInt(cell.position.x / cellSize) - heatmap.MinIx;
                    var py = Mathf.RoundToInt(cell.position.y / cellSize) - heatmap.MinIy;
                    if (px < 0 || py < 0 || px >= heatmap.Width || py >= heatmap.Height)
                    {
                        continue;
                    }

                    heatmap.Pixels[(py * heatmap.Width) + px] = Color.Lerp(FireLowColor, FireHighColor, intensity);
                }
            }

            foreach (var pair in heatmapsByFloor)
            {
                pair.Value.Texture.SetPixels32(pair.Value.Pixels);
                pair.Value.Texture.Apply(false);
            }
        }

        // Lazily builds a floor's heatmap (texture + quad) the first time it catches fire. The quad is
        // sized and positioned so each texel sits exactly on its fire cell (sprite is 1px-per-unit, scaled
        // by cellSize; the grid is centred on the floor's local bounds).
        private FloorHeatmap GetOrCreateHeatmap(string floorId, SandboxFloorLayoutService layoutService, float cellSize)
        {
            if (string.IsNullOrEmpty(floorId))
            {
                return null;
            }

            if (heatmapsByFloor.TryGetValue(floorId, out var existing))
            {
                return existing;
            }

            if (!layoutService.TryGetPlacement(floorId, out var placement))
            {
                return null;
            }

            var bounds = placement.LocalBounds;
            var minIx = Mathf.FloorToInt(bounds.xMin / cellSize) - 1;
            var minIy = Mathf.FloorToInt(bounds.yMin / cellSize) - 1;
            var maxIx = Mathf.CeilToInt(bounds.xMax / cellSize) + 1;
            var maxIy = Mathf.CeilToInt(bounds.yMax / cellSize) + 1;
            var width = Mathf.Max(1, (maxIx - minIx) + 1);
            var height = Mathf.Max(1, (maxIy - minIy) + 1);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var quad = new GameObject("FireHeatmap_" + floorId);
            quad.transform.SetParent(fireRoot.transform, false);
            var spriteRenderer = quad.AddComponent<SpriteRenderer>();
            spriteRenderer.sprite = Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f), 1f);
            spriteRenderer.sortingOrder = FireOrder;

            var midLocal = new Vector2((minIx + maxIx) * 0.5f * cellSize, (minIy + maxIy) * 0.5f * cellSize);
            var world = placement.ToWorld(midLocal);
            quad.transform.position = new Vector3(world.x, world.y, 0f);
            quad.transform.localScale = new Vector3(cellSize, cellSize, 1f);

            var heatmap = new FloorHeatmap
            {
                Quad = quad,
                Texture = texture,
                Pixels = new Color32[width * height],
                MinIx = minIx,
                MinIy = minIy,
                Width = width,
                Height = height
            };
            heatmapsByFloor[floorId] = heatmap;
            return heatmap;
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

            foreach (var pair in heatmapsByFloor)
            {
                DestroyUnityObject(pair.Value.Quad);
                DestroyUnityObject(pair.Value.Texture);
            }

            heatmapsByFloor.Clear();
            lastRenderedHazardRevision = -1;
            lastRenderedFireCount = -1;
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
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

        private void DrawQuad(string label, Vector2 worldCenter, Vector2 size, float rotationDegrees, Color color, int sortingOrder, bool addCollider = false)
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
            if (addCollider)
            {
                var collider = quad.AddComponent<BoxCollider2D>();
                collider.size = Vector2.one;
            }

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
