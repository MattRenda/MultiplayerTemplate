using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
using TMPro;
#endif

/// <summary>
/// Wires the Main Menu Host/Join actions to Mirror.
/// Defaults to Telepathy, but can auto-swap to FizzySteamworks if present (no hard dependency).
/// Attach this to any object in the MainMenu scene.
/// - Host: starts a host (server+client)
/// - Join: connects a client to the given address/port
/// If controls aren't assigned, it tries to auto-find by common names.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuNetworkUI : MonoBehaviour
{
	public enum NetworkMode
	{
		LAN,
		Steam
	}
	[Header("UI")]
	public Button hostButton; // optional; auto-found by name if null
	[Tooltip("Optional root GameObject of the Main Menu UI to hide/destroy when hosting.")]
	public GameObject menuRoot;
	[Tooltip("If true, destroys the menuRoot upon hosting. Otherwise just disables it.")]
	public bool destroyMenuOnHost = true;

	[Header("Join (Direct)")]
	[Tooltip("Optional: Join button to connect as client.")]
	public Button joinButton; // optional; auto-found by name if null
	[Tooltip("If true, this script will auto-wire the Join button to call StartClient via OnClickJoin. Leave OFF if your Join button navigates to a Servers menu.")]
	public bool autoWireJoinButton = false;
	[Tooltip("Optional: Address input (UI InputField). If not set, tries to find by name 'Address' or 'IP'.")]
	public InputField addressInput; // optional
	[Tooltip("Optional: Port input (UI InputField). If not set, tries to find by name 'Port'.")]
	public InputField portInput; // optional
#if TMP_PRESENT || UNITY_TEXTMESHPRO
	[Tooltip("Optional: Address input (TMP InputField)")]
	public TMP_InputField tmpAddressInput; // optional
	[Tooltip("Optional: Port input (TMP InputField)")]
	public TMP_InputField tmpPortInput; // optional
#endif
	[Tooltip("If true, tries to auto-locate Join controls by common names.")]
	public bool autoFindJoinControls = true;

	[Header("Mode Toggle (optional)")]
	[Tooltip("Optional Toggle to switch between LAN and Steam. ON=Steam, OFF=LAN.")]
	public Toggle steamModeToggle; // optional; auto-found
	[Tooltip("Optional Dropdown to choose mode: 0=LAN, 1=Steam.")]
	public Dropdown modeDropdown; // optional; auto-found
#if TMP_PRESENT || UNITY_TEXTMESHPRO
	[Tooltip("Optional TMP Dropdown to choose mode: 0=LAN, 1=Steam.")]
	public TMP_Dropdown tmpModeDropdown; // optional; auto-found
#endif
	[Tooltip("Auto-find a Toggle named 'Steam', 'UseSteam', or a Dropdown named 'Mode'/'NetworkMode' in the scene.")]
	public bool autoFindModeControls = true;
	[Tooltip("Hide the Port field UI when Steam mode is selected.")]
	public bool hidePortFieldInSteamMode = true;

	[Tooltip("Default mode if no UI control is assigned.")]
	public NetworkMode defaultMode = NetworkMode.LAN;

	[Header("Scenes (optional)")]
	[Tooltip("Online scene to switch to when hosting starts. Leave empty to keep current scene.")]
	public string onlineSceneName = "Game";
	[Tooltip("Set offlineScene to current scene before hosting, so StopHost/StopClient return here.")]
	public bool setOfflineToCurrent = true;

	[Header("Auto Setup")]
	[Tooltip("Create a NetworkManager if one isn't found.")]
	public bool autoCreateNetworkManager = false;
	[Tooltip("Ensure TelepathyTransport exists and is active when no other preferred transport is available.")]
	public bool ensureTelepathyTransport = true;

	[Header("Transports")]
	[Tooltip("If true, tries to use FizzySteamworks (Steam P2P) transport when available. If not found, falls back to Telepathy.")]
	public bool preferFizzySteamworks = true;
	[Tooltip("If true, automatically switch to Steam mode when Steam API is available at startup.")]
	public bool autoSelectSteamIfAvailable = true;
	[Tooltip("Optional fallback preference order when Auto-selecting a transport. If empty, uses any existing transport or Telepathy.")]
	public string[] autoTransportTypeNameHints = new[]
	{
		// Most common type names/namespaces across versions. We resolve them via reflection at runtime.
		"FizzySteamworks", // generic
		"Mirror.FizzySteam.FizzySteamworks", // actual current namespace
		"FizzySteamworks.FizzySteamworks", // older variants
		"Mirror.FizzySteamworks.FizzySteamworks", // older variants
		"kcp2k.KcpTransport",
		"TelepathyTransport",
		"Mirror.SimpleWeb.SimpleWebTransport"
	};
	[Tooltip("Advertise the server on LAN when hosting (requires NetworkDiscovery).")]
	public bool advertiseServerOnHost = false;

	[Header("Steam Only")]
	[Tooltip("Force Steam-only networking. Disables LAN, removes Telepathy fallback, and requires FizzySteamworks.")]
	public bool forceSteamOnly = true;

	[Header("Steam Lobby")]
	[Tooltip("Create lobby as Friends-only (ON) or Public (OFF). Public is recommended for broad visibility during tests.")]
	public bool steamLobbyFriendsOnly = false;
	[Tooltip("Automatically open the Steam 'Invite Friends' overlay when hosting starts (Steam mode only).")]
	public bool autoOpenSteamInviteOnHost = false;

	void Awake()
	{
		// Force Steam mode if requested; otherwise, auto-select Steam when available
		if (autoSelectSteamIfAvailable)
		{
			try
			{
				var t = typeof(SteamLobbyUtil);
				var m = t.GetMethod("IsAvailable", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
				bool steamReady = false;
				if (m != null) steamReady = (bool)m.Invoke(null, null);
				if (steamReady || forceSteamOnly)
				{
					preferFizzySteamworks = true;
					defaultMode = NetworkMode.Steam;
				}
			}
			catch { }
		}
		// Try auto-find a button named "Host" if not assigned
		if (hostButton == null)
		{
			var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			hostButton = buttons.FirstOrDefault(b => b.name.Equals("Host"));
		}

		// Try auto-detect a likely menu root if not assigned
		if (menuRoot == null)
		{
			// Prefer a Canvas at the scene root
			var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			menuRoot = canvases.FirstOrDefault(c => c.isRootCanvas)?.gameObject;
		}

		if (hostButton != null)
		{
			hostButton.onClick.RemoveListener(OnClickHost);
			hostButton.onClick.AddListener(OnClickHost);
		}
		else
		{
			Debug.LogWarning("MainMenuNetworkUI: No Host Button assigned or found by name 'Host'. Assign one in the inspector.");
		}

		// Auto-locate Join controls if desired
		if (autoFindJoinControls)
		{
			if (joinButton == null)
			{
				var buttons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				joinButton = buttons.FirstOrDefault(b => b.name.Equals("Join"));
			}

			if (addressInput == null)
			{
				var inputs = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				addressInput = inputs.FirstOrDefault(i => i.name.Equals("Address") || i.name.Equals("IP"));
			}
			if (portInput == null)
			{
				var inputs = UnityEngine.Object.FindObjectsByType<InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				portInput = inputs.FirstOrDefault(i => i.name.Equals("Port"));
			}
#if TMP_PRESENT || UNITY_TEXTMESHPRO
			if (tmpAddressInput == null)
			{
				var tmps = UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				tmpAddressInput = tmps.FirstOrDefault(i => i.name.Equals("Address") || i.name.Equals("IP"));
			}
			if (tmpPortInput == null)
			{
				var tmps = UnityEngine.Object.FindObjectsByType<TMP_InputField>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				tmpPortInput = tmps.FirstOrDefault(i => i.name.Equals("Port"));
			}
#endif
		}

		// Auto-locate Mode controls if desired
		if (autoFindModeControls)
		{
			if (steamModeToggle == null)
			{
				var toggles = UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				steamModeToggle = toggles.FirstOrDefault(t => t.name.Equals("Steam") || t.name.Equals("UseSteam") || t.name.Equals("SteamMode"));
			}
			if (modeDropdown == null)
			{
				var drops = UnityEngine.Object.FindObjectsByType<Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				modeDropdown = drops.FirstOrDefault(d => d.name.Equals("Mode") || d.name.Equals("NetworkMode"));
			}
#if TMP_PRESENT || UNITY_TEXTMESHPRO
			if (tmpModeDropdown == null)
			{
				var tdrops = UnityEngine.Object.FindObjectsByType<TMP_Dropdown>(FindObjectsInactive.Include, FindObjectsSortMode.None);
				tmpModeDropdown = tdrops.FirstOrDefault(d => d.name.Equals("Mode") || d.name.Equals("NetworkMode"));
			}
#endif
		}

		// Wire up mode control listeners
		if (steamModeToggle != null)
		{
			steamModeToggle.onValueChanged.RemoveListener(OnSteamToggleChanged);
			steamModeToggle.onValueChanged.AddListener(OnSteamToggleChanged);
		}
		if (modeDropdown != null)
		{
			modeDropdown.onValueChanged.RemoveListener(OnModeDropdownChanged);
			modeDropdown.onValueChanged.AddListener(OnModeDropdownChanged);
			EnsureModeDropdownOptions(modeDropdown);
		}
#if TMP_PRESENT || UNITY_TEXTMESHPRO
		if (tmpModeDropdown != null)
		{
			tmpModeDropdown.onValueChanged.RemoveListener(OnTmpModeDropdownChanged);
			tmpModeDropdown.onValueChanged.AddListener(OnTmpModeDropdownChanged);
			EnsureModeDropdownOptions(tmpModeDropdown);
		}
#endif

		UpdateUiForMode();

		if (autoWireJoinButton && joinButton != null)
		{
			joinButton.onClick.RemoveListener(OnClickJoin);
			joinButton.onClick.AddListener(OnClickJoin);
		}
	}

	void OnDestroy()
	{
		if (hostButton != null)
			hostButton.onClick.RemoveListener(OnClickHost);
		if (autoWireJoinButton && joinButton != null)
			joinButton.onClick.RemoveListener(OnClickJoin);
		if (steamModeToggle != null) steamModeToggle.onValueChanged.RemoveListener(OnSteamToggleChanged);
		if (modeDropdown != null) modeDropdown.onValueChanged.RemoveListener(OnModeDropdownChanged);
#if TMP_PRESENT || UNITY_TEXTMESHPRO
		if (tmpModeDropdown != null) tmpModeDropdown.onValueChanged.RemoveListener(OnTmpModeDropdownChanged);
#endif
	}

	public void OnClickHost()
	{
	var nm = EnsureNetworkManager();
		if (nm == null)
		{
			Debug.LogError("MainMenuNetworkUI: No NetworkManager found in scene. Please add one and assign your Player Prefab.");
			return;
		}

		// Ensure transport matches selected mode right before hosting
		EnsurePreferredTransport(nm);

		// Require player prefab to be assigned so users control which prefab is used
		if (nm.playerPrefab == null)
		{
			Debug.LogError("MainMenuNetworkUI: NetworkManager.playerPrefab is not set. Assign your Player Prefab in the inspector.");
			return;
		}

		// Scenes
		if (setOfflineToCurrent)
		{
			var active = SceneManager.GetActiveScene();
			if (active.IsValid()) nm.offlineScene = active.name;
		}
		if (!string.IsNullOrWhiteSpace(onlineSceneName) && SceneExistsInBuild(onlineSceneName))
		{
			nm.onlineScene = onlineSceneName;
		}
		else if (!string.IsNullOrWhiteSpace(onlineSceneName))
		{
			Debug.LogWarning($"MainMenuNetworkUI: Online scene '{onlineSceneName}' not found in Build Settings. Host will stay in current scene. Add it to File > Build Settings.");
		}

		// If NetworkManager has an onlineScene set via inspector, validate it exists
		if (!string.IsNullOrWhiteSpace(nm.onlineScene) && !SceneExistsInBuild(nm.onlineScene))
		{
			Debug.LogError($"MainMenuNetworkUI: NetworkManager.onlineScene '{nm.onlineScene}' is not in Build Settings. Add it to File > Build Settings to allow scene switching.");
			return;
		}

		// Make sure Transport.active is set before starting host
		if (Transport.active == null && nm.transport != null)
		{
			Transport.active = nm.transport;
		}

		if (Transport.active == null)
		{
			Debug.LogError("MainMenuNetworkUI: No active Transport set. EnsureTelepathyTransport must be enabled or assign NetworkManager.transport.");
			return;
		}

		if (NetworkServer.active || NetworkClient.isConnected)
		{
			Debug.Log("MainMenuNetworkUI: Already hosting/connected.");
			return;
		}

		Application.runInBackground = true;

		// Ensure LoadingScreen is not parented under menuRoot so it can persist and hide itself
		DetachLoadingScreenFromMenu();

		// Hide or destroy the menu before starting host (useful if menu is DontDestroyOnLoad)
		if (menuRoot != null)
		{
			if (destroyMenuOnHost)
			{
				Destroy(menuRoot);
			}
			else
			{
				menuRoot.SetActive(false);
			}
		}

		// Stop any client discovery to avoid UDP port conflicts before we become the advertising server
		StopAnyDiscovery();

		nm.StartHost();
		Debug.Log($"MainMenuNetworkUI: StartHost called using transport '{Transport.active?.GetType().Name ?? nm.transport?.GetType().Name ?? "<none>"}'.");

		// If in Steam mode, create/update a Steam lobby so ServerBrowser can show it without manual input
		try
		{
			bool steam = CurrentModeIsSteam();
			if (steam)
			{
				long hs = 0;
				try { hs = ComputeHandshakeForDiscovery(); } catch { }
				TryCreateSteamLobby(hs, Application.productName);
				if (autoOpenSteamInviteOnHost)
				{
					try { var t = typeof(SteamLobbyUtil); var m = t.GetMethod("OpenInviteDialog", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static); m?.Invoke(null, null); } catch { }
				}
				// register cleanup on stop once (via reflection to avoid hard dependency)
				try
				{
					var nmEventsGo = nm.gameObject;
					var tCleanup = System.AppDomain.CurrentDomain.GetAssemblies()
						.SelectMany(a => { try { return a.GetTypes(); } catch { return System.Array.Empty<System.Type>(); } })
						.FirstOrDefault(x => x.Name == "HostStopCleanup");
					if (tCleanup != null && nmEventsGo.GetComponent(tCleanup) == null)
					{
						nmEventsGo.AddComponent(tCleanup);
					}
				}
				catch { }
			}
		}
		catch { }
		// Skip LAN advertising when forcing steam-only
		if (!forceSteamOnly && advertiseServerOnHost)
		{
			try
			{
				var discovery = UnityEngine.Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>(FindObjectsInactive.Include)
							?? nm.gameObject.GetComponent<Mirror.Discovery.NetworkDiscovery>()
							?? nm.gameObject.AddComponent<Mirror.Discovery.NetworkDiscovery>();
				if (discovery != null)
				{
					if (discovery.transport == null) discovery.transport = Transport.active ?? nm.transport;
					// attempt to set a deterministic handshake
					try
					{
						unchecked
						{
							string s = Application.productName;
							long h = 1469598103934665603L; // FNV-1a 64-bit offset
							foreach (char c in s) { h ^= c; h *= 1099511628211L; }
							discovery.secretHandshake = h;
						}
					}
					catch { }
					discovery.AdvertiseServer();
					Debug.Log("MainMenuNetworkUI: Advertising server on LAN.");
				}
			}
			catch (System.Exception ex)
			{
				Debug.LogWarning($"MainMenuNetworkUI: AdvertiseServer failed: {ex.Message}");
			}
		}
	}

	long ComputeHandshakeForDiscovery()
	{
		unchecked
		{
			string s = Application.productName;
			long h = 1469598103934665603L; // FNV-1a 64-bit offset
			foreach (char c in s) { h ^= c; h *= 1099511628211L; }
			return h;
		}
	}

	void TryCreateSteamLobby(long handshake, string lobbyName)
	{
		try
		{
			var asms = System.AppDomain.CurrentDomain.GetAssemblies();
			var t = asms.SelectMany(a =>
			{
				try { return a.GetTypes(); } catch { return System.Array.Empty<System.Type>(); }
			}).FirstOrDefault(x => x.Name == "SteamLobbyUtil");
			if (t == null) return;
			var m = t.GetMethod("CreateOrUpdateLobby", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			if (m == null) return;
			m.Invoke(null, new object[] { handshake, lobbyName, 8, steamLobbyFriendsOnly });
		}
		catch { }
	}

	public void OnClickJoin()
	{
		var nm = EnsureNetworkManager();
		if (nm == null)
		{
			Debug.LogError("MainMenuNetworkUI: No NetworkManager found in scene. Please add one.");
			return;
		}

		// Ensure transport matches selected mode right before joining
		EnsurePreferredTransport(nm);

		// If forcing Steam-only and Fizzy wasn’t found, stop here
		if (forceSteamOnly && !(Transport.active?.GetType().Name.Contains("Fizzy") ?? false))
		{
			Debug.LogError("MainMenuNetworkUI: Steam-only mode requires FizzySteamworks transport. Aborting join.");
			return;
		}

		if (NetworkServer.active)
		{
			Debug.LogWarning("MainMenuNetworkUI: Already hosting. Stop host before joining as a client in this instance.");
			return;
		}
		if (NetworkClient.isConnected)
		{
			Debug.Log("MainMenuNetworkUI: Already connected as a client.");
			return;
		}

		// Determine address and port (defaults)
	bool steam = forceSteamOnly ? true : CurrentModeIsSteam();
		// Ensure Steam API is initialized before attempting to connect via Fizzy/Steam
		if (steam)
		{
			try { SteamLobbyUtil.IsAvailable(); } catch { }
		}
		string address = GetJoinAddressOrDefault(steam); // for Steam, this can be a SteamID64 or empty for lobby/invite
		ushort port = steam ? (ushort)0 : GetJoinPortOrDefault(7777);

		// Set address/port on manager & transport
		nm.networkAddress = steam ? (address?.Trim() ?? string.Empty) : (string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim());
	// Configure address/port for transports that need it. Steam/Fizzy uses SteamIDs instead of ports.
		var activeTransport = Transport.active ?? nm.transport;
		if (activeTransport == null)
		{
			Debug.LogError("MainMenuNetworkUI: No active transport. Ensure one is present or enable auto setup.");
			return;
		}

		// Telepathy port (if applicable)
		// No LAN port configuration when forcing Steam-only
		if (!steam)
		{
			var telepathy = activeTransport as TelepathyTransport;
			if (telepathy != null)
			{
				telepathy.port = port;
				if (Transport.active == null) Transport.active = telepathy;
			}
			else
			{
				var t = activeTransport.GetType();
				var portProp = t.GetProperty("Port") ?? t.GetProperty("port");
				if (portProp != null && portProp.CanWrite && portProp.PropertyType == typeof(ushort))
				{
					try { portProp.SetValue(activeTransport, port); } catch { }
				}
			}
		}

		Application.runInBackground = true;

		// Optional: keep menu visible while connecting. You can disable it if desired.
		LoadingScreen.Instance?.Show();
		// If Steam mode and the address looks like a raw SteamID, build a steam:// URI so Fizzy parses it correctly
		if (steam && !string.IsNullOrWhiteSpace(nm.networkAddress) && ulong.TryParse(nm.networkAddress, out _))
		{
			try { nm.StartClient(new System.Uri($"steam://{nm.networkAddress}")); }
			catch { nm.StartClient(); }
		}
		else
		{
			nm.StartClient();
		}
		var transportName = (Transport.active ?? nm.transport)?.GetType().Name ?? "<none>";
		Debug.Log($"MainMenuNetworkUI: StartClient called to {(steam ? (string.IsNullOrEmpty(nm.networkAddress) ? "<steam lobby/invite>" : nm.networkAddress) : nm.networkAddress)} using transport '{transportName}'.");
	}

	void DetachLoadingScreenFromMenu()
	{
		#if UNITY_2021_2_OR_NEWER
		var ls = UnityEngine.Object.FindFirstObjectByType<LoadingScreen>(FindObjectsInactive.Include);
		#else
		var ls = FindObjectOfType<LoadingScreen>();
		#endif
		if (ls == null || menuRoot == null) return;
		var lsGo = ls.gameObject;
		if (lsGo.transform.IsChildOf(menuRoot.transform))
		{
			lsGo.transform.SetParent(null, worldPositionStays: true);
			Debug.Log("MainMenuNetworkUI: Detected LoadingScreen under menuRoot. Detached it to allow proper scene transition hiding.");
		}
	}

	void StopAnyDiscovery()
	{
		#if UNITY_2021_2_OR_NEWER
		var discovery = UnityEngine.Object.FindFirstObjectByType<Mirror.Discovery.NetworkDiscovery>(FindObjectsInactive.Include);
		#else
		var discovery = GameObject.FindObjectOfType<Mirror.Discovery.NetworkDiscovery>();
		#endif
		try { discovery?.StopDiscovery(); }
		catch { }
	}

	NetworkManager EnsureNetworkManager()
	{
		var existing = NetworkManager.singleton ?? UnityEngine.Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
		if (existing == null && autoCreateNetworkManager)
		{
			// Create inactive so we can wire transport before Awake
			var go = new GameObject("NetworkManager");
			go.SetActive(false);
			Transport chosen = null;
			// Try to honor Steam preference first
			if (preferFizzySteamworks)
			{
				chosen = FindOrAddFizzyTransport(go);
			}
			// Fallback to any existing transport in the scene
			if (chosen == null)
			{
				chosen = UnityEngine.Object.FindFirstObjectByType<Transport>(FindObjectsInactive.Include);
			}
			// Fallback to Telepathy if requested
			if (chosen == null && ensureTelepathyTransport)
			{
				var tele = go.AddComponent<TelepathyTransport>();
				tele.enabled = true;
				chosen = tele;
			}
			var nm = go.AddComponent<NetworkManager>();
			if (chosen != null)
			{
				nm.transport = chosen;
				Transport.active = chosen;
			}
			go.SetActive(true);
			return nm;
		}

		// For existing manager, selection will be applied closer to action via EnsurePreferredTransport
		return existing;
	}

	void OnSteamToggleChanged(bool _)
	{
		UpdateUiForMode();
		ApplyModeToTransportImmediate();
	}

	void OnModeDropdownChanged(int value)
	{
		UpdateUiForMode();
		ApplyModeToTransportImmediate();
	}
#if TMP_PRESENT || UNITY_TEXTMESHPRO
	void OnTmpModeDropdownChanged(int value)
	{
		UpdateUiForMode();
		ApplyModeToTransportImmediate();
	}
#endif

	void EnsureModeDropdownOptions(Dropdown dd)
	{
		if (dd.options == null || dd.options.Count < 2)
		{
			dd.options.Clear();
			dd.options.Add(new Dropdown.OptionData("LAN"));
			dd.options.Add(new Dropdown.OptionData("Steam"));
		}
	}
#if TMP_PRESENT || UNITY_TEXTMESHPRO
	void EnsureModeDropdownOptions(TMP_Dropdown dd)
	{
		if (dd.options == null || dd.options.Count < 2)
		{
			dd.options.Clear();
			dd.options.Add(new TMP_Dropdown.OptionData("LAN"));
			dd.options.Add(new TMP_Dropdown.OptionData("Steam"));
		}
	}
#endif

	bool CurrentModeIsSteam()
	{
		if (steamModeToggle != null) return steamModeToggle.isOn;
		if (modeDropdown != null) return modeDropdown.value == 1;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
		if (tmpModeDropdown != null) return tmpModeDropdown.value == 1;
#endif
		// Fallback to previous flag / default
		if (preferFizzySteamworks) return true;
		return defaultMode == NetworkMode.Steam;
	}

	void UpdateUiForMode()
	{
		bool steam = true; // default to steam when forcing
		if (!forceSteamOnly)
			steam = CurrentModeIsSteam();
		// Hide/show port field(s) if desired
		if (hidePortFieldInSteamMode)
		{
			if (portInput != null) portInput.gameObject.SetActive(!steam);
#if TMP_PRESENT || UNITY_TEXTMESHPRO
			if (tmpPortInput != null) tmpPortInput.gameObject.SetActive(!steam);
#endif
		}

		// Disable mode toggles when forcing steam-only
		if (forceSteamOnly)
		{
			if (steamModeToggle != null) steamModeToggle.interactable = false;
			if (modeDropdown != null) modeDropdown.interactable = false;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
			if (tmpModeDropdown != null) tmpModeDropdown.interactable = false;
#endif
		}
	}

	void EnsurePreferredTransport(NetworkManager nm)
	{
		bool steam = forceSteamOnly ? true : CurrentModeIsSteam();
		Transport chosen = null;
		if (steam)
		{
			chosen = FindOrAddFizzyTransport(nm.gameObject);
			if (chosen == null)
			{
				Debug.LogError("MainMenuNetworkUI: Steam-only mode but FizzySteamworks transport not found. Please add the FizzySteamworks transport component/package.");
				return;
			}
		}
		// No Telepathy fallback when forcing steam-only
		if (nm.transport != chosen) nm.transport = chosen;
		Transport.active = chosen;
	}

	// Immediately apply current mode to the active transport when user toggles mode in UI.
	void ApplyModeToTransportImmediate()
	{
		// Avoid switching while running
		if (NetworkServer.active || NetworkClient.isConnected) return;
		var nm = NetworkManager.singleton ?? UnityEngine.Object.FindFirstObjectByType<NetworkManager>(FindObjectsInactive.Include);
		if (nm == null) return;
		var before = (Transport.active ?? nm.transport)?.GetType().Name;
		EnsurePreferredTransport(nm);
		var after = (Transport.active ?? nm.transport)?.GetType().Name;
		if (before != after)
		{
			Debug.Log($"MainMenuNetworkUI: Transport switched due to mode change: {before} -> {after}");
		}
	}

	// Try to find an existing FizzySteamworks transport, or add it via reflection if the type is present in the project.
	Transport FindOrAddFizzyTransport(GameObject host)
	{
		// Look for any Transport whose type name contains "Fizzy" (covers most package variants)
		var all = UnityEngine.Object.FindObjectsByType<Transport>(FindObjectsInactive.Include, FindObjectsSortMode.None);
		var fizzyExisting = all.FirstOrDefault(t => t != null && t.GetType().Name.IndexOf("Fizzy", System.StringComparison.OrdinalIgnoreCase) >= 0);
		if (fizzyExisting != null)
		{
			if (!fizzyExisting.enabled) fizzyExisting.enabled = true;
			return fizzyExisting;
		}

		// Try to resolve the type by common names/namespaces
		System.Type fizzyType = FindTypeByHints(new[]
		{
			"FizzySteamworks",
			"Mirror.FizzySteam.FizzySteamworks",
			"FizzySteamworks.FizzySteamworks",
			"Mirror.FizzySteamworks.FizzySteamworks",
		});
		if (fizzyType == null) return null; // package not installed

		try
		{
			var comp = host.AddComponent(fizzyType) as Transport;
			if (comp != null) comp.enabled = true;
			return comp;
		}
		catch
		{
			return null;
		}
	}

	System.Type FindTypeByHints(string[] typeNames)
	{
		foreach (var name in typeNames)
		{
			var t = System.Type.GetType(name, throwOnError: false);
			if (t != null) return t;
		}
		// Fallback: scan assemblies for anything that matches any provided short name
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

	static bool SceneExistsInBuild(string sceneName)
	{
		if (string.IsNullOrEmpty(sceneName)) return false;
		int count = SceneManager.sceneCountInBuildSettings;
		for (int i = 0; i < count; i++)
		{
			var path = SceneUtility.GetScenePathByBuildIndex(i);
			var name = System.IO.Path.GetFileNameWithoutExtension(path);
			if (name == sceneName) return true;
		}
		return false;
	}

	string GetJoinAddressOrDefault(bool steamMode = false)
	{
		// Try UI InputField first, then TMP if present.
		if (addressInput != null && !string.IsNullOrWhiteSpace(addressInput.text))
			return addressInput.text;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
		if (tmpAddressInput != null && !string.IsNullOrWhiteSpace(tmpAddressInput.text))
			return tmpAddressInput.text;
#endif
		return steamMode ? string.Empty : "127.0.0.1";
	}

	ushort GetJoinPortOrDefault(ushort fallback)
	{
		string raw = null;
		if (portInput != null && !string.IsNullOrWhiteSpace(portInput.text)) raw = portInput.text;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
		else if (tmpPortInput != null && !string.IsNullOrWhiteSpace(tmpPortInput.text)) raw = tmpPortInput.text;
#endif
		if (ushort.TryParse(raw, out var p) && p > 0) return p;
		return fallback;
	}
}

