using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using Halazia.UI;

namespace Halazia.Editor
{
    /// <summary>
    /// Automated builder for the HALAZIA: The Lost Signal opening screen / main menu.
    /// Creates Assets/Scenes/MainMenu.unity, configures dark sci-fi UI styling,
    /// sets up credits/controls panels, wires button events, and updates Build Settings.
    /// </summary>
    public static class MainMenuBuilder
    {
        private const string SceneDirectory = "Assets/Scenes";
        private const string ScenePath = "Assets/Scenes/MainMenu.unity";
        private const string PrimaryGameScene = "Assets/Scenes/halazia_Ship_World.unity";
        private const string SandboxGameScene = "Assets/Scenes/MovementSandbox.unity";

        // Sci-Fi Palette
        private static readonly Color ColorBgDark = new Color(0.04f, 0.06f, 0.08f, 1.0f);          // #0A0F14 deep naval black
        private static readonly Color ColorPanelDark = new Color(0.07f, 0.10f, 0.14f, 0.95f);      // #121A24 panel backdrop
        private static readonly Color ColorCyanAccent = new Color(0.0f, 0.90f, 1.0f, 1.0f);        // #00E5FF sci-fi cyan glow
        private static readonly Color ColorSubtleText = new Color(0.55f, 0.65f, 0.72f, 1.0f);      // #8CA6B8 sub-label grey
        private static readonly Color ColorButtonBase = new Color(0.09f, 0.14f, 0.20f, 0.92f);      // #172433 button normal
        private static readonly Color ColorButtonHover = new Color(0.0f, 0.50f, 0.65f, 0.95f);     // #0080A6 button highlight
        private static readonly Color ColorButtonPressed = new Color(0.0f, 0.85f, 1.0f, 1.0f);     // #00D9FF button pressed

        [MenuItem("Halazia/🎮 1-Click Create Opening Screen (Main Menu)", false, 5)]
        public static void BuildMainMenuScene()
        {
            Debug.Log("[Halazia] Starting automated Main Menu generation...");

            if (!Directory.Exists(SceneDirectory))
            {
                Directory.CreateDirectory(SceneDirectory);
                AssetDatabase.Refresh();
            }

            // 1. Create a fresh new scene
            var newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 2. Camera Setup
            var camObj = new GameObject("Main Camera");
            camObj.tag = "MainCamera";
            var cam = camObj.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ColorBgDark;
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            camObj.transform.position = new Vector3(0, 0, -10);

            // 3. Canvas Setup
            var canvasObj = new GameObject("MainMenu_Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObj.AddComponent<GraphicRaycaster>();

            // 4. EventSystem Setup
            CreateEventSystem();

            // 5. Fullscreen Background Image
            var bgObj = CreateUIElement("Background_Vignette", canvasObj.transform);
            var bgRect = bgObj.GetComponent<RectTransform>();
            StretchToParent(bgRect);
            var bgImg = bgObj.AddComponent<Image>();
            bgImg.color = ColorBgDark;

            // 6. Menu Controller
            var controllerObj = new GameObject("MenuController");
            var manager = controllerObj.AddComponent<MainMenuManager>();

            // 7. Panels
            var mainPanel = CreateMainPanel(canvasObj.transform, manager);
            var creditsPanel = CreateCreditsPanel(canvasObj.transform, manager);
            var controlsPanel = CreateControlsPanel(canvasObj.transform, manager);

            // Wire panel references onto MainMenuManager
            var so = new SerializedObject(manager);
            so.FindProperty("_mainMenuPanel").objectReferenceValue = mainPanel;
            so.FindProperty("_creditsPanel").objectReferenceValue = creditsPanel;
            so.FindProperty("_controlsPanel").objectReferenceValue = controlsPanel;
            so.ApplyModifiedProperties();

            // Initially hide secondary panels
            creditsPanel.SetActive(false);
            controlsPanel.SetActive(false);
            mainPanel.SetActive(true);

            // 8. Save Scene
            EditorSceneManager.SaveScene(newScene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 9. Update Build Settings (Make MainMenu Index 0)
            UpdateBuildSettings();

            Debug.Log($"[Halazia] Main Menu scene successfully built at: '{ScenePath}'. Build Settings updated!");
            EditorUtility.DisplayDialog(
                "Opening Screen Generated!",
                "MainMenu.unity has been created and configured!\n\n" +
                "• Added to Build Settings at Index 0 (Loads first)\n" +
                "• Full sci-fi styled Title, Play, Controls, Credits, and Quit\n" +
                "• Squad Manifest lists all 4 teammates & roles\n" +
                "• Press Play in Unity to test it!",
                "Awesome!"
            );
        }

        private static void CreateEventSystem()
        {
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();

            // Check if modern Input System UI module exists, otherwise use Standalone
            var inputModuleType = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (inputModuleType != null)
            {
                esObj.AddComponent(inputModuleType);
            }
            else
            {
                esObj.AddComponent<StandaloneInputModule>();
            }
        }

        private static GameObject CreateMainPanel(Transform parent, MainMenuManager manager)
        {
            var panelObj = CreateUIElement("MainPanel", parent);
            var panelRect = panelObj.GetComponent<RectTransform>();
            StretchToParent(panelRect);

            // --- Game Title ---
            var titleObj = CreateUIElement("TitleText", panelObj.transform);
            var titleRect = titleObj.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.5f, 0.72f);
            titleRect.anchorMax = new Vector2(0.5f, 0.72f);
            titleRect.sizeDelta = new Vector2(1200, 110);
            var titleText = titleObj.AddComponent<Text>();
            titleText.text = "HALAZIA: THE LOST SIGNAL";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            titleText.fontSize = 68;
            titleText.fontStyle = FontStyle.Bold;
            titleText.alignment = TextAnchor.MiddleCenter;
            titleText.color = ColorCyanAccent;

            // --- Subtitle ---
            var subObj = CreateUIElement("SubtitleText", panelObj.transform);
            var subRect = subObj.GetComponent<RectTransform>();
            subRect.anchorMin = new Vector2(0.5f, 0.63f);
            subRect.anchorMax = new Vector2(0.5f, 0.63f);
            subRect.sizeDelta = new Vector2(800, 40);
            var subText = subObj.AddComponent<Text>();
            subText.text = "DECK 4 // TACTICAL SURVIVAL & SQUAD COORDINATION";
            subText.font = titleText.font;
            subText.fontSize = 20;
            subText.alignment = TextAnchor.MiddleCenter;
            subText.color = ColorSubtleText;

            // --- Button Container ---
            var btnContainer = CreateUIElement("ButtonsContainer", panelObj.transform);
            var btnRect = btnContainer.GetComponent<RectTransform>();
            btnRect.anchorMin = new Vector2(0.5f, 0.35f);
            btnRect.anchorMax = new Vector2(0.5f, 0.35f);
            btnRect.sizeDelta = new Vector2(440, 280);

            var vlg = btnContainer.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 16;
            vlg.childAlignment = TextAnchor.MiddleCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // Buttons
            var btnPlay = CreateStyledButton("Btn_Play", btnContainer.transform, "COMMENCE MISSION", 56);
            var btnControls = CreateStyledButton("Btn_Controls", btnContainer.transform, "OPERATIONAL CONTROLS", 52);
            var btnCredits = CreateStyledButton("Btn_Credits", btnContainer.transform, "SQUAD MANIFEST (CREDITS)", 52);
            var btnQuit = CreateStyledButton("Btn_Quit", btnContainer.transform, "ABORT PROTOCOL (QUIT)", 52);

            // Wire events
            UnityEventTools.AddPersistentListener(btnPlay.onClick, manager.OnPlayButtonClicked);
            UnityEventTools.AddPersistentListener(btnControls.onClick, manager.OnControlsButtonClicked);
            UnityEventTools.AddPersistentListener(btnCredits.onClick, manager.OnCreditsButtonClicked);
            UnityEventTools.AddPersistentListener(btnQuit.onClick, manager.OnQuitButtonClicked);

            // Footer version
            var footerObj = CreateUIElement("FooterText", panelObj.transform);
            var footerRect = footerObj.GetComponent<RectTransform>();
            footerRect.anchorMin = new Vector2(0.5f, 0.05f);
            footerRect.anchorMax = new Vector2(0.5f, 0.05f);
            footerRect.sizeDelta = new Vector2(600, 30);
            var footerText = footerObj.AddComponent<Text>();
            footerText.text = "HALAZIA OS // CARGO VESSEL DECK 4";
            footerText.font = titleText.font;
            footerText.fontSize = 14;
            footerText.alignment = TextAnchor.MiddleCenter;
            footerText.color = new Color(0.25f, 0.35f, 0.45f, 1.0f);

            return panelObj;
        }

        private static GameObject CreateCreditsPanel(Transform parent, MainMenuManager manager)
        {
            var panelObj = CreateUIElement("CreditsPanel", parent);
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(920, 680);

            var img = panelObj.AddComponent<Image>();
            img.color = ColorPanelDark;

            // Header
            var titleText = CreateTextElement("CreditsTitle", panelObj.transform, "SQUAD MANIFEST // ENEMY COMBAT UNITS", 34, ColorCyanAccent, FontStyle.Bold);
            var tr = titleText.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0.5f, 0.90f);
            tr.anchorMax = new Vector2(0.5f, 0.90f);
            tr.sizeDelta = new Vector2(800, 50);

            // Roster Body - Pure in-game agent roles & capabilities (no personal/assignment info)
            string manifest =
                "UNIT 01: THE COMMANDER (Tactical Overseer)\n" +
                "  • Central intelligence coordinator overseeing Deck 4 operations.\n" +
                "  • Analyzes combat telemetry, predicts player escape routes using Markov modeling,\n" +
                "    and dynamically issues conflict-free tactical orders to the squad.\n\n" +
                "UNIT 02: THE HUNTER (Frontline Tracker & Pursuer)\n" +
                "  • High-aggression frontline assault specialist equipped with acoustic sensors.\n" +
                "  • Sprints directly down corridors toward last-known sightings to engage in\n" +
                "    relentless close-quarters melee combat.\n\n" +
                "UNIT 03: THE FLANKER (Lateral Infiltrator)\n" +
                "  • High-mobility stealth skirmisher utilizing secondary access corridors.\n" +
                "  • Executes lateral pincer movements to encircle targets and cut off retreat vectors\n" +
                "    from unexpected angles.\n\n" +
                "UNIT 04: THE SENTINEL (Area Security & Route Guardian)\n" +
                "  • Heavy patrol sentry guarding critical deck bulkheads and corridors.\n" +
                "  • Performs methodical search sweeps and rushes ahead to lock down predicted\n" +
                "    escape chokepoints before the player arrives.";

            var bodyText = CreateTextElement("CreditsBody", panelObj.transform, manifest, 17, Color.white, FontStyle.Normal);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.lineSpacing = 1.15f;
            var br = bodyText.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.52f);
            br.anchorMax = new Vector2(0.5f, 0.52f);
            br.sizeDelta = new Vector2(800, 390);

            // Back Button
            var backBtn = CreateStyledButton("Btn_BackFromCredits", panelObj.transform, "< RETURN TO CONSOLE", 50);
            var backRect = backBtn.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0.5f, 0.10f);
            backRect.anchorMax = new Vector2(0.5f, 0.10f);
            backRect.sizeDelta = new Vector2(360, 50);
            UnityEventTools.AddPersistentListener(backBtn.onClick, manager.OnBackButtonClicked);

            return panelObj;
        }

        private static GameObject CreateControlsPanel(Transform parent, MainMenuManager manager)
        {
            var panelObj = CreateUIElement("ControlsPanel", parent);
            var panelRect = panelObj.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(860, 600);

            var img = panelObj.AddComponent<Image>();
            img.color = ColorPanelDark;

            // Header
            var titleText = CreateTextElement("ControlsTitle", panelObj.transform, "TACTICAL OPERATIONAL CONTROLS", 34, ColorCyanAccent, FontStyle.Bold);
            var tr = titleText.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0.5f, 0.88f);
            tr.anchorMax = new Vector2(0.5f, 0.88f);
            tr.sizeDelta = new Vector2(760, 50);

            // Controls Body
            string controls =
                "NAVIGATION:\n" +
                "  • W / A / S / D  —  Corridor Locomotion & Lateral Strafe\n" +
                "  • MOUSE  —  Visual Acquisition & Targeting\n" +
                "  • LEFT SHIFT  —  Tactical Sprint (High Energy)\n\n" +
                "COMBAT & DEFENSE:\n" +
                "  • LEFT MOUSE CLICK  —  Close-Quarters Strike / Primary Action\n" +
                "  • E  —  Interact with Bulkheads, Terminals & Consoles\n" +
                "  • ESC  —  Release Cursor / Pause Operational Link";

            var bodyText = CreateTextElement("ControlsBody", panelObj.transform, controls, 20, Color.white, FontStyle.Normal);
            bodyText.alignment = TextAnchor.UpperLeft;
            bodyText.lineSpacing = 1.25f;
            var br = bodyText.GetComponent<RectTransform>();
            br.anchorMin = new Vector2(0.5f, 0.52f);
            br.anchorMax = new Vector2(0.5f, 0.52f);
            br.sizeDelta = new Vector2(740, 300);

            // Back Button
            var backBtn = CreateStyledButton("Btn_BackFromControls", panelObj.transform, "< RETURN TO CONSOLE", 50);
            var backRect = backBtn.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0.5f, 0.12f);
            backRect.anchorMax = new Vector2(0.5f, 0.12f);
            backRect.sizeDelta = new Vector2(360, 50);
            UnityEventTools.AddPersistentListener(backBtn.onClick, manager.OnBackButtonClicked);

            return panelObj;
        }

        private static Button CreateStyledButton(string name, Transform parent, string label, float height)
        {
            var btnObj = CreateUIElement(name, parent);
            var btnRect = btnObj.GetComponent<RectTransform>();
            btnRect.sizeDelta = new Vector2(400, height);

            var img = btnObj.AddComponent<Image>();
            img.color = ColorButtonBase;

            var btn = btnObj.AddComponent<Button>();
            var colors = btn.colors;
            colors.normalColor = ColorButtonBase;
            colors.highlightedColor = ColorButtonHover;
            colors.pressedColor = ColorButtonPressed;
            colors.selectedColor = ColorButtonHover;
            btn.colors = colors;

            // Label
            var text = CreateTextElement("Label", btnObj.transform, label, 18, Color.white, FontStyle.Bold);
            var textRect = text.GetComponent<RectTransform>();
            StretchToParent(textRect);

            return btn;
        }

        private static Text CreateTextElement(string name, Transform parent, string text, int fontSize, Color color, FontStyle style)
        {
            var obj = CreateUIElement(name, parent);
            var txt = obj.AddComponent<Text>();
            txt.text = text;
            txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            txt.fontSize = fontSize;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = TextAnchor.MiddleCenter;
            return txt;
        }

        private static GameObject CreateUIElement(string name, Transform parent)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.AddComponent<RectTransform>();
            return obj;
        }

        private static void StretchToParent(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void UpdateBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>();

            // Always put MainMenu first at index 0
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));

            // Add gameplay scene if it exists
            if (File.Exists(PrimaryGameScene))
            {
                scenes.Add(new EditorBuildSettingsScene(PrimaryGameScene, true));
            }

            // Add Sandbox scene as fallback
            if (File.Exists(SandboxGameScene))
            {
                scenes.Add(new EditorBuildSettingsScene(SandboxGameScene, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
