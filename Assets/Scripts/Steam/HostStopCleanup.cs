using Mirror;
using UnityEngine;

/// <summary>
/// Monitors Mirror server state and destroys the Steam lobby when hosting stops.
/// Safe no-op if Steam is unavailable.
/// </summary>
[DisallowMultipleComponent]
public class HostStopCleanup : MonoBehaviour
{
    bool wasServer;

    void OnEnable()
    {
        wasServer = NetworkServer.active;
    }

    void Update()
    {
        // if we were hosting and now not, cleanup lobby once
        if (wasServer && !NetworkServer.active)
        {
            TryDestroyLobby();
            wasServer = false;
        }
        else if (NetworkServer.active)
        {
            wasServer = true;
        }
    }

    void OnDestroy()
    {
        // Also cleanup on destroy just in case
        TryDestroyLobby();
    }

    void TryDestroyLobby()
    {
        try { SteamLobbyUtil.DestroyLobby(); } catch { }
    }
}
