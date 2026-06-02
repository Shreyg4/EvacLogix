using System;
using System.IO;
using EvacLogix.Sandbox.Core;
using EvacLogix.Sandbox.Infrastructure;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EvacLogix.Sandbox.UI.Menu
{
    // Front-door scene shown before the editor. Rendered with IMGUI so the MainMenu scene can stay
    // minimal (a camera + this component). "Editor Mode" loads the sandbox editor; "Simulation Mode"
    // loads the most recent saved/autosaved project into the dedicated simulation scene.
    public sealed class SandboxMainMenu : MonoBehaviour
    {
        [SerializeField] private string editorSceneName = "SandboxEditor";
        [SerializeField] private string simulationSceneName = SandboxSimulationLaunchContext.SimulationSceneName;
        [SerializeField] private string titleText = "EvacLogix";
        [SerializeField] private string subtitleText = "Evacuation Simulation Sandbox";
        [SerializeField] private Color backgroundColor = new(0.07f, 0.09f, 0.12f, 1f);

        private string simulationCaption = "Loads your most recent saved project";

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private GUIStyle menuButtonStyle;
        private GUIStyle captionStyle;
        private bool stylesReady;

        private void OnGUI()
        {
            EnsureStyles();
            DrawBackground();

            const float buttonWidth = 320f;
            const float buttonHeight = 64f;
            const float captionHeight = 20f;
            const float buttonSpacing = 18f;
            var centerX = Screen.width * 0.5f;

            var titleRect = new Rect(0f, Screen.height * 0.5f - 200f, Screen.width, 64f);
            GUI.Label(titleRect, titleText, titleStyle);
            GUI.Label(new Rect(0f, titleRect.yMax, Screen.width, 30f), subtitleText, subtitleStyle);

            // Simulation Mode sits above Editor Mode, per the agreed layout.
            var simulationRect = new Rect(centerX - buttonWidth * 0.5f, Screen.height * 0.5f - 40f, buttonWidth, buttonHeight);
            if (GUI.Button(simulationRect, "Simulation Mode", menuButtonStyle))
            {
                TryLaunchSimulationFromDisk();
            }

            GUI.Label(new Rect(simulationRect.x, simulationRect.yMax + 2f, buttonWidth, captionHeight), simulationCaption, captionStyle);

            var editorRect = new Rect(
                centerX - buttonWidth * 0.5f,
                simulationRect.yMax + captionHeight + buttonSpacing,
                buttonWidth,
                buttonHeight);
            if (GUI.Button(editorRect, "Editor Mode", menuButtonStyle))
            {
                SceneManager.LoadScene(editorSceneName, LoadSceneMode.Single);
            }
        }

        // Finds the most recent saved/autosaved project on disk, hands it to the simulation launch
        // context, and loads the simulation scene. P1 scans the autosave directory the editor writes
        // to; a richer recent-projects picker is a later addition.
        private void TryLaunchSimulationFromDisk()
        {
            try
            {
                var autosaveRoot = Path.Combine(Application.persistentDataPath, "SandboxAutosaves");
                if (!Directory.Exists(autosaveRoot))
                {
                    simulationCaption = "No saved project found — save one in the editor first.";
                    return;
                }

                var newestPath = string.Empty;
                var newestStamp = DateTime.MinValue;
                foreach (var path in Directory.GetFiles(autosaveRoot, "*.json", SearchOption.AllDirectories))
                {
                    var stamp = File.GetLastWriteTimeUtc(path);
                    if (stamp > newestStamp)
                    {
                        newestStamp = stamp;
                        newestPath = path;
                    }
                }

                if (string.IsNullOrWhiteSpace(newestPath))
                {
                    simulationCaption = "No saved project found — save one in the editor first.";
                    return;
                }

                var project = SandboxProjectFileStorage.ReadProjectFromPath(newestPath);
                if (project == null)
                {
                    simulationCaption = "Could not read the most recent project.";
                    return;
                }

                SandboxSimulationLaunchContext.SetFromProject(project, "MainMenu", $"Project: {project.metadata?.buildingName}");
                SceneManager.LoadScene(simulationSceneName, LoadSceneMode.Single);
            }
            catch (Exception exception)
            {
                simulationCaption = $"Failed to launch: {exception.Message}";
            }
        }

        private void DrawBackground()
        {
            var previousColor = GUI.color;
            GUI.color = backgroundColor;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private void EnsureStyles()
        {
            if (stylesReady)
            {
                return;
            }

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.white }
            };
            subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.7f, 0.76f, 0.85f, 1f) }
            };
            menuButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 22
            };
            captionStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.6f, 0.64f, 0.72f, 1f) }
            };
            stylesReady = true;
        }
    }
}
