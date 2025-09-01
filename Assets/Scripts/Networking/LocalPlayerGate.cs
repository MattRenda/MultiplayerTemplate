using Mirror;
using UnityEngine;

/// <summary>
/// Enables local-only components (like PlayerCharacterController & Camera)
/// and disables them for remote players, so input only affects the local player.
/// Attach this to your Player prefab (same object that has NetworkIdentity).
/// </summary>
[DisallowMultipleComponent]
public class LocalPlayerGate : NetworkBehaviour
{
    [Header("Optional explicit assignments (auto-detected if empty)")]
    public MonoBehaviour[] enableOnlyForLocal; // e.g., PlayerCharacterController
    public Behaviour[] disableForRemote; // e.g., Camera, AudioListener

    void Awake()
    {
        // Auto-detect common components if not explicitly assigned.
        if (enableOnlyForLocal == null || enableOnlyForLocal.Length == 0)
        {
            var pcc = GetComponent<PlayerCharacterController>();
            if (pcc != null)
                enableOnlyForLocal = new MonoBehaviour[] { pcc };
        }
        if (disableForRemote == null || disableForRemote.Length == 0)
        {
            var cam = GetComponentInChildren<Camera>(true);
            var audio = cam != null ? cam.GetComponent<AudioListener>() : null;
            if (cam != null && audio != null)
                disableForRemote = new Behaviour[] { cam, audio };
            else if (cam != null)
                disableForRemote = new Behaviour[] { cam };
        }
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (!isLocalPlayer)
            ApplyRemoteState();
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        ApplyLocalState();
    SetCursorLocked(true);
    }

    void ApplyLocalState()
    {
        // Enable control scripts for local player
        if (enableOnlyForLocal != null)
        {
            foreach (var mb in enableOnlyForLocal)
                if (mb != null) mb.enabled = true;
        }
        // Ensure local camera/audio are enabled
        if (disableForRemote != null)
        {
            foreach (var b in disableForRemote)
                if (b != null) b.enabled = true;
        }
    }

    void ApplyRemoteState()
    {
        // Disable control scripts for remote players
        if (enableOnlyForLocal != null)
        {
            foreach (var mb in enableOnlyForLocal)
                if (mb != null) mb.enabled = false;
        }
        // Disable remote cameras/audio so only the local camera renders/hears
        if (disableForRemote != null)
        {
            foreach (var b in disableForRemote)
                if (b != null) b.enabled = false;
        }
        SetCursorLocked(false);
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
