using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace EvacLogix.Editor
{
    public static class EvacLogixWebGlBuilder
    {
        [UnityEditor.MenuItem("EvacLogix/Build WebGL")]
        public static void Build()
        {
            var outputPath = Path.GetFullPath("EvacLogixBuild");
            var scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes are configured for the WebGL build.");
            }

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            });

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException($"WebGL build failed with result {report.summary.result}.");
            }
        }
    }
}
