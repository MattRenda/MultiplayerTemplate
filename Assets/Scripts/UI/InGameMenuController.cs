using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.IO;
using System.Linq;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if MIRROR
using Mirror;
#endif

public class InGameMenuController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Root panel of the in-game menu (enable/disable to show/hide)")]
    public GameObject menuRoot;
    [Tooltip("Resume button")] public Button resumeButton;
    [Tooltip("Settings button (already wired to settings page via UI)")] public Button settingsButton;
    [Tooltip("Quit to Main Menu button")] public Button quitButton;

    [Header("Options")]
    [Tooltip("Name of the Main Menu scene to load on quit")] public string mainMenuSceneName = "MainMenu";
    [Tooltip("Pause time scale when menu is open")] public bool pauseOnOpen = true;

    bool _isOpen;

    void Awake()
    {
        if (resumeButton) resumeButton.onClick.AddListener(Resume);
        if (quitButton) quitButton.onClick.AddListener(QuitToMainMenu);

        // Try to auto-detect menu root if not assigned
        if (menuRoot == null)
        {
            // Prefer a disabled child with Canvas or a child named like "Menu"
            Transform t = transform;
            var candidate = t.GetComponentsInChildren<Canvas>(true).Select(c => c.gameObject)
                              .FirstOrDefault(go => go != this.gameObject && go.transform.IsChildOf(t));
            if (candidate == null)
                candidate = t.GetComponentsInChildren<RectTransform>(true)
                             .Select(rt => rt.gameObject)
                             .FirstOrDefault(go => go.name.ToLower().Contains("menu") && go != this.gameObject);
            if (candidate != null)
                menuRoot = candidate;
        }

        // Start hidden by default
        SetOpen(false, instant: true);
    }

    void Update()
    {
    // Toggle on Esc (support Old + New Input Systems)
    bool esc = Input.GetKeyDown(KeyCode.Escape);
#if ENABLE_INPUT_SYSTEM
    if (Keyboard.current != null)
        esc |= Keyboard.current.escapeKey.wasPressedThisFrame;
#endif
    if (esc)
        {
            SetOpen(!_isOpen);
        }
    }

    public void Resume()
    {
        SetOpen(false);
    }

    public void QuitToMainMenu()
    {
        // Unpause
        if (pauseOnOpen) Time.timeScale = 1f;
        UnlockCursor();

#if MIRROR
        // If Mirror is active, prefer stopping host/client which will return to offlineScene if set
        if (NetworkManager.singleton != null)
        {
            if (string.IsNullOrWhiteSpace(NetworkManager.singleton.offlineScene) && !string.IsNullOrWhiteSpace(mainMenuSceneName))
                NetworkManager.singleton.offlineScene = mainMenuSceneName;

            if (NetworkServer.active && NetworkClient.isConnected)
            {
                NetworkManager.singleton.StopHost();
                return;
            }
            if (NetworkClient.isConnected)
            {
                NetworkManager.singleton.StopClient();
                return;
            }
        }
#endif
        // Fallback: find MainMenu by name in Build Settings and load by index
        if (!string.IsNullOrWhiteSpace(mainMenuSceneName))
        {
            int index = FindSceneIndexByName(mainMenuSceneName);
            if (index >= 0)
            {
                SceneManager.LoadScene(index);
                return;
            }
            Debug.LogError(
                $"InGameMenuController: Scene '{mainMenuSceneName}' is not in the active Build Profile.\n" +
                "Add it via File -> Build Profiles (or Build Settings) under 'Scenes in Build'.");
        }
    }

    static int FindSceneIndexByName(string sceneName)
    {
        // iterate scenes in active build profile by index and compare filenames without extension
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            string fileName = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(fileName, sceneName, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    void SetOpen(bool open, bool instant = false)
    {
        _isOpen = open;
        if (menuRoot)
        {
            menuRoot.SetActive(open);
        }
        else
        {
            if (open)
                Debug.LogWarning("InGameMenuController: menuRoot not assigned and could not be auto-detected. Assign it in the inspector.");
        }

    // Pause/unpause
    // In multiplayer, never pause global timeScale on host or clients.
    // Server physics must keep running, otherwise clients see objects frozen mid-air.
    if (pauseOnOpen)
    {
#if MIRROR
        if (!IsNetworkSessionActive())
        Time.timeScale = open ? 0f : 1f;
        // else: keep timeScale unchanged (== 1) during network sessions
#else
        Time.timeScale = open ? 0f : 1f;
#endif
    }

        // Mouse cursor
        if (open) UnlockCursor(); else LockCursor();

        // Optionally disable player input when open
        TogglePlayerControls(!open);
    }

#if MIRROR
    static bool IsNetworkSessionActive()
    {
        // Network is considered active if either server is running or client is connected
        return NetworkServer.active || NetworkClient.isConnected;
    }
#endif

    void TogglePlayerControls(bool enabled)
    {
        // Disable our local player controller and camera look, if found in scene
    // Try to find a PlayerCharacterController in scene even if not tagged
    PlayerCharacterController controller = null;
    var player = GameObject.FindWithTag("Player");
    if (player != null) controller = player.GetComponent<PlayerCharacterController>();
    if (controller == null)
    {
#if UNITY_2023_1_OR_NEWER
            controller = UnityEngine.Object.FindFirstObjectByType<PlayerCharacterController>(FindObjectsInactive.Include);
#else
            controller = UnityEngine.Object.FindObjectOfType<PlayerCharacterController>();
#endif
    }
    if (controller != null) controller.enabled = enabled;
    }

    void LockCursor()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void UnlockCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
