using UnityEngine;

// Provides minimal Steam lobby advertise/query helpers behind compile guards.
// Safe to include in projects without Steamworks; all Steam calls are wrapped.
public static class SteamLobbyUtil
{
#if !DISABLESTEAMWORKS
    static Steamworks.CSteamID _lobby = Steamworks.CSteamID.Nil;
    static bool _callbacksInstalled;
    static Steamworks.Callback<Steamworks.LobbyCreated_t> _cbLobbyCreated;
    static Steamworks.Callback<Steamworks.LobbyEnter_t> _cbLobbyEnter;
    static Steamworks.Callback<Steamworks.LobbyChatUpdate_t> _cbLobbyChatUpdate;

    static bool EnsureSteamInitialized()
    {
        try
        {
            // If already initialized, this is a no-op in Steamworks.NET.
            return Steamworks.SteamAPI.Init();
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAvailable()
    {
        return EnsureSteamInitialized();
    }

    public static void CreateOrUpdateLobby(long handshake, string lobbyName, int maxMembers = 8, bool friendsOnly = false)
    {
        if (!EnsureSteamInitialized())
        {
            Debug.LogWarning("SteamLobbyUtil: SteamAPI.Init failed; cannot create lobby.");
            return;
        }

        if (!_callbacksInstalled)
        {
            // Ensure callbacks get processed
            UnityEngine.Application.onBeforeRender += PumpCallbacks;
            _callbacksInstalled = true;
            // Track lobby membership and keep presence in sync
            try
            {
                _cbLobbyEnter = Steamworks.Callback<Steamworks.LobbyEnter_t>.Create((ev) =>
                {
                    try
                    {
                        if (_lobby.IsValid())
                        {
                            var size = Steamworks.SteamMatchmaking.GetNumLobbyMembers(_lobby);
                            Steamworks.SteamFriends.SetRichPresence("steam_player_group", _lobby.m_SteamID.ToString());
                            Steamworks.SteamFriends.SetRichPresence("steam_player_group_size", size.ToString());
                            Steamworks.SteamFriends.SetRichPresence("status", "In Lobby");
                        }
                    }
                    catch { }
                });
                _cbLobbyChatUpdate = Steamworks.Callback<Steamworks.LobbyChatUpdate_t>.Create((ev) =>
                {
                    try
                    {
                        if (_lobby.IsValid())
                        {
                            var size = Steamworks.SteamMatchmaking.GetNumLobbyMembers(_lobby);
                            Steamworks.SteamFriends.SetRichPresence("steam_player_group_size", size.ToString());
                        }
                    }
                    catch { }
                });
            }
            catch { }
        }

    if (_lobby.IsValid())
        {
            try
            {
                // Update metadata if lobby already exists
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "name", string.IsNullOrEmpty(lobbyName) ? Application.productName : lobbyName);
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "handshake", handshake.ToString());
                // Provide host's steam id for clients to connect
                var hostId = Steamworks.SteamUser.GetSteamID().m_SteamID.ToString();
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "HostAddress", hostId);
                // Rich Presence: show join option and group
                try {
                    Steamworks.SteamFriends.SetRichPresence("connect", hostId);
                    Steamworks.SteamFriends.SetRichPresence("steam_player_group", _lobby.m_SteamID.ToString());
                    Steamworks.SteamFriends.SetRichPresence("steam_player_group_size", "1");
                    Steamworks.SteamFriends.SetRichPresence("status", "In Lobby");
                } catch { }
            }
            catch { }
            return;
        }

    var type = friendsOnly ? Steamworks.ELobbyType.k_ELobbyTypeFriendsOnly : Steamworks.ELobbyType.k_ELobbyTypePublic;
    Steamworks.SteamMatchmaking.CreateLobby(type, maxMembers);
    // Persist callback so GC doesn't collect it
    _cbLobbyCreated ??= Steamworks.Callback<Steamworks.LobbyCreated_t>.Create((cb) =>
        {
            if (cb.m_eResult != Steamworks.EResult.k_EResultOK)
            {
                Debug.LogWarning($"SteamLobbyUtil: Lobby creation failed: {cb.m_eResult}");
                return;
            }
            _lobby = new Steamworks.CSteamID(cb.m_ulSteamIDLobby);
            try
            {
                Steamworks.SteamMatchmaking.SetLobbyJoinable(_lobby, true);
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "name", string.IsNullOrEmpty(lobbyName) ? Application.productName : lobbyName);
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "handshake", handshake.ToString());
                var hostId = Steamworks.SteamUser.GetSteamID().m_SteamID.ToString();
                Steamworks.SteamMatchmaking.SetLobbyData(_lobby, "HostAddress", hostId);
                try {
                    Steamworks.SteamFriends.SetRichPresence("connect", hostId);
                    Steamworks.SteamFriends.SetRichPresence("steam_player_group", _lobby.m_SteamID.ToString());
                    Steamworks.SteamFriends.SetRichPresence("steam_player_group_size", "1");
                    Steamworks.SteamFriends.SetRichPresence("status", "In Lobby");
                } catch { }
            }
            catch { }
            Debug.Log($"SteamLobbyUtil: Lobby created {_lobby.m_SteamID}");
        });
    }

    public static void DestroyLobby()
    {
        if (!EnsureSteamInitialized()) return;
        if (_lobby.IsValid())
        {
            try { Steamworks.SteamMatchmaking.LeaveLobby(_lobby); } catch { }
            _lobby = Steamworks.CSteamID.Nil;
            try { Steamworks.SteamFriends.ClearRichPresence(); } catch { }
        }
    }

    static void PumpCallbacks()
    {
        try { Steamworks.SteamAPI.RunCallbacks(); } catch { }
    }

    public static ulong CurrentLobbyId()
    {
        return _lobby.IsValid() ? _lobby.m_SteamID : 0UL;
    }

    public static bool OpenInviteDialog()
    {
        if (!EnsureSteamInitialized()) return false;
        if (!_lobby.IsValid()) return false;
        try { Steamworks.SteamFriends.ActivateGameOverlayInviteDialog(_lobby); return true; } catch { return false; }
    }
#else
    public static bool IsAvailable() => false;
    public static void CreateOrUpdateLobby(long handshake, string lobbyName, int maxMembers = 8, bool friendsOnly = true) { }
    public static void DestroyLobby() { }
    public static ulong CurrentLobbyId() => 0UL;
    public static bool OpenInviteDialog() => false;
#endif
}
