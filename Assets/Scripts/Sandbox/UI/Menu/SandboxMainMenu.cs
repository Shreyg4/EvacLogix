using UnityEngine;
using UnityEngine.SceneManagement;

namespace EvacLogix.Sandbox.UI.Menu
{
    // Front-door scene shown before the editor. Rendered with IMGUI so the MainMenu scene can stay
    // minimal (a camera + this component). "Editor Mode" loads the sandbox editor; "Simulation Mode"
    // is reserved for the future evacuation simulation and stays disabled for now.
    public sealed class SandboxMainMenu : MonoBehaviour
    {
        [SerializeField] private string editorSceneName = "SandboxEditor";
        [SerializeField] private string titleText = "EvacLogix";
        [SerializeField] private string subtitleText = "Evacuation Simulation Sandbox";
        [SerializeField] private Color backgroundColor = new(0.07f, 0.09f, 0.12f, 1f);

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

            // Simulation Mode (disabled) sits above Editor Mode, per the agreed layout.
            var simulationRect = new Rect(centerX - buttonWidth * 0.5f, Screen.height * 0.5f - 40f, buttonWidth, buttonHeight);
            var previousEnabled = GUI.enabled;
            GUI.enabled = false;
            GUI.Button(simulationRect, "Simulation Mode", menuButtonStyle);
            GUI.enabled = previousEnabled;
            GUI.Label(new Rect(simulationRect.x, simulationRect.yMax + 2f, buttonWidth, captionHeight), "Coming soon", captionStyle);

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
