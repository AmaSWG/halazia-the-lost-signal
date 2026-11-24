using UnityEngine;
using UnityEngine.SceneManagement;

namespace Halazia.UI
{
    /// <summary>
    /// Manages the opening screen / main menu for HALAZIA: The Lost Signal.
    /// Handles scene transitions, panel navigation (Play, Credits, Controls),
    /// cursor state, and clean game exit.
    /// </summary>
    public class MainMenuManager : MonoBehaviour
    {
        [Header("Scene Routing")]
        [Tooltip("Primary gameplay scene name (from Amaya's environment branch)")]
        [SerializeField] private string _primaryGameplayScene = "halazia_Ship_World";

        [Tooltip("Fallback scene if primary is not yet merged")]
        [SerializeField] private string _fallbackScene = "MovementSandbox";

        [Header("Menu Panels")]
        [SerializeField] private GameObject _mainMenuPanel;
        [SerializeField] private GameObject _creditsPanel;
        [SerializeField] private GameObject _controlsPanel;

        private void Start()
        {
            // Ensure cursor is free and visible for menu navigation
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ShowMainMenu();
        }

        /// <summary>
        /// Called when the player clicks "COMMENCE MISSION".
        /// Tries to load the primary ship level; falls back to the movement sandbox if not yet merged.
        /// </summary>
        public void OnPlayButtonClicked()
        {
            string targetScene = _primaryGameplayScene;

            // Check if primary scene can be loaded; if not, use fallback
            if (!Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.LogWarning($"[MainMenu] Scene '{targetScene}' not found in build settings. Attempting fallback: '{_fallbackScene}'.");
                targetScene = _fallbackScene;
            }

            if (Application.CanStreamedLevelBeLoaded(targetScene))
            {
                Debug.Log($"[MainMenu] Commencing mission. Loading scene: '{targetScene}'...");
                SceneManager.LoadScene(targetScene);
            }
            else
            {
                Debug.LogError($"[MainMenu] Neither '{_primaryGameplayScene}' nor '{_fallbackScene}' is in Build Settings! Please add scenes via File -> Build Settings.");
            }
        }

        /// <summary>
        /// Shows the team credits panel (Squad Manifest)
        /// </summary>
        public void OnCreditsButtonClicked()
        {
            if (_mainMenuPanel != null) _mainMenuPanel.SetActive(false);
            if (_controlsPanel != null) _controlsPanel.SetActive(false);
            if (_creditsPanel != null) _creditsPanel.SetActive(true);
        }

        /// <summary>
        /// Shows the operational controls guide panel
        /// </summary>
        public void OnControlsButtonClicked()
        {
            if (_mainMenuPanel != null) _mainMenuPanel.SetActive(false);
            if (_creditsPanel != null) _creditsPanel.SetActive(false);
            if (_controlsPanel != null) _controlsPanel.SetActive(true);
        }

        /// <summary>
        /// Returns back to the primary main menu panel
        /// </summary>
        public void OnBackButtonClicked()
        {
            ShowMainMenu();
        }

        /// <summary>
        /// Quits the game or exits Play Mode in editor
        /// </summary>
        public void OnQuitButtonClicked()
        {
            Debug.Log("[MainMenu] Aborting mission. Exiting application...");
            Application.Quit();

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void ShowMainMenu()
        {
            if (_creditsPanel != null) _creditsPanel.SetActive(false);
            if (_controlsPanel != null) _controlsPanel.SetActive(false);
            if (_mainMenuPanel != null) _mainMenuPanel.SetActive(true);
        }
    }
}
