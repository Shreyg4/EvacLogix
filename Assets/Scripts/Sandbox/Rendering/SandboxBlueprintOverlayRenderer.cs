using System.Collections.Generic;
using EvacLogix.Sandbox.Data;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;
using System;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EvacLogix.Sandbox.Rendering
{
    public sealed class SandboxBlueprintOverlayRenderer : MonoBehaviour
    {
        [SerializeField] private bool renderOnlyActiveFloor = true;
        [SerializeField] private string overlayPrefix = "BlueprintOverlay_";

        private readonly List<GameObject> activeOverlayObjects = new();
        private readonly List<Sprite> activeOverlaySprites = new();
        // Decoded blueprint textures cached by reference id. Without this, the image was re-decoded into
        // a brand-new Texture2D on every Refresh (i.e. every edit, since each edit re-assigns the
        // project), and the old textures were never destroyed — a leak that eventually starved WebGL
        // memory until a decode failed and the background silently disappeared.
        private readonly Dictionary<string, CachedBlueprintTexture> textureCache = new();
        private SandboxProjectWorkspaceService workspaceService;

        private sealed class CachedBlueprintTexture
        {
            public Texture2D Texture;
            public int PayloadSignature;
            public bool Owned;
        }

        private void Awake()
        {
            workspaceService = FindAnyObjectByType<SandboxProjectWorkspaceService>();
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged += HandleProjectChanged;
                workspaceService.ActiveFloorChanged += HandleFloorChanged;
                Refresh();
            }
        }

        private void OnDestroy()
        {
            if (workspaceService != null)
            {
                workspaceService.ActiveProjectChanged -= HandleProjectChanged;
                workspaceService.ActiveFloorChanged -= HandleFloorChanged;
            }

            PruneTextureCache(null);
        }

        // Destroys all cached decoded textures (used by the memory guard to reclaim memory under pressure).
        // They are lazily re-decoded from the live project's payloads on the next Refresh.
        public void FlushTextureCache()
        {
            PruneTextureCache(null);
        }

        public void Refresh()
        {
            ClearOverlays();

            if (workspaceService?.ActiveProject == null)
            {
                return;
            }

            if (renderOnlyActiveFloor)
            {
                TryRenderFloorBlueprint(workspaceService.ActiveFloor);
            }
            else
            {
                for (var i = 0; i < workspaceService.ActiveProject.floors.Count; i += 1)
                {
                    TryRenderFloorBlueprint(workspaceService.ActiveProject.floors[i]);
                }
            }

            // Drop cached textures for blueprints no longer in the project so the cache can't grow
            // unbounded across project loads.
            PruneTextureCache(workspaceService.ActiveProject);
        }

        private void HandleProjectChanged(BuildingProjectData project)
        {
            Refresh();
        }

        private void HandleFloorChanged(FloorData floor)
        {
            Refresh();
        }

        private void TryRenderFloorBlueprint(FloorData floor)
        {
            if (floor == null || string.IsNullOrWhiteSpace(floor.blueprintReferenceId))
            {
                return;
            }

            var blueprintReference = workspaceService.FindBlueprintReference(floor.blueprintReferenceId);
            if (blueprintReference == null || !blueprintReference.isVisible)
            {
                return;
            }

            var texture = ResolveBlueprintTexture(blueprintReference);
            if (texture == null)
            {
                return;
            }

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                1f);

            var overlayObject = new GameObject($"{overlayPrefix}{floor.name}");
            overlayObject.transform.SetParent(transform, false);
            var resolvedDisplayScale = blueprintReference.displayScale <= 0f ? 1f : blueprintReference.displayScale;
            overlayObject.transform.localScale = new Vector3(
                blueprintReference.worldUnitsPerPixel * resolvedDisplayScale,
                blueprintReference.worldUnitsPerPixel * resolvedDisplayScale,
                1f);

            var renderer = overlayObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(blueprintReference.opacity));
            renderer.sortingOrder = -10 - floor.order;
            activeOverlayObjects.Add(overlayObject);
            activeOverlaySprites.Add(sprite);
        }

        private void ClearOverlays()
        {
            for (var i = 0; i < activeOverlayObjects.Count; i += 1)
            {
                if (activeOverlayObjects[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(activeOverlayObjects[i]);
                    }
                    else
                    {
                        DestroyImmediate(activeOverlayObjects[i]);
                    }
                }
            }

            activeOverlayObjects.Clear();

            for (var i = 0; i < activeOverlaySprites.Count; i += 1)
            {
                if (activeOverlaySprites[i] != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(activeOverlaySprites[i]);
                    }
                    else
                    {
                        DestroyImmediate(activeOverlaySprites[i]);
                    }
                }
            }

            activeOverlaySprites.Clear();
        }

        private Texture2D ResolveBlueprintTexture(BlueprintReferenceData blueprintReference)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(blueprintReference.assetPath))
            {
                var editorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(blueprintReference.assetPath);
                if (editorTexture != null)
                {
                    // Editor-owned asset; never cache/destroy it ourselves.
                    return editorTexture;
                }
            }
#endif

            if (string.IsNullOrWhiteSpace(blueprintReference.importedPayloadBase64))
            {
                return null;
            }

            var signature = blueprintReference.importedPayloadBase64.Length;
            var cacheKey = blueprintReference.blueprintReferenceId;
            if (textureCache.TryGetValue(cacheKey, out var cached) &&
                cached.Owned &&
                cached.Texture != null &&
                cached.PayloadSignature == signature)
            {
                return cached.Texture;
            }

            // Payload changed or wasn't cached: drop the stale texture we own before decoding again.
            if (cached != null && cached.Owned && cached.Texture != null)
            {
                DestroyTexture(cached.Texture);
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blueprintReference.importedPayloadBase64);
            }
            catch
            {
                textureCache.Remove(cacheKey);
                return null;
            }

            if (bytes.Length == 0)
            {
                textureCache.Remove(cacheKey);
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes, false))
            {
                DestroyTexture(texture);
                textureCache.Remove(cacheKey);
                return null;
            }

            textureCache[cacheKey] = new CachedBlueprintTexture
            {
                Texture = texture,
                PayloadSignature = signature,
                Owned = true
            };
            return texture;
        }

        // Destroys cached textures we own (decoded from base64) whose blueprint is no longer present in
        // the project. Pass null to flush the whole cache (teardown).
        private void PruneTextureCache(BuildingProjectData project)
        {
            if (textureCache.Count == 0)
            {
                return;
            }

            var validIds = new HashSet<string>();
            if (project?.blueprintReferences != null)
            {
                for (var i = 0; i < project.blueprintReferences.Count; i += 1)
                {
                    validIds.Add(project.blueprintReferences[i].blueprintReferenceId);
                }
            }

            var staleKeys = new List<string>();
            foreach (var pair in textureCache)
            {
                if (!validIds.Contains(pair.Key))
                {
                    staleKeys.Add(pair.Key);
                }
            }

            for (var i = 0; i < staleKeys.Count; i += 1)
            {
                if (textureCache.TryGetValue(staleKeys[i], out var entry) && entry.Owned && entry.Texture != null)
                {
                    DestroyTexture(entry.Texture);
                }

                textureCache.Remove(staleKeys[i]);
            }
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(texture);
            }
            else
            {
                DestroyImmediate(texture);
            }
        }
    }
}
