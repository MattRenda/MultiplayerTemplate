using Mirror.Discovery;
using UnityEngine;
using UnityEngine.UI;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
using TMPro;
#endif

/// <summary>
/// UI row that displays a discovered server and exposes a Join button.
/// Designed to be added to your existing serverRow prefab.
/// </summary>
[DisallowMultipleComponent]
public class ServerRowUI : MonoBehaviour
{
    public Text nameLabel;    // e.g., "Server 12345"
    public Text addressLabel; // e.g., 192.168.1.10:7777
    public Button joinButton;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
    public TMP_Text tmpNameLabel;
    public TMP_Text tmpAddressLabel;
#endif

    ServerResponse _info;
    System.Action<ServerResponse> _onJoin;
    bool _layoutPrepared;

    public void Bind(ServerResponse info, System.Action<ServerResponse> onJoin)
    {
        _info = info;
        _onJoin = onJoin;

        // Ensure row has some height for VerticalLayoutGroup/Content
        var rtRow = transform as RectTransform;
        if (rtRow != null)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            if (transform.localScale == Vector3.zero) transform.localScale = Vector3.one;
            var cg = GetComponent<CanvasGroup>();
            if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
            var le = GetComponent<LayoutElement>();
            if (le == null) le = gameObject.AddComponent<LayoutElement>();
            if (le.preferredHeight <= 0) le.preferredHeight = 40f;
            le.flexibleWidth = 1f;
            le.minWidth = 100f;
            // Add a subtle background so the row is visible against transparent UIs
            if (GetComponent<Image>() == null)
            {
                var bg = gameObject.AddComponent<Image>();
                bg.color = new Color(0f, 0f, 0f, 0.25f);
                bg.raycastTarget = false;
            }
        }

        string host = info.EndPoint != null ? info.EndPoint.Address.ToString() : (info.uri != null ? info.uri.Host : "?");
        string port = info.uri != null ? (info.uri.Port >= 0 ? info.uri.Port.ToString() : "?") : "?";
        string displayName = null;
#if !DISABLESTEAMWORKS
        try
        {
            // If this is a Steam entry, use the lobby name if available
            if (info.uri != null && info.uri.Scheme == "steam")
            {
                var lobbyId = (ulong)info.serverId;
                var csteam = new Steamworks.CSteamID(lobbyId);
                var lobbyName = Steamworks.SteamMatchmaking.GetLobbyData(csteam, "name");
                if (!string.IsNullOrEmpty(lobbyName)) displayName = lobbyName;
                host = "Steam"; // override host label to avoid showing raw IDs
                port = "P2P";
            }
        }
        catch { }
#endif
        // Auto-find UI elements if not assigned
        if (nameLabel == null)
            nameLabel = GetComponentInChildren<Text>(true);
        if (addressLabel == null)
        {
            var texts = GetComponentsInChildren<Text>(true);
            if (texts.Length > 1) addressLabel = texts[1];
        }
#if TMP_PRESENT || UNITY_TEXTMESHPRO
        if (tmpNameLabel == null)
            tmpNameLabel = GetComponentInChildren<TMP_Text>(true);
        if (tmpAddressLabel == null)
        {
            var tmps = GetComponentsInChildren<TMP_Text>(true);
            if (tmps.Length > 1) tmpAddressLabel = tmps[1];
        }
#endif
    bool hasTmpName = false;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
    hasTmpName = tmpNameLabel != null;
#endif
        if (nameLabel == null && !hasTmpName)
        {
            // Create a basic UGUI Text for name if none present
            var go = new GameObject("NameLabel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0.6f, 0.5f);
                rt.offsetMin = new Vector2(10, -10);
                rt.offsetMax = new Vector2(-10, 10);
            nameLabel = go.AddComponent<Text>();
            nameLabel.color = Color.white;
            nameLabel.fontSize = 14;
            nameLabel.alignment = TextAnchor.MiddleLeft;
        }
    bool hasTmpAddr = false;
#if TMP_PRESENT || UNITY_TEXTMESHPRO
    hasTmpAddr = tmpAddressLabel != null;
#endif
        if (addressLabel == null && !hasTmpAddr)
        {
            var go = new GameObject("AddressLabel", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0, 0.5f);
                rt.anchorMax = new Vector2(0.6f, 0.5f);
                rt.offsetMin = new Vector2(10, -28);
                rt.offsetMax = new Vector2(-10, -4);
            addressLabel = go.AddComponent<Text>();
            addressLabel.color = new Color(0.8f, 0.8f, 0.8f);
            addressLabel.fontSize = 12;
            addressLabel.alignment = TextAnchor.MiddleLeft;
        }
    if (nameLabel != null) nameLabel.text = string.IsNullOrEmpty(displayName) ? $"Server {info.serverId}" : displayName;
        if (addressLabel != null) addressLabel.text = $"{host}:{port}";
#if TMP_PRESENT || UNITY_TEXTMESHPRO
    if (tmpNameLabel != null) tmpNameLabel.text = string.IsNullOrEmpty(displayName) ? $"Server {info.serverId}" : displayName;
        if (tmpAddressLabel != null) tmpAddressLabel.text = $"{host}:{port}";
#endif

    if (joinButton == null) joinButton = GetComponentInChildren<Button>(true);
        if (joinButton == null)
        {
            // Create a simple button on the right
            var go = new GameObject("JoinButton", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(1, 0.5f);
            rt.anchorMax = new Vector2(1, 0.5f);
            rt.sizeDelta = new Vector2(90, 28);
            rt.anchoredPosition = new Vector2(-60, 0);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            joinButton = go.AddComponent<Button>();
            var labelGO = new GameObject("Text", typeof(RectTransform));
            labelGO.transform.SetParent(go.transform, false);
            var lrt = labelGO.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = lrt.offsetMax = Vector2.zero;
            var txt = labelGO.AddComponent<Text>();
            txt.text = "Join";
            txt.alignment = TextAnchor.MiddleCenter;
            txt.color = Color.white;
            txt.fontSize = 14;
            // assign a default font so it renders
            txt.font = GetDefaultFont();
        }
        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(() => _onJoin?.Invoke(_info));
        }

        // Ensure created UGUI Texts have a font so they render
        if (nameLabel != null && nameLabel.font == null)
            nameLabel.font = GetDefaultFont();
        if (addressLabel != null && addressLabel.font == null)
            addressLabel.font = GetDefaultFont();

        // Prepare left-right layout only once
        if (!_layoutPrepared)
        {
            PrepareLeftRightLayout();
            _layoutPrepared = true;
        }

    var size = rtRow != null ? rtRow.rect.size : Vector2.zero;
    Debug.Log($"ServerRowUI: Bound row for {host}:{port} (id={info.serverId}) size={size}.");
    }

    void PrepareLeftRightLayout()
    {
        // Add a horizontal layout group to the row to distribute Left (expand) and Right (fixed)
        var hlg = GetComponent<HorizontalLayoutGroup>();
        if (hlg == null)
        {
            hlg = gameObject.AddComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = true;
            hlg.childForceExpandHeight = false;
            hlg.childAlignment = TextAnchor.MiddleCenter;
        }

        // Left container for labels
        var left = transform.Find("Left");
        if (left == null)
        {
            var goLeft = new GameObject("Left", typeof(RectTransform));
            goLeft.transform.SetParent(transform, false);
            left = goLeft.transform;
        }
        var leftRT = left as RectTransform;
        if (leftRT != null)
        {
            var le = leftRT.GetComponent<LayoutElement>() ?? leftRT.gameObject.AddComponent<LayoutElement>();
            le.flexibleWidth = 1f;
            le.minWidth = 100f;
        }
        var vlg = leftRT.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = leftRT.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 2f;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
        }

        // Spacer to push Right to the far edge
        var spacer = transform.Find("Spacer");
        if (spacer == null)
        {
            var goSpacer = new GameObject("Spacer", typeof(RectTransform));
            goSpacer.transform.SetParent(transform, false);
            spacer = goSpacer.transform;
            var sle = goSpacer.AddComponent<LayoutElement>();
            sle.flexibleWidth = 1f; // takes leftover width
        }

        // Right container for the button
        var right = transform.Find("Right");
        if (right == null)
        {
            var goRight = new GameObject("Right", typeof(RectTransform));
            goRight.transform.SetParent(transform, false);
            right = goRight.transform;
        }
        var rightRT = right as RectTransform;
        if (rightRT != null)
        {
            var le = rightRT.GetComponent<LayoutElement>() ?? rightRT.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 110f;
            le.flexibleWidth = 0f;
            le.minWidth = 90f;
        }

        // Move existing elements into containers
        if (nameLabel != null) nameLabel.transform.SetParent(left, false);
#if TMP_PRESENT || UNITY_TEXTMESHPRO
        if (tmpNameLabel != null) tmpNameLabel.transform.SetParent(left, false);
#endif
        if (addressLabel != null) addressLabel.transform.SetParent(left, false);
#if TMP_PRESENT || UNITY_TEXTMESHPRO
        if (tmpAddressLabel != null) tmpAddressLabel.transform.SetParent(left, false);
#endif
        if (joinButton != null) joinButton.transform.SetParent(right, false);
    }

    static Font GetDefaultFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        }
        if (f == null)
        {
            try
            {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
                f = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Tahoma" }, 14);
#else
                f = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "Liberation Sans" }, 14);
#endif
            }
            catch { }
        }
        return f;
    }
}
