using System;
using System.Linq;
using Mirror;
using UnityEngine;

/// <summary>
/// Handles Steam overlay invites: when you receive a lobby invite or click Join on a friend,
/// this component joins the lobby and connects the Mirror client via Fizzy Steam transport.
/// Safe no-op if Steamworks is not available.
/// </summary>
[DisallowMultipleComponent]
public class SteamOverlayInvites : MonoBehaviour
{
#if !DISABLESTEAMWORKS
    Steamworks.Callback<Steamworks.GameLobbyJoinRequested_t> _cbJoinRequested;
    Steamworks.Callback<Steamworks.LobbyEnter_t> _cbLobbyEnter;
    Steamworks.Callback<Steamworks.GameRichPresenceJoinRequested_t> _cbRichPresenceJoin;
    Steamworks.CSteamID _pendingLobby = Steamworks.CSteamID.Nil;
    bool _pumpInstalled;
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Bootstrap()
    {
        try
        {
            var go = new GameObject("SteamOverlayInvites");
            DontDestroyOnLoad(go);
            go.AddComponent<SteamOverlayInvites>();
        }
        catch { }
    }

    void Awake()
    {
#if !DISABLESTEAMWORKS
        TryInit();
#endif
    }

#if !DISABLESTEAMWORKS
    void TryInit()
    {
        try
        {
            if (!Steamworks.SteamAPI.Init()) return;
            if (!_pumpInstalled)
            {
                // Pump Steam callbacks so overlay events fire reliably even when not hosting
                Application.onBeforeRender += PumpCallbacks;
                _pumpInstalled = true;
            }
            if (_cbJoinRequested == null)
            {
                _cbJoinRequested = Steamworks.Callback<Steamworks.GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
            }
            if (_cbLobbyEnter == null)
            {
                _cbLobbyEnter = Steamworks.Callback<Steamworks.LobbyEnter_t>.Create(OnLobbyEnter);
            }
            if (_cbRichPresenceJoin == null)
            {
                _cbRichPresenceJoin = Steamworks.Callback<Steamworks.GameRichPresenceJoinRequested_t>.Create(OnGameRichPresenceJoinRequested);
            }
        }
        catch { }
    }

    void OnGameLobbyJoinRequested(Steamworks.GameLobbyJoinRequested_t ev)
    {
        try
        {
            Debug.Log($"[SteamOverlayInvites] GameLobbyJoinRequested lobby={ev.m_steamIDLobby.m_SteamID}");
            _pendingLobby = ev.m_steamIDLobby;
            Steamworks.SteamMatchmaking.JoinLobby(_pendingLobby);
        }
        catch { }
    }

    void OnGameRichPresenceJoinRequested(Steamworks.GameRichPresenceJoinRequested_t ev)
    {
        try
        {
            var connect = ev.m_rgchConnect;
            if (string.IsNullOrEmpty(connect)) return;
            Debug.Log($"[SteamOverlayInvites] GameRichPresenceJoinRequested connect='{connect}'");
            // If already connected/hosting, ignore
            if (NetworkServer.active || NetworkClient.isConnected) return;

            // Normalize to steam URI if needed
            string uriStr = connect.StartsWith("steam://", StringComparison.OrdinalIgnoreCase)
                ? connect
                : ($"steam://{connect}");
            var uri = new Uri(uriStr);

            var nm = NetworkManager.singleton ?? FindAny<NetworkManager>(true);
            if (nm == null) return;
            var fizzy = EnsureFizzyTransport(nm.gameObject);
            if (fizzy == null) return;
            if (Transport.active != fizzy) Transport.active = fizzy;
            if (nm.transport != fizzy) nm.transport = fizzy;
            nm.StartClient(uri);
        }
        catch { }
    }

    void OnLobbyEnter(Steamworks.LobbyEnter_t ev)
    {
        try
        {
            var id = new Steamworks.CSteamID(ev.m_ulSteamIDLobby);
            Debug.Log($"[SteamOverlayInvites] LobbyEnter lobby={id.m_SteamID} pending={_pendingLobby.m_SteamID}");
            if (_pendingLobby.IsValid() && id != _pendingLobby)
                return; // ignore unrelated entries

            // If already connected/hosting, ignore
            if (NetworkServer.active || NetworkClient.isConnected) return;

            // Determine host ID either from lobby data or owner
            string hostId = Steamworks.SteamMatchmaking.GetLobbyData(id, "HostAddress");
            if (string.IsNullOrEmpty(hostId))
            {
                try { hostId = Steamworks.SteamMatchmaking.GetLobbyOwner(id).m_SteamID.ToString(); } catch { }
            }
            if (string.IsNullOrEmpty(hostId)) return;

            // Ensure FizzySteamworks transport is active
            var nm = NetworkManager.singleton ?? FindAny<NetworkManager>(true);
            if (nm == null) return;
            var fizzy = EnsureFizzyTransport(nm.gameObject);
            if (fizzy == null) return;
            if (Transport.active != fizzy) Transport.active = fizzy;
            if (nm.transport != fizzy) nm.transport = fizzy;

            // Connect using steam URI
            var uri = new Uri($"steam://{hostId}");
            nm.StartClient(uri);
        }
        catch { }
    }

    void OnDisable()
    {
#if !DISABLESTEAMWORKS
        if (_pumpInstalled)
        {
            Application.onBeforeRender -= PumpCallbacks;
            _pumpInstalled = false;
        }
#endif
    }

#if !DISABLESTEAMWORKS
    void PumpCallbacks()
    {
        try { Steamworks.SteamAPI.RunCallbacks(); } catch { }
    }
#endif

    static Mirror.Transport EnsureFizzyTransport(GameObject host)
    {
        // find existing
        var all = UnityEngine.Object.FindObjectsByType<Mirror.Transport>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var existing = all.FirstOrDefault(t => t != null && t.GetType().Name.IndexOf("Fizzy", StringComparison.OrdinalIgnoreCase) >= 0);
        if (existing != null)
        {
            existing.enabled = true;
            return existing;
        }
        // try add by common names
        var type = FindType(new[]
        {
            "Mirror.FizzySteam.FizzySteamworks",
            "FizzySteamworks.FizzySteamworks",
            "Mirror.FizzySteamworks.FizzySteamworks",
            "FizzySteamworks"
        });
        if (type == null) return null;
        try
        {
            var t = host.AddComponent(type) as Mirror.Transport;
            if (t != null) t.enabled = true;
            return t;
        }
        catch { return null; }
    }

    static Type FindType(string[] names)
    {
        foreach (var n in names)
        {
            var t = Type.GetType(n, false);
            if (t != null) return t;
        }
        try
        {
            var asms = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var a in asms)
            {
                foreach (var n in names)
                {
                    var shortName = n.Split('.').Last();
                    var t = a.GetTypes().FirstOrDefault(x => x.Name == shortName);
                    if (t != null) return t;
                }
            }
        }
        catch { }
        return null;
    }

    static T FindAny<T>(bool includeInactive = false) where T : UnityEngine.Object
    {
#if UNITY_2022_2_OR_NEWER
        return GameObject.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
        return UnityEngine.Object.FindObjectOfType<T>();
#endif
    }
#endif // !DISABLESTEAMWORKS
}
