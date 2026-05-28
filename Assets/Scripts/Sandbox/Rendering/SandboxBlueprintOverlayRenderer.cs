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
        private SandboxProjectWorkspaceService workspaceService;

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
                return;
            }

            for (var i = 0; i < workspaceService.ActiveProject.floors.Count; i += 1)
            {
                TryRenderFloorBlueprint(workspaceService.ActiveProject.floors[i]);
            }
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

        private static Texture2D ResolveBlueprintTexture(BlueprintReferenceData blueprintReference)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(blueprintReference.assetPath))
            {
                var editorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(blueprintReference.assetPath);
                if (editorTexture != null)
                {
                    return editorTexture;
                }
            }
#endif

            if (string.IsNullOrWhiteSpace(blueprintReference.importedPayloadBase64))
            {
                return null;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(blueprintReference.importedPayloadBase64);
            }
            catch
            {
                return null;
            }

            if (bytes.Length == 0)
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(bytes, false) ? texture : null;
        }
    }
}
