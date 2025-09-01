# MultiplayerTemplate

A Unity **Mirror** networking starter project supporting both **LAN** and **Steam (FizzySteamworks)** connectivity with a minimal main menu, server browser scaffolding, and example in‑game player/controller setup. Targets Unity `6000.0.23f1` (2025 LTS tech stream) but should migrate forward with minimal changes.

## Key Features
- **Dual Mode Networking**: Toggle between LAN (Telepathy) and Steam (FizzySteamworks) from the main menu.
- **Automatic Transport Selection**: `MainMenuNetworkUI` picks FizzySteamworks when Steam is available; otherwise falls back.
- **Steam Lobby Integration** (via Steamworks.NET helper `SteamLobbyUtil`) with optional auto invite overlay.
- **Simple Host / Join UI**: Auto-finds buttons & input fields by common names; optional address / port hiding for Steam.
- **Scene Flow**: Offline main menu -> online game scene (`Game`). Loading screen detaches automatically when hosting.
- **Player Prefab Driven**: Uses standard Mirror `NetworkManager.playerPrefab` workflow (you must assign your prefab).
- **Transports Included**: Telepathy, KCP (kcp2k), SimpleWeb, FizzySteamworks, Edgegap relay/lobby, Encryption + Threaded variants.
- **LAN Advertising (Optional)**: Can advertise over Mirror Network Discovery when not in Steam-only mode.
- **Steam-Only Hard Mode**: Configuration flag blocks LAN & ensures a Steam transport is present.
- **Utility Scripts**: Auto setup helpers, player controller example, crate spawner, loading UX helper.

## Project Structure (High Level)
```
Assets/
  Scenes/            MainMenu.unity, Game.unity
  Scripts/
    UI/              MainMenuNetworkUI, ServerBrowser UI, InGame menu
    Networking/      ServerKinematicProxy, authority helpers
    Net/             Discovery & Network root persistence
    Steam/           Steam lobby + overlay invite logic
    World/           Example crate & cube network behaviours
    Player/          Character controller + interaction
  Mirror/            Mirror core & transports (subfolder copies)
  Settings/          URP + physics + volume profiles
  Prefabs/           Player, UI pieces, loading, server row
```

## Requirements
| Component | Version / Notes |
|-----------|-----------------|
| Unity     | 6000.0.23f1 (adjust in `ProjectSettings/ProjectVersion.txt`) |
| Mirror    | Included (local copy of runtime & transports) |
| Steam     | Optional. Needs Steam client running + valid `steam_appid.txt` for local tests |

> If you upgrade Mirror via UPM, delete the duplicated local folders to avoid conflicts.

## Getting Started
1. Clone the repo:
   ```bash
   git clone https://github.com/MattRenda/MultiplayerTemplate.git
   ```
2. Open the project in Unity 6000.0.23f1 (or newer 6000.x). Let it import.
3. Open `Assets/Scenes/MainMenu.unity`.
4. Select your `NetworkManager` object (create one if none) and assign your **Player Prefab**.
5. (Optional Steam) Place a `steam_appid.txt` with your App ID in the root (local test uses 480 or your own). Ensure Steam client is running.
6. Press Play. Use Host / Join. Toggle Steam/LAN if the controls are present.

## Hosting
- Press **Host**. The script:
  - Chooses a transport (Steam vs LAN).
  - Sets offline scene to current (if configured) and switches to the `Game` scene.
  - Optionally creates/updates a Steam lobby & opens invite overlay.
  - Can advertise on LAN (if enabled).

## Joining
- Enter an IP (LAN) or leave blank for Steam lobby/invite resolution.
- Port defaults to 7777 for Telepathy unless changed.
- For Steam: a numeric SteamID64 in the address field (optional) attempts a direct connect; invites/lobbies work with blank.

## Steam Integration Notes
- Provided helper `SteamLobbyUtil` (reflection-accessed) is used only if Steamworks.NET is initialized (`SteamManager`).
- `forceSteamOnly` blocks LAN & ensures Fizzy transport must exist.
- Add your own lobby metadata, capacity, or matchmaking logic inside the Steam scripts.

## Adding / Switching Transports Manually
Attach desired transport component to the `NetworkManager` GameObject and disable others. The `MainMenuNetworkUI` attempts to select based on:
1. FizzySteamworks (Steam) if Steam mode.
2. Telepathy fallback (if allowed).

## Network Discovery (LAN)
Enable `advertiseServerOnHost` and ensure a `NetworkDiscovery` component is present (script can add it automatically). Clients can implement browsing logic via provided Server Browser UI scripts.

## Customization Tips
- Replace placeholder `Assets/Scripts/Networking/.delete_me` once you add your own networking scripts.
- Update art / UI by swapping prefabs in `Prefabs/`.
- Extend `MainMenuNetworkUI` for authentication, region selection, NAT traversal, etc.

## Building
1. Add `MainMenu` and `Game` scenes to Build Settings in that order.
2. (Steam) Include correct App ID at runtime; distribute required steam_api DLLs (already handled by Steamworks.NET plugin).
3. Build via File > Build (or create a CI workflow—see below suggestion).

## Suggested Next Steps (Not yet implemented)
- GitHub Actions CI for automated Windows build.
- Automated Lobby list & refresh UI.
- In-game chat and voice placeholder hooks.
- ScriptableObject config for transport preferences.
- Tests for utility components.

## Contributing
PRs and issues welcome. Keep runtime scripts clean and avoid hard dependencies—prefer reflection wrappers like current Steam usage where optional.

## License
Add a LICENSE file (MIT, Apache-2.0, etc.) to clarify reuse. If Mirror / Steamworks.NET licensing changes, ensure compatibility.

---
Happy hacking—launch, invite, iterate!
