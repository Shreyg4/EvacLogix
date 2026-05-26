using System;
using System.IO;
using EvacLogix.Sandbox.Data;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace EvacLogix.Sandbox.Infrastructure
{
    public sealed class SandboxBlueprintImportService : MonoBehaviour
    {
        private const string ManagedBlueprintDirectory = "Assets/Art/Blueprints/Sandbox";

        public bool CanImport(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            {
                return false;
            }

            var extension = Path.GetExtension(sourceFilePath);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase)
                || string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase);
        }

        public BlueprintReferenceData ImportBlueprint(string sourceFilePath)
        {
            if (!CanImport(sourceFilePath))
            {
                throw new InvalidOperationException($"Unsupported blueprint import path '{sourceFilePath}'.");
            }

            var managedBlueprintDiskDirectory = Path.Combine(Application.dataPath, "Art/Blueprints/Sandbox");
            Directory.CreateDirectory(managedBlueprintDiskDirectory);

            var fileName = Path.GetFileName(sourceFilePath);
            var managedAssetPath = Path.Combine(ManagedBlueprintDirectory, fileName).Replace("\\", "/");
            var managedDiskPath = Path.Combine(managedBlueprintDiskDirectory, fileName);

            if (File.Exists(managedDiskPath))
            {
                var timestampPrefix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                managedAssetPath = Path.Combine(ManagedBlueprintDirectory, $"{timestampPrefix}_{fileName}").Replace("\\", "/");
                managedDiskPath = Path.Combine(managedBlueprintDiskDirectory, $"{timestampPrefix}_{fileName}");
            }

            File.Copy(sourceFilePath, managedDiskPath, false);

            var assetGuid = string.Empty;
#if UNITY_EDITOR
            AssetDatabase.ImportAsset(managedAssetPath, ImportAssetOptions.ForceUpdate);
            assetGuid = AssetDatabase.AssetPathToGUID(managedAssetPath);
#endif

            return new BlueprintReferenceData
            {
                blueprintReferenceId = SandboxId.NewId(),
                assetGuid = assetGuid,
                assetPath = managedAssetPath,
                sourceFileName = fileName,
                opacity = 1f,
                isVisible = true,
            };
        }
    }
}
