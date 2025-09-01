using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using Mirror;
using Mirror.Discovery;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

/// <summary>
/// Lists LAN servers discovered via Mirror NetworkDiscovery.
/// Requires a NetworkDiscovery component in the scene.
/// Expects a row prefab with a ServerRowUI component to display server info and a Join button.
/// </summary>
[DisallowMultipleComponent]
public class ServerBrowserUI : MonoBehaviour
{
    [Header("Discovery")]
    public NetworkDiscovery discovery; // auto-find if null

    [Header("UI")]
    public Transform contentRoot; // parent where rows go (e.g., a VerticalLayoutGroup)
    public GameObject serverRowPrefab; // must have ServerRowUI on root
    public Button refreshButton; // optional; auto-find by name "Refresh"
    public Text emptyLabel; // optional; shown when no servers discovered

    [Header("Target List")]
    [Tooltip("Name of the GameObject that holds the Vertical Layout Group for rows.")]
    public string serverListName = "ServerList";

    [Header("Debug/Test")]
    [Tooltip("If true, shows a dummy test server row when nothing is discovered yet.")]
    public bool showTestRowIfEmpty = true;
    public string testRowAddress = "127.0.0.1";
    public ushort testRowPort = 7777;

    [Header("Discovery Options")]
    [Tooltip("Also send discovery to 127.0.0.1 for same-machine host discovery.")]
    public bool searchLocalhostAlso = true;
    [Tooltip("Also send discovery to each local subnet's broadcast address (e.g., 192.168.1.255). Useful with multiple NICs.")]
    public bool searchLocalSubnets = true;
    [Tooltip("When Steam transport is active, hide LAN discovery results and only show Steam lobbies.")]
    public bool hideLanWhenSteamMode = true;

    [Header("Steam Only")]
    [Tooltip("Force Steam-only browsing. Hides LAN discovery and requires Fizzy.")]
    public bool forceSteamOnly = true;

    [Header("Steam (optional)")]
    [Tooltip("If a Steam transport is active, also list Steam lobbies and allow joining by clicking.")]
    public bool includeSteamLobbies = true;

    readonly Dictionary<long, ServerResponse> _servers = new();
    readonly Dictionary<long, float> _lastSeen = new();

#if !DISABLESTEAMWORKS
    Steamworks.HSteamListenSocket _steamListenSocket; // unused placeholder; we only query lobbies
    System.Collections.Generic.List<Steamworks.CSteamID> _steamLobbyIds = new();
    bool _steamCallbacksInstalled;
    Steamworks.Callback<Steamworks.LobbyMatchList_t> _cbLobbyList;
#endif

    [Header("List Lifetime")]
    [Tooltip("Seconds without being rediscovered before a server is removed from the list.")]
    public float serverTimeoutSeconds = 6f;
    [Tooltip("How often to check and prune stale servers.")]
    public float pruneIntervalSeconds = 1.5f;
    float _nextPruneTime;

    // Simpler behavior: no pruning/intervals. The list is rebuilt on discovery/refresh only.

    // Unity-version-safe helpers
    static T FindAny<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_2022_2_OR_NEWER
    return GameObject.FindAnyObjectByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
#else
    // Fallback for older versions
    return FindObjectOfType<T>();
#endif
    }
    static T[] FindAll<T>(bool includeInactive = false) where T : Object
    {
#if UNITY_2022_2_OR_NEWER
    return GameObject.FindObjectsByType<T>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#else
    return FindObjectsOfType<T>();
#endif
    }

    void Awake()
    {
        if (discovery == null)
            discovery = FindAny<NetworkDiscovery>(true);
        if (refreshButton == null)
        {
            var buttons = FindAll<Button>(true);
            foreach (var b in buttons)
                if (b.name == "Refresh") { refreshButton = b; break; }
        }
        if (refreshButton != null)
        {
            // Ensure our manual refresh is hooked
            refreshButton.onClick.RemoveListener(Refresh);
            refreshButton.onClick.RemoveListener(RefreshButtonClicked);
            refreshButton.onClick.AddListener(RefreshButtonClicked);
        }
    }

    void OnEnable()
    {
        // Ensure we have somewhere to render rows
        EnsureUIRoots();
        if (contentRoot != null)
            Debug.Log($"ServerBrowserUI: Using contentRoot=/{GetHierarchyPath(contentRoot)} active={contentRoot.gameObject.activeInHierarchy}");
#if UNITY_EDITOR
        if (serverRowPrefab == null)
        {
            try { EditorEnsureRowPrefab(); }
            catch (System.Exception ex) { Debug.LogWarning($"ServerBrowserUI: EditorEnsureRowPrefab failed: {ex.Message}"); }
        }
#endif
    if (discovery == null && !forceSteamOnly)
        {
            // Prefer the discovery attached to NetworkManager if any, otherwise create one
            var nm = NetworkManager.singleton ?? FindAny<NetworkManager>(true);
            if (nm != null)
                discovery = nm.GetComponent<NetworkDiscovery>();
            if (discovery == null)
                discovery = FindAny<NetworkDiscovery>(true);
            if (discovery == null)
            {
                if (nm != null)
                {
                    discovery = nm.gameObject.AddComponent<NetworkDiscovery>();
                    Debug.Log("ServerBrowserUI: Auto-added NetworkDiscovery to NetworkManager.");
                }
                else
                {
                    discovery = gameObject.AddComponent<NetworkDiscovery>();
                    Debug.Log("ServerBrowserUI: Auto-added NetworkDiscovery to ServerBrowserUI GameObject.");
                }
            }
            // Align transport and handshake
            if (discovery != null)
            {
                if (discovery.transport == null)
                {
                    var active = Transport.active;
                    if (active == null && nm != null) active = nm.transport;
                    discovery.transport = active;
                }
                discovery.secretHandshake = ComputeHandshake();
            }
        }
    if (discovery != null && !forceSteamOnly)
        {
            discovery.OnServerFound.RemoveListener(OnDiscoveredServer);
            discovery.OnServerFound.AddListener(OnDiscoveredServer);
        }
        // Clean any orphan UI (e.g., if container changed) but keep dictionaries so rows persist
        ClearUIRows();
        Refresh();
    }

    void OnDisable()
    {
        if (discovery != null)
            discovery.OnServerFound.RemoveListener(OnDiscoveredServer);
#if !DISABLESTEAMWORKS
        if (_steamCallbacksInstalled)
        {
            Application.onBeforeRender -= PumpSteamCallbacks;
            _steamCallbacksInstalled = false;
        }
#endif
    }

    public void Refresh()
    {
        // Keep current servers visible during refresh; pruning will remove stale ones
        bool steamMode = false;
        {
            var active = Transport.active;
            steamMode = active != null && active.GetType().Name.IndexOf("Fizzy", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
        if (discovery == null && !forceSteamOnly)
        {
            Debug.LogWarning("ServerBrowserUI: No NetworkDiscovery available; cannot discover servers. Showing test row only.");
            RebuildList();
            SetEmptyVisible(_servers.Count == 0);
            return;
        }
        if (!forceSteamOnly)
        {
            try { discovery.StopDiscovery(); } catch { }
            if (discovery.transport == null) discovery.transport = Transport.active;
            // Use same handshake strategy as MyNetworkManager.ComputeHandshake
            discovery.secretHandshake = ComputeHandshake();
            if (!(steamMode && hideLanWhenSteamMode))
            {
                // Ensure we actually send discovery packets
                discovery.enableActiveDiscovery = true;
                Debug.Log($"ServerBrowserUI: Starting LAN discovery... transport={(discovery.transport != null ? discovery.transport.GetType().Name : "null")} handshake={discovery.secretHandshake}");
                try
                {
                    discovery.StartDiscovery();
                    // fire one request immediately so we don't wait for the first interval
                    discovery.BroadcastDiscoveryRequest();
                    Debug.Log("ServerBrowserUI: Broadcast sent.");

                    // Optionally also try loopback and per-interface broadcast addresses
                    if (searchLocalhostAlso)
                    {
                        TrySendToAddress(discovery, IPAddress.Loopback);
                    }
                    if (searchLocalSubnets)
                    {
                        foreach (var bcast in GetLocalBroadcastAddresses())
                        {
                            TrySendToAddress(discovery, bcast);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"ServerBrowserUI: Discovery failed to start or broadcast: {ex.Message}. Showing test row only.");
                }
            }
        }
        // Build view from existing servers immediately; new ones will appear as discovered
    RebuildList();
    // Also query Steam lobbies if requested and Steam transport looks active
    TryQuerySteamLobbies();
        SetEmptyVisible(_servers.Count == 0);
    }

    void OnDiscoveredServer(ServerResponse info)
    {
    Debug.Log($"ServerBrowserUI: Discovered server id={info.serverId} uri={info.uri} ep={info.EndPoint}");
        _servers[info.serverId] = info;
    _lastSeen[info.serverId] = Time.unscaledTime;
    RebuildList();
    }

    static long ComputeHandshake()
    {
        unchecked
        {
            string s = Application.productName;
            long h = 1469598103934665603L;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211L;
            }
            return h;
        }
    }

    void RebuildList()
    {
        // Make sure UI roots exist
    if (!EnsureUIRoots())
        {
            Debug.LogError("ServerBrowserUI: Unable to determine a contentRoot to render rows.");
            return;
        }
    Debug.Log($"ServerBrowserUI: RebuildList servers={_servers.Count}");
        bool hasPrefab = serverRowPrefab != null;
        if (!hasPrefab)
        {
            Debug.LogWarning("ServerBrowserUI: serverRowPrefab is not assigned. Using a simple runtime row instead.");
        }
        if (contentRoot == null)
        {
            Debug.LogError("ServerBrowserUI: contentRoot is not assigned.");
            return;
        }
        if (!contentRoot.gameObject.activeInHierarchy)
            Debug.LogWarning("ServerBrowserUI: contentRoot is not active in hierarchy; rows may not be visible.");
        // Ensure content has a layout group so children are positioned
        var vlg = contentRoot.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.spacing = 4f;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            Debug.LogWarning("ServerBrowserUI: Added VerticalLayoutGroup to contentRoot at runtime. Consider adding one in the prefab.");
        }
        // Create a simple empty label if none assigned
        if (emptyLabel == null)
        {
            var go = new GameObject("EmptyLabel", typeof(RectTransform));
            go.transform.SetParent(contentRoot, false);
            emptyLabel = go.AddComponent<Text>();
            emptyLabel.text = "Searching for servers...";
            emptyLabel.alignment = TextAnchor.MiddleCenter;
            emptyLabel.color = new Color(0.85f, 0.85f, 0.85f);
            emptyLabel.fontSize = 14;
            try { emptyLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        }
    // Simple clear-and-rebuild approach
        ClearUIRows();
        if (_servers.Count == 0)
        {
            if (showTestRowIfEmpty)
            {
                var fake = new ServerResponse { serverId = long.MaxValue, uri = null };
                try { fake.EndPoint = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(testRowAddress), testRowPort); } catch { }
                GameObject go;
                if (hasPrefab) go = Instantiate(serverRowPrefab, contentRoot);
                else { go = new GameObject("ServerRow (Test)", typeof(RectTransform)); go.transform.SetParent(contentRoot, false); }
                var row = go.GetComponent<ServerRowUI>() ?? go.AddComponent<ServerRowUI>();
                row.Bind(fake, OnClickJoin);
                Debug.Log("ServerBrowserUI: Showing placeholder test row.");
            }
        }
        else
        {
            foreach (var kv in _servers)
            {
                var info = kv.Value;
                GameObject go;
                if (hasPrefab)
                {
                    go = Instantiate(serverRowPrefab, contentRoot);
                }
                else
                {
                    go = new GameObject("ServerRow (Runtime)", typeof(RectTransform));
                    go.transform.SetParent(contentRoot, false);
                }
                if (!go.activeSelf) go.SetActive(true);
                var host = go;
                var rowRT = go.GetComponent<RectTransform>();
                if (rowRT == null)
                {
                    // Wrap in a RectTransform container so layout works
                    var container = new GameObject("RowContainer", typeof(RectTransform));
                    var crt = container.GetComponent<RectTransform>();
                    container.transform.SetParent(contentRoot, false);
                    crt.anchorMin = new Vector2(0, 1);
                    crt.anchorMax = new Vector2(1, 1);
                    crt.pivot = new Vector2(0.5f, 1);
                    var le = container.AddComponent<LayoutElement>();
                    le.preferredHeight = 40f;
                    le.flexibleWidth = 1f;
                    go.transform.SetParent(container.transform, false);
                    host = container;
                }
                var row = host.GetComponent<ServerRowUI>() ?? host.GetComponentInChildren<ServerRowUI>(true);
                if (row == null)
                {
                    row = host.AddComponent<ServerRowUI>();
                    Debug.LogWarning("ServerBrowserUI: serverRowPrefab had no ServerRowUI; auto-added at runtime. Consider adding it to the prefab for clarity.");
                }
                // Stretch row to full width within content
                var hostRT = host.GetComponent<RectTransform>();
                if (hostRT != null)
                {
                    hostRT.anchorMin = new Vector2(0, 0.5f);
                    hostRT.anchorMax = new Vector2(1, 0.5f);
                    hostRT.pivot = new Vector2(0.5f, 0.5f);
                    var pad = vlg != null ? vlg.padding : new RectOffset(0, 0, 0, 0);
                    hostRT.offsetMin = new Vector2(pad.left, hostRT.offsetMin.y);
                    hostRT.offsetMax = new Vector2(-pad.right, hostRT.offsetMax.y);
                }
                row.Bind(info, OnClickJoin);
            }
        }

    // Force a layout rebuild so new/removed rows become visible immediately
        var rt = contentRoot as RectTransform;
        if (rt != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        SetEmptyVisible(_servers.Count == 0);
    // No suppression handling in simple rebuild mode
    }

    void Update()
    {
        if (pruneIntervalSeconds <= 0) return;
        if (Time.unscaledTime < _nextPruneTime) return;
        _nextPruneTime = Time.unscaledTime + pruneIntervalSeconds;
        if (PruneStaleServers())
        {
            RebuildList();
            SetEmptyVisible(_servers.Count == 0);
        }
    }

    bool PruneStaleServers()
    {
        if (serverTimeoutSeconds <= 0) return false;
        var now = Time.unscaledTime;
        bool removedAny = false;
        var toRemove = new List<long>();
        foreach (var kv in _lastSeen)
        {
            if (now - kv.Value > serverTimeoutSeconds)
                toRemove.Add(kv.Key);
        }
        foreach (var id in toRemove)
        {
            _lastSeen.Remove(id);
            if (_servers.Remove(id)) removedAny = true;
        }
        return removedAny;
    }

    void TryQuerySteamLobbies()
    {
        if (!includeSteamLobbies) return;
        var active = Transport.active;
        bool steamTransport = active != null && active.GetType().Name.IndexOf("Fizzy", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (forceSteamOnly && !steamTransport)
        {
            Debug.LogError("ServerBrowserUI: Steam-only browsing requires FizzySteamworks active. No lobbies can be shown.");
            return;
        }
        if (!steamTransport) return;
#if !DISABLESTEAMWORKS
        // Ensure steam
        try
        {
            if (!Steamworks.SteamAPI.Init()) return;
            if (!_steamCallbacksInstalled)
            {
                Application.onBeforeRender += PumpSteamCallbacks;
                _steamCallbacksInstalled = true;
            }

            // Request lobby list filtered by our handshake so we only see this game’s lobbies
            var hs = ComputeHandshake().ToString();
            Steamworks.SteamMatchmaking.AddRequestLobbyListStringFilter("handshake", hs, Steamworks.ELobbyComparison.k_ELobbyComparisonEqual);
            // Increase result count and accept any distance
            Steamworks.SteamMatchmaking.AddRequestLobbyListResultCountFilter(200);
            Steamworks.SteamMatchmaking.AddRequestLobbyListDistanceFilter(Steamworks.ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);
            Steamworks.SteamMatchmaking.AddRequestLobbyListFilterSlotsAvailable(1);
            Steamworks.SteamMatchmaking.RequestLobbyList();

            // Persist the callback instance so GC doesn't collect it
            _cbLobbyList = Steamworks.Callback<Steamworks.LobbyMatchList_t>.Create((cb) =>
            {
                _steamLobbyIds.Clear();
                for (int i = 0; i < cb.m_nLobbiesMatching; i++)
                {
                    var id = Steamworks.SteamMatchmaking.GetLobbyByIndex(i);
                    _steamLobbyIds.Add(id);
                }
                MapSteamLobbiesToServers();
                // Prune any steam entries that are no longer returned by Steam
                try
                {
                    var currentSteamIds = new HashSet<long>(_steamLobbyIds.Select(x => (long)x.m_SteamID));
                    var toRemove = new List<long>();
                    foreach (var kv in _servers)
                    {
                        // Steam entries use serverId=lobbyID and have a steam:// uri
                        if (kv.Value.uri != null && kv.Value.uri.Scheme == "steam")
                        {
                            if (!currentSteamIds.Contains(kv.Key)) toRemove.Add(kv.Key);
                        }
                    }
                    foreach (var id2 in toRemove)
                    {
                        _servers.Remove(id2);
                        _lastSeen.Remove(id2);
                    }
                    if (toRemove.Count > 0) RebuildList();
                }
                catch { }
            });
        }
        catch { }
#endif
    }

#if !DISABLESTEAMWORKS
    void PumpSteamCallbacks()
    {
        try { Steamworks.SteamAPI.RunCallbacks(); } catch { }
    }

    void MapSteamLobbiesToServers()
    {
        foreach (var id in _steamLobbyIds)
        {
            try
            {
                string hostId = Steamworks.SteamMatchmaking.GetLobbyData(id, "HostAddress");
                if (string.IsNullOrEmpty(hostId)) continue;
                // Pack a steam URI; ServerResponse.uri is used by our join handler
                var uri = new System.Uri($"steam://{hostId}");
                long sid = (long)id.m_SteamID;
                var info = new ServerResponse { serverId = sid, uri = uri };
                _servers[sid] = info;
                _lastSeen[sid] = Time.unscaledTime;
            }
            catch { }
        }
        RebuildList();
    }
#endif

    void RefreshButtonClicked()
    {
        Refresh();
    }

    void TrySendToAddress(NetworkDiscovery d, IPAddress ip)
    {
        if (d == null || ip == null) return;
        string prev = d.BroadcastAddress;
        try
        {
            d.BroadcastAddress = ip.ToString();
            d.BroadcastDiscoveryRequest();
            Debug.Log($"ServerBrowserUI: Extra discovery ping to {d.BroadcastAddress}:{GetDiscoveryPortForLog(d)}");
        }
        finally
        {
            d.BroadcastAddress = prev;
        }
    }

    int GetDiscoveryPortForLog(NetworkDiscovery d)
    {
        // Reflection to read protected field for logging only; safe if it fails.
        try
        {
            var f = typeof(NetworkDiscoveryBase<,>).MakeGenericType(typeof(ServerRequest), typeof(ServerResponse))
                                                  .GetField("serverBroadcastListenPort", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (f != null)
            {
                object val = f.GetValue(d);
                if (val is int p) return p;
            }
        }
        catch { }
        return 47777;
    }

    IEnumerable<IPAddress> GetLocalBroadcastAddresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = nic.GetIPProperties();
                foreach (var ua in ipProps.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue; // IPv4 only
                    var addrBytes = ua.Address.GetAddressBytes();
                    if (ua.IPv4Mask == null) continue;
                    var maskBytes = ua.IPv4Mask.GetAddressBytes();
                    if (addrBytes.Length != 4 || maskBytes.Length != 4) continue;
                    var bcast = new byte[4];
                    for (int i = 0; i < 4; i++) bcast[i] = (byte)(addrBytes[i] | (~maskBytes[i]));
                    // Skip 255.255.255.255 because default broadcast already covered it
                    if (bcast[0] == 255 && bcast[1] == 255 && bcast[2] == 255 && bcast[3] == 255) continue;
                    try { list.Add(new IPAddress(bcast)); } catch { }
                }
            }
        }
        catch { }
        return list;
    }

    // Try to find the user's ServerList object. Returns true on success.
    bool EnsureUIRoots()
    {
        if (contentRoot != null) return true;
        // Prefer a child named serverListName under this object
        var t = transform.Find(serverListName);
        if (t != null) { contentRoot = t; return true; }
        // Otherwise search scene (prefer active)
    var all = FindAll<Transform>(true);
        Transform candidate = null;
        foreach (var tr in all)
        {
            if (tr.name == serverListName)
            {
                if (tr.gameObject.activeInHierarchy) { candidate = tr; break; }
                if (candidate == null) candidate = tr;
            }
        }
    if (candidate != null) { contentRoot = candidate; return true; }
    // As a last resort, create a runtime container under this object so rows can render
    var go = new GameObject(serverListName, typeof(RectTransform));
    go.transform.SetParent(transform, false);
    contentRoot = go.transform;
    var vlg = go.AddComponent<VerticalLayoutGroup>();
    vlg.childForceExpandWidth = true; vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.spacing = 4f;
    (go.transform as RectTransform).anchorMin = new Vector2(0, 0); (go.transform as RectTransform).anchorMax = new Vector2(1, 1);
    (go.transform as RectTransform).offsetMin = Vector2.zero; (go.transform as RectTransform).offsetMax = Vector2.zero;
    Debug.LogWarning($"ServerBrowserUI: Created runtime '{serverListName}' container because none was found.");
    return true;
    }

    [ContextMenu("Force Show Test Row")]
    void ForceShowTestRow()
    {
    // Clear discovered servers and force showing the placeholder row once
    _servers.Clear();
    RebuildList();
    SetEmptyVisible(false);
    Debug.Log("ServerBrowserUI: Forced showing a test row.");
    }

    void ClearUIRows()
    {
        if (contentRoot == null)
        {
            Debug.LogWarning("ServerBrowserUI: ClearUIRows skipped; contentRoot not assigned.");
            return;
        }
        for (int i = contentRoot.childCount - 1; i >= 0; --i)
        {
            var child = contentRoot.GetChild(i);
            if (child.GetComponent<ServerRowUI>() != null || child.name.Contains("ServerRow"))
            {
                Destroy(child.gameObject);
            }
        }
    }

    void SetEmptyVisible(bool visible)
    {
        if (emptyLabel != null) emptyLabel.gameObject.SetActive(visible);
    }

    // Test row is managed directly in RebuildList without touching _servers

    void OnClickJoin(ServerResponse info)
    {
        // Use the URI provided by discovery.
    var nm = NetworkManager.singleton ?? FindAny<NetworkManager>(true);
        if (nm == null)
        {
            Debug.LogError("ServerBrowserUI: No NetworkManager in scene.");
            return;
        }

        // Set address from URI
        if (info.uri != null)
        {
            // If this is a Steam URI, ensure FizzySteamworks is the active transport
            if (string.Equals(info.uri.Scheme, "steam", System.StringComparison.OrdinalIgnoreCase))
            {
                var fizzy = EnsureFizzyTransport(nm.gameObject);
                if (fizzy == null)
                {
                    Debug.LogError("ServerBrowserUI: Steam lobby selected but FizzySteamworks transport not found.");
                    return;
                }
                if (Transport.active != fizzy) Transport.active = fizzy;
                if (nm.transport != fizzy) nm.transport = fizzy;
            }
            nm.StartClient(info.uri);
            Debug.Log($"ServerBrowserUI: Joining {info.uri} (serverId={info.serverId})");
        }
        else
        {
            // Fallback: use endpoint address and current transport port
            string host = info.EndPoint != null ? info.EndPoint.Address.ToString() : "127.0.0.1";
            nm.networkAddress = host;
            nm.StartClient();
            Debug.Log($"ServerBrowserUI: Joining {host} (serverId={info.serverId})");
        }
    }

    // Try to find or add FizzySteamworks transport
    Mirror.Transport EnsureFizzyTransport(GameObject host)
    {
        var all = FindAll<Mirror.Transport>(true);
        var existing = all.FirstOrDefault(t => t != null && t.GetType().Name.IndexOf("Fizzy", System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (existing != null)
        {
            if (!existing.enabled) existing.enabled = true;
            return existing;
        }
        // Try to resolve by common names
        System.Type type = FindTypeByHints(new[]
        {
            "Mirror.FizzySteam.FizzySteamworks",
            "FizzySteamworks.FizzySteamworks",
            "Mirror.FizzySteamworks.FizzySteamworks",
            "FizzySteamworks"
        });
        if (type == null) return null;
        try
        {
            var comp = host.AddComponent(type) as Mirror.Transport;
            if (comp != null) comp.enabled = true;
            return comp;
        }
        catch { return null; }
    }

    System.Type FindTypeByHints(string[] typeNames)
    {
        foreach (var name in typeNames)
        {
            var t = System.Type.GetType(name, throwOnError: false);
            if (t != null) return t;
        }
        try
        {
            var asms = System.AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in asms)
            {
                foreach (var tn in typeNames)
                {
                    var shortName = tn.Split('.').Last();
                    var t = asm.GetTypes().FirstOrDefault(x => x.Name == shortName);
                    if (t != null) return t;
                }
            }
        }
        catch { }
        return null;
    }

    // ScrollView-based debug spawner removed per request to use the existing ServerList object.

    static string GetHierarchyPath(Transform t)
    {
        if (t == null) return "(null)";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        while (t != null)
        {
            sb.Insert(0, t.name);
            t = t.parent;
            if (t != null) sb.Insert(0, "/");
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    void EditorEnsureRowPrefab()
    {
        // First, try to find an existing prefab named ServerRowPrefab
        var guids = UnityEditor.AssetDatabase.FindAssets("ServerRowPrefab t:Prefab");
        if (guids != null && guids.Length > 0)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                serverRowPrefab = prefab;
                UnityEditor.EditorUtility.SetDirty(this);
                Debug.Log($"ServerBrowserUI: Found existing row prefab at {path}");
                return;
            }
        }

        // Create a default row prefab under Assets/Prefabs
        string folder = "Assets/Prefabs";
        if (!UnityEditor.AssetDatabase.IsValidFolder(folder))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Prefabs");

        var temp = BuildDefaultRowGO();
        string assetPath = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(folder + "/ServerRowPrefab.prefab");
        var created = UnityEditor.PrefabUtility.SaveAsPrefabAsset(temp, assetPath);
        GameObject.DestroyImmediate(temp);
        if (created != null)
        {
            serverRowPrefab = created;
            UnityEditor.EditorUtility.SetDirty(this);
            Debug.Log($"ServerBrowserUI: Auto-created default row prefab at {assetPath}");
        }
        else
        {
            Debug.LogWarning("ServerBrowserUI: Failed to create default row prefab.");
        }
    }

    GameObject BuildDefaultRowGO()
    {
        var go = new GameObject("ServerRowPrefab", typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(400, 40);
        var img = go.AddComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.25f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 40f; le.flexibleWidth = 1f;

        // Row layout
        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f; hlg.childControlWidth = true; hlg.childControlHeight = true; hlg.childForceExpandWidth = true; hlg.childForceExpandHeight = false; hlg.childAlignment = TextAnchor.MiddleLeft;

        // Left container
        var left = new GameObject("Left", typeof(RectTransform));
        left.transform.SetParent(go.transform, false);
        var leftLE = left.AddComponent<LayoutElement>(); leftLE.flexibleWidth = 1f; leftLE.minWidth = 100f;
        var vlg = left.AddComponent<VerticalLayoutGroup>(); vlg.spacing = 2f; vlg.childControlWidth = true; vlg.childControlHeight = true; vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;

        // Name label
        var nameGO = new GameObject("NameLabel", typeof(RectTransform));
        nameGO.transform.SetParent(left.transform, false);
        var nameText = nameGO.AddComponent<Text>();
        nameText.text = "Server";
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.color = Color.white;
        nameText.fontSize = 14;
        try { nameText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

        // Address label
        var addrGO = new GameObject("AddressLabel", typeof(RectTransform));
        addrGO.transform.SetParent(left.transform, false);
        var addrText = addrGO.AddComponent<Text>();
        addrText.text = "127.0.0.1:7777";
        addrText.alignment = TextAnchor.MiddleLeft;
        addrText.color = new Color(0.85f, 0.85f, 0.85f);
        addrText.fontSize = 12;
        try { addrText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }

        // Right container
        var right = new GameObject("Right", typeof(RectTransform));
        right.transform.SetParent(go.transform, false);
        var rightLE = right.AddComponent<LayoutElement>(); rightLE.preferredWidth = 110f; rightLE.minWidth = 90f; rightLE.flexibleWidth = 0f;

        // Join button
        var btnGO = new GameObject("JoinButton", typeof(RectTransform));
        btnGO.transform.SetParent(right.transform, false);
        var btnImg = btnGO.AddComponent<Image>(); btnImg.color = new Color(0.2f, 0.6f, 1f, 0.8f);
        var button = btnGO.AddComponent<Button>();
        var btnTextGO = new GameObject("Text", typeof(RectTransform));
        btnTextGO.transform.SetParent(btnGO.transform, false);
        var btnText = btnTextGO.AddComponent<Text>();
        btnText.text = "Join";
        btnText.alignment = TextAnchor.MiddleCenter;
        btnText.color = Color.white;
        btnText.fontSize = 14;
        try { btnText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        var btnRT = btnGO.GetComponent<RectTransform>(); btnRT.sizeDelta = new Vector2(90, 28);
        var btnTextRT = btnTextGO.GetComponent<RectTransform>(); btnTextRT.anchorMin = Vector2.zero; btnTextRT.anchorMax = Vector2.one; btnTextRT.offsetMin = btnTextRT.offsetMax = Vector2.zero;

        // Add ServerRowUI and wire references
        var row = go.AddComponent<ServerRowUI>();
        row.nameLabel = nameText;
        row.addressLabel = addrText;
        row.joinButton = button;

        return go;
    }
#endif
}
