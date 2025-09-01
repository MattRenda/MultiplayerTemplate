using System.Collections;
using Mirror;
using UnityEngine;

/// <summary>
/// Ensures the cursor is visible and unlocked when the client disconnects (e.g., host leaves).
/// Safe to keep in all scenes; it auto-bootstraps and persists across loads.
/// </summary>
[DisallowMultipleComponent]
public class CursorResetOnDisconnect : MonoBehaviour
{
    [Tooltip("Also reset Time.timeScale to 1 on disconnect (in case gameplay paused it).")]
    public bool resetTimeScale = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        try
        {
            if (FindObjectOfType<CursorResetOnDisconnect>() != null) return;
            var go = new GameObject("CursorResetOnDisconnect");
            DontDestroyOnLoad(go);
            go.AddComponent<CursorResetOnDisconnect>();
        }
        catch { }
    }

    void OnEnable()
    {
        NetworkClient.OnDisconnectedEvent += HandleClientDisconnected;
    }

    void OnDisable()
    {
        NetworkClient.OnDisconnectedEvent -= HandleClientDisconnected;
    }

    void HandleClientDisconnected()
    {
        // Defer to end of frame to avoid being overridden by other callbacks
        StartCoroutine(ResetCursorEndOfFrame());
    }

    IEnumerator ResetCursorEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        try
        {
            if (resetTimeScale) Time.timeScale = 1f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }
        catch { }
    }
}
