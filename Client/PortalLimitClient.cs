using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PortalLimit.Client
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PortalLimitClientPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "keith.valheim.portallimit.client";
        public const string PluginName = "Portal Limit and Discovery Client";
        public const string PluginVersion = "0.1.3";

        private const string RpcConfigRequest = "keith.valheim.portallimit.config_request";
        private const string RpcConfigResponse = "keith.valheim.portallimit.config_response";
        private const string RpcConfigUpdate = "keith.valheim.portallimit.config_update";
        private const string RpcCountRequest = "keith.valheim.portallimit.count_request";
        private const string RpcCountResponse = "keith.valheim.portallimit.count_response";
        private const string RpcNotice = "keith.valheim.portallimit.notice";
        private const string RpcDiscoveryMarkRequest = "keith.valheim.portallimit.discovery_mark_request";
        private const string RpcDiscoveryUnlockRequest = "keith.valheim.portallimit.discovery_unlock_request";
        private const string RpcDiscoveryUnlockListRequest = "keith.valheim.portallimit.discovery_unlock_list_request";
        private const string RpcDiscoveryUnlockListResponse = "keith.valheim.portallimit.discovery_unlock_list_response";

        private const string DiscoveryRoleKey = "PortalLimit_DiscoveryRole";
        private const string DiscoveryLinkKey = "PortalLimit_DiscoveryLink";
        private const string DiscoveryColorKey = "PortalLimit_DiscoveryColor";
        private const string DiscoveryMarkerTextKey = "PortalLimit_DiscoveryMarkerText";
        private const int DiscoveryRoleNone = 0;
        private const int DiscoveryRoleLocked = 1;
        private const int DiscoveryRoleRemote = 2;

        internal static PortalLimitClientPlugin Instance;
        internal static ManualLogSource Log;

        private static readonly FieldInfo AllPiecesField = AccessTools.Field(typeof(Piece), "s_allPieces");
        private static readonly MethodInfo GetPlayerIdMethod = AccessTools.Method(typeof(Player), "GetPlayerID");
        private static readonly MethodInfo GetPrefabNameMethod = AccessTools.Method(typeof(ZNetView), "GetPrefabName");
        private static readonly Color LockedOrange = new Color(1f, 0.6235294f, 0.1019608f, 1f);
        private static readonly string[] MarkerColorLabels = { "Orange", "Black", "White", "Green", "Red" };
        private static readonly string[] MarkerColorValues = { "#ff9f1a", "#000000", "#ffffff", "#42ff68", "#ff3030" };
        private static readonly string[] PortalRoleChoiceLabels = { "NORMAL PORTAL", "LOCKED ENTRANCE", "UNLOCKED ENTRANCE" };
        private static readonly string[] MarkerTextLabels = { "UNLOCK ON OTHER SIDE", "LOCKED", "X" };
        private static readonly string[] MarkerTextValues = { "UnlockOnOtherSide", "Locked", "X" };

        private Harmony harmony;
        private bool rpcRegistered;
        private bool configRequested;
        private bool showCountMessageOnNextResponse;
        private bool adminStatusKnown;
        private bool isAdmin;
        private bool adminBypass = true;
        private bool enforceOnServer = true;
        private int maxPortalsPerPlayer = 4;
        private string portalPrefabNames = "portal_wood,portal_stone";
        private bool discoveryEnabled = true;
        private bool discoveryAdminBypass = true;
        private bool showDiscoveryHoverText = true;
        private string discoveryLockedHoverText = "Locked: find the other end first.";
        private string discoveryRemoteHoverText = "Discovery portal: use this side to unlock the route.";
        private string discoveryUnlockedHoverText = "Discovery route unlocked.";
        private string discoveryLockedMessage = "This portal is locked. Find and enter the other end first.";
        private string discoveryUnlockMessage = "Discovery portal unlocked.";
        private bool discoveryShowWorldMarker = true;
        private bool discoveryTintPortalModel;
        private bool discoverySuppressLockedGlow = true;
        private string discoveryLockedColor = "#ff9f1a";
        private string discoveryRemoteColor = "#38a6ff";
        private string discoveryUnlockedColor = "#42ff68";
        private readonly HashSet<string> unlockedDiscoveryLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private int serverPortalCount;
        private Rect adminWindow = new Rect(120f, 60f, 700f, 720f);
        private Vector2 adminScroll;
        private bool showAdminWindow;
        private int adminTab;
        private string openDropdown = string.Empty;
        private int selectedPortalRole;
        private string loadedPortalKey = string.Empty;
        private TeleportWorld selectedPortal;
        private string maxText = "4";
        private string portalNamesText = "portal_wood,portal_stone";
        private string discoveryLinkText = string.Empty;
        private string selectedPortalColorText = "#ff9f1a";
        private int selectedPortalColorIndex;
        private string discoveryLockedHoverTextField = "Locked: find the other end first.";
        private string discoveryRemoteHoverTextField = "Discovery portal: use this side to unlock the route.";
        private string discoveryUnlockedHoverTextField = "Discovery route unlocked.";
        private string discoveryLockedMessageField = "This portal is locked. Find and enter the other end first.";
        private string discoveryUnlockMessageField = "Discovery portal unlocked.";
        private int selectedPortalMarkerTextIndex;
        private string discoveryLockedColorField = "#ff9f1a";
        private string discoveryRemoteColorField = "#38a6ff";
        private string discoveryUnlockedColorField = "#42ff68";
        private string statusMessage = "Use /portallimit to load server settings.";
        private bool adminStylesReady;
        private Texture2D adminWindowTexture;
        private Texture2D adminHeaderTexture;
        private Texture2D adminSectionTexture;
        private Texture2D adminInputTexture;
        private Texture2D adminButtonTexture;
        private Texture2D adminButtonHoverTexture;
        private Texture2D adminTabTexture;
        private Texture2D adminTabSelectedTexture;
        private GUIStyle adminWindowStyle;
        private GUIStyle adminTitleStyle;
        private GUIStyle adminLabelStyle;
        private GUIStyle adminHelpStyle;
        private GUIStyle adminSectionStyle;
        private GUIStyle adminButtonStyle;
        private GUIStyle adminTabStyle;
        private GUIStyle adminTabSelectedStyle;
        private GUIStyle adminTextFieldStyle;
        private GUIStyle adminBoxStyle;

        private ConfigEntry<KeyCode> toggleAdminUiKey;
        private ConfigEntry<bool> autoOpenAdminUiOnPortalPlacement;
        private ConfigEntry<bool> openAdminUiWithPortalInteract;

        private void Awake()
        {
            if (IsDedicatedServerProcess())
            {
                Logger.LogInfo("Portal Limit Client disabled on the dedicated server process.");
                enabled = false;
                return;
            }

            Instance = this;
            Log = Logger;
            toggleAdminUiKey = Config.Bind("Input", "ToggleAdminUIKey", KeyCode.F10, "Key used to open the portal limit admin UI.");
            autoOpenAdminUiOnPortalPlacement = Config.Bind("Input", "AutoOpenAdminUIWhenPlacingPortal", true, "Open the admin portal setup window after an admin places a portal.");
            openAdminUiWithPortalInteract = Config.Bind("Input", "OpenAdminUIWithPortalInteract", true, "When an admin presses the normal portal interact key, open the Portal Limit portal editor instead of the vanilla tag prompt.");

            harmony = new Harmony(PluginGuid);
            harmony.PatchAll();
            InvokeRepeating("TryRegisterRpcs", 1f, 1f);
        }

        private void OnDestroy()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
            }

            DestroyAdminTexture(adminWindowTexture);
            DestroyAdminTexture(adminHeaderTexture);
            DestroyAdminTexture(adminSectionTexture);
            DestroyAdminTexture(adminInputTexture);
            DestroyAdminTexture(adminButtonTexture);
            DestroyAdminTexture(adminButtonHoverTexture);
            DestroyAdminTexture(adminTabTexture);
            DestroyAdminTexture(adminTabSelectedTexture);
        }

        private static bool IsDedicatedServerProcess()
        {
            string processName = Process.GetCurrentProcess().ProcessName;
            return processName.StartsWith("valheim_server", StringComparison.OrdinalIgnoreCase);
        }

        private void TryRegisterRpcs()
        {
            if (rpcRegistered || ZRoutedRpc.instance == null)
            {
                return;
            }

            ZRoutedRpc.instance.Register<string>(RpcConfigResponse, OnConfigResponse);
            ZRoutedRpc.instance.Register<int, int, bool, string>(RpcCountResponse, OnCountResponse);
            ZRoutedRpc.instance.Register<string>(RpcNotice, OnNotice);
            ZRoutedRpc.instance.Register<string>(RpcDiscoveryUnlockListResponse, OnDiscoveryUnlockListResponse);
            rpcRegistered = true;
            CancelInvoke("TryRegisterRpcs");
        }

        private void Update()
        {
            if (!rpcRegistered || ZNet.instance == null)
            {
                return;
            }

            if (!configRequested && Player.m_localPlayer != null)
            {
                RequestConfig(false);
            }

            if (Input.GetKeyDown(toggleAdminUiKey.Value))
            {
                ToggleAdminWindow();
            }
        }

        private void OnGUI()
        {
            if (showAdminWindow)
            {
                EnsureAdminStyles();
                adminWindow = GUI.Window(GetInstanceID(), adminWindow, DrawAdminWindow, GUIContent.none, GUIStyle.none);
            }
        }

        private void DrawAdminWindow(int id)
        {
            EnsureAdminStyles();
            GUI.Box(new Rect(0f, 0f, adminWindow.width, adminWindow.height), GUIContent.none, adminWindowStyle);
            GUI.Box(new Rect(0f, 0f, adminWindow.width, 42f), GUIContent.none, adminSectionStyle);
            GUI.Label(new Rect(18f, 9f, adminWindow.width - 36f, 28f), "Portal Limit Admin", adminTitleStyle);

            GUILayout.BeginArea(new Rect(16f, 48f, adminWindow.width - 32f, adminWindow.height - 62f));
            GUILayout.BeginVertical();
            GUILayout.Label(statusMessage, adminHelpStyle);

            if (isAdmin)
            {
                DrawAdminTabs();
                adminScroll = GUILayout.BeginScrollView(adminScroll, GUILayout.Height(Math.Max(220f, adminWindow.height - 145f)));
                if (adminTab == 0)
                {
                    DrawServerSettingsTab();
                }
                else
                {
                    DrawPortalSettingsTab();
                }

                GUILayout.EndScrollView();
                DrawAdminFooter();
            }
            else
            {
                GUILayout.Label("You need to be on the Valheim admin list to edit this.", adminLabelStyle);
                if (GUILayout.Button("Close", adminButtonStyle))
                {
                    showAdminWindow = false;
                }
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();
            GUI.DragWindow(new Rect(0f, 0f, adminWindow.width, 42f));
        }

        private void EnsureAdminStyles()
        {
            if (adminStylesReady)
            {
                return;
            }

            adminWindowTexture = MakeAdminTexture(new Color(0.03f, 0.026f, 0.022f, 0.98f));
            adminHeaderTexture = MakeAdminTexture(new Color(0.18f, 0.115f, 0.065f, 0.98f));
            adminSectionTexture = MakeAdminTexture(new Color(0.13f, 0.085f, 0.052f, 0.96f));
            adminInputTexture = MakeAdminBorderTexture(new Color(0.015f, 0.014f, 0.012f, 0.99f), new Color(0.83f, 0.64f, 0.32f, 1f));
            adminButtonTexture = MakeAdminTexture(new Color(0.25f, 0.24f, 0.21f, 0.98f));
            adminButtonHoverTexture = MakeAdminTexture(new Color(0.36f, 0.31f, 0.23f, 0.98f));
            adminTabTexture = MakeAdminTexture(new Color(0.16f, 0.14f, 0.12f, 0.98f));
            adminTabSelectedTexture = MakeAdminTexture(new Color(0.45f, 0.31f, 0.14f, 0.98f));

            adminWindowStyle = new GUIStyle(GUI.skin.box);
            adminWindowStyle.normal.background = adminWindowTexture;
            adminWindowStyle.border = new RectOffset(8, 8, 8, 8);
            adminWindowStyle.padding = new RectOffset(14, 14, 14, 14);

            adminTitleStyle = new GUIStyle(GUI.skin.label);
            adminTitleStyle.fontSize = 18;
            adminTitleStyle.fontStyle = FontStyle.Bold;
            adminTitleStyle.alignment = TextAnchor.MiddleCenter;
            adminTitleStyle.normal.textColor = new Color(0.95f, 0.83f, 0.55f, 1f);

            adminLabelStyle = new GUIStyle(GUI.skin.label);
            adminLabelStyle.fontSize = 14;
            adminLabelStyle.wordWrap = true;
            adminLabelStyle.normal.textColor = new Color(0.88f, 0.84f, 0.74f, 1f);

            adminHelpStyle = new GUIStyle(adminLabelStyle);
            adminHelpStyle.fontSize = 13;
            adminHelpStyle.normal.textColor = new Color(0.67f, 0.64f, 0.56f, 1f);

            adminSectionStyle = new GUIStyle(GUI.skin.box);
            adminSectionStyle.normal.background = adminSectionTexture;
            adminSectionStyle.border = new RectOffset(4, 4, 4, 4);
            adminSectionStyle.padding = new RectOffset(8, 8, 4, 4);

            adminSectionStyle.normal.textColor = new Color(0.98f, 0.82f, 0.43f, 1f);
            adminSectionStyle.alignment = TextAnchor.MiddleLeft;
            adminSectionStyle.fontStyle = FontStyle.Bold;
            adminSectionStyle.fontSize = 15;

            adminButtonStyle = new GUIStyle(GUI.skin.button);
            adminButtonStyle.normal.background = adminButtonTexture;
            adminButtonStyle.hover.background = adminButtonHoverTexture;
            adminButtonStyle.active.background = adminTabSelectedTexture;
            adminButtonStyle.normal.textColor = new Color(0.9f, 0.86f, 0.76f, 1f);
            adminButtonStyle.hover.textColor = new Color(1f, 0.9f, 0.58f, 1f);
            adminButtonStyle.active.textColor = Color.white;
            adminButtonStyle.fontSize = 13;
            adminButtonStyle.padding = new RectOffset(8, 8, 5, 5);
            adminButtonStyle.margin = new RectOffset(3, 3, 3, 3);

            adminTabStyle = new GUIStyle(adminButtonStyle);
            adminTabStyle.normal.background = adminTabTexture;
            adminTabStyle.fontStyle = FontStyle.Bold;

            adminTabSelectedStyle = new GUIStyle(adminButtonStyle);
            adminTabSelectedStyle.normal.background = adminTabSelectedTexture;
            adminTabSelectedStyle.normal.textColor = Color.white;
            adminTabSelectedStyle.fontStyle = FontStyle.Bold;

            adminTextFieldStyle = new GUIStyle(GUI.skin.textField);
            adminTextFieldStyle.normal.background = adminInputTexture;
            adminTextFieldStyle.focused.background = adminInputTexture;
            adminTextFieldStyle.hover.background = adminInputTexture;
            adminTextFieldStyle.border = new RectOffset(4, 4, 4, 4);
            adminTextFieldStyle.normal.textColor = new Color(1f, 0.96f, 0.82f, 1f);
            adminTextFieldStyle.focused.textColor = Color.white;
            adminTextFieldStyle.fontSize = 15;
            adminTextFieldStyle.padding = new RectOffset(10, 10, 7, 7);
            adminTextFieldStyle.margin = new RectOffset(3, 3, 4, 8);

            adminBoxStyle = new GUIStyle(GUI.skin.box);
            adminBoxStyle.normal.background = adminHeaderTexture;
            adminBoxStyle.padding = new RectOffset(6, 6, 6, 6);
            adminBoxStyle.margin = new RectOffset(0, 0, 2, 8);

            adminStylesReady = true;
        }

        private static Texture2D MakeAdminTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private static Texture2D MakeAdminBorderTexture(Color fill, Color border)
        {
            Texture2D texture = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    bool edge = x < 2 || y < 2 || x > 13 || y > 13;
                    texture.SetPixel(x, y, edge ? border : fill);
                }
            }

            texture.Apply();
            return texture;
        }

        private static void DestroyAdminTexture(Texture2D texture)
        {
            if (texture != null)
            {
                Destroy(texture);
            }
        }

        private void DrawAdminTabs()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Server Settings", adminTab == 0 ? adminTabSelectedStyle : adminTabStyle))
            {
                adminTab = 0;
                openDropdown = string.Empty;
            }

            if (GUILayout.Button("This Portal", adminTab == 1 ? adminTabSelectedStyle : adminTabStyle))
            {
                adminTab = 1;
                openDropdown = string.Empty;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawSectionHeader(string text)
        {
            GUILayout.Box(text, adminSectionStyle, GUILayout.ExpandWidth(true), GUILayout.Height(30f));
        }

        private void DrawServerSettingsTab()
        {
            GUILayout.Space(8f);
            DrawSectionHeader("Portal limit");
            GUILayout.Label("How many counted portals may each non-admin player own? Use 0 for no limit.", adminHelpStyle);
            maxText = GUILayout.TextField(maxText, adminTextFieldStyle);
            adminBypass = DrawBoolDropdown("adminBypass", "Admin portal limit bypass", adminBypass, "Allowed", "Not allowed");
            enforceOnServer = DrawBoolDropdown("enforceOnServer", "Server removes extra portals over the limit", enforceOnServer, "Enabled", "Disabled");
            GUILayout.Label("Which portal types count toward the limit?", adminLabelStyle);
            GUILayout.Label("Default covers normal wood and stone portals. Empty means every portal-like object.", adminHelpStyle);
            portalNamesText = GUILayout.TextField(portalNamesText, adminTextFieldStyle);
            GUILayout.Label("Your current portal count: " + serverPortalCount + " / " + maxPortalsPerPlayer, adminLabelStyle);

            GUILayout.Space(12f);
            DrawSectionHeader("Locked discovery portals");
            GUILayout.Label("These settings apply to every locked discovery portal pair.", adminHelpStyle);
            discoveryEnabled = DrawBoolDropdown("discoveryEnabled", "Locked discovery portals", discoveryEnabled, "Enabled", "Disabled");
            discoveryAdminBypass = DrawBoolDropdown("discoveryAdminBypass", "Admin bypass for locked discovery portals", discoveryAdminBypass, "Allowed", "Not allowed");
            showDiscoveryHoverText = DrawBoolDropdown("showDiscoveryHoverText", "Lock/unlock hover text", showDiscoveryHoverText, "Shown", "Hidden");
            autoOpenAdminUiOnPortalPlacement.Value = DrawBoolDropdown("autoOpenAdminUiOnPortalPlacement", "After I place a portal", autoOpenAdminUiOnPortalPlacement.Value, "Open this setup window", "Do nothing");
            openAdminUiWithPortalInteract.Value = DrawBoolDropdown("openAdminUiWithPortalInteract", "When an admin presses E on a portal", openAdminUiWithPortalInteract.Value, "Open this setup window", "Use Valheim's tag prompt");
            discoveryShowWorldMarker = DrawBoolDropdown("discoveryShowWorldMarker", "Unlock message marker on locked entrances", discoveryShowWorldMarker, "Shown", "Hidden");
            discoverySuppressLockedGlow = DrawBoolDropdown("discoverySuppressLockedGlow", "Connected orange glow while locked", discoverySuppressLockedGlow, "Hidden", "Shown");

            GUILayout.Space(12f);
            DrawSectionHeader("Player-facing hover text");
            GUILayout.Label("Locked entrance text", adminLabelStyle);
            discoveryLockedHoverTextField = GUILayout.TextField(discoveryLockedHoverTextField, adminTextFieldStyle);
            GUILayout.Label("Unlocked entrance text", adminLabelStyle);
            discoveryRemoteHoverTextField = GUILayout.TextField(discoveryRemoteHoverTextField, adminTextFieldStyle);
            GUILayout.Label("After unlocked text", adminLabelStyle);
            discoveryUnlockedHoverTextField = GUILayout.TextField(discoveryUnlockedHoverTextField, adminTextFieldStyle);
        }

        private void DrawPortalSettingsTab()
        {
            GUILayout.Space(8f);
            TeleportWorld hoveredPortal = GetHoveredPortal();
            if (hoveredPortal != null && hoveredPortal != selectedPortal)
            {
                SelectPortalForEditing(hoveredPortal, false);
            }

            TeleportWorld portal = GetSelectedPortal();
            if (portal == null)
            {
                DrawSectionHeader("Selected portal");
                GUILayout.Label("No portal selected.", adminLabelStyle);
                GUILayout.Label("Look directly at a portal, then click Select.", adminHelpStyle);
            }
            else
            {
                DrawSectionHeader("Selected portal");
                int currentRole = GetDiscoveryRole(portal);
                string currentTag = GetPortalTagForUi(portal);
                GUILayout.Label("Selected portal: " + GetPortalRoleLabel(currentRole), adminLabelStyle);
                GUILayout.Label("Portal tag: " + (string.IsNullOrWhiteSpace(currentTag) ? "(none)" : currentTag), adminLabelStyle);
            }

            GUILayout.Space(8f);
            GUILayout.Label("Portal tag", adminLabelStyle);
            GUILayout.Label("Use the same tag on both portals, for example: Bog Witch.", adminHelpStyle);
            discoveryLinkText = GUILayout.TextField(discoveryLinkText, adminTextFieldStyle);

            GUILayout.Space(8f);
            DrawSectionHeader("This portal");
            GUILayout.Label("What should this portal do?", adminLabelStyle);
            selectedPortalRole = DrawChoiceList("selectedPortalRole", selectedPortalRole, PortalRoleChoiceLabels);

            GUILayout.Space(8f);
            GUILayout.Label("Marker color", adminLabelStyle);
            selectedPortalColorIndex = DrawChoiceList("selectedPortalColor", selectedPortalColorIndex, MarkerColorLabels);
            selectedPortalColorText = MarkerColorValues[Mathf.Clamp(selectedPortalColorIndex, 0, MarkerColorValues.Length - 1)];

            GUILayout.Space(8f);
            GUILayout.Label("Marker text", adminLabelStyle);
            selectedPortalMarkerTextIndex = DrawChoiceList("selectedPortalMarkerText", selectedPortalMarkerTextIndex, MarkerTextLabels);
        }

        private bool DrawBoolDropdown(string id, string label, bool value, string trueLabel, string falseLabel)
        {
            int selected = value ? 0 : 1;
            selected = DrawChoiceDropdown(id, label, selected, new[] { trueLabel, falseLabel });
            return selected == 0;
        }

        private int DrawChoiceDropdown(string id, int selected, string[] labels)
        {
            return DrawChoiceDropdown(id, string.Empty, selected, labels);
        }

        private int DrawChoiceDropdown(string id, string label, int selected, string[] labels)
        {
            if (labels == null || labels.Length == 0)
            {
                return selected;
            }

            selected = Mathf.Clamp(selected, 0, labels.Length - 1);
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrWhiteSpace(label))
            {
                GUILayout.Label(label, adminLabelStyle, GUILayout.Width(330f));
            }

            string buttonText = labels[selected] + "  v";
            if (GUILayout.Button(buttonText, adminButtonStyle, GUILayout.MinWidth(190f)))
            {
                openDropdown = openDropdown == id ? string.Empty : id;
            }

            GUILayout.EndHorizontal();

            if (openDropdown == id)
            {
                GUILayout.BeginVertical(adminBoxStyle);
                for (int i = 0; i < labels.Length; i++)
                {
                    string prefix = i == selected ? "> " : "  ";
                    if (GUILayout.Button(prefix + labels[i], adminButtonStyle))
                    {
                        selected = i;
                        openDropdown = string.Empty;
                    }
                }

                GUILayout.EndVertical();
            }

            return selected;
        }

        private int DrawChoiceList(string id, int selected, string[] labels)
        {
            if (labels == null || labels.Length == 0)
            {
                return selected;
            }

            selected = Mathf.Clamp(selected, 0, labels.Length - 1);
            GUILayout.BeginVertical(adminBoxStyle);
            for (int i = 0; i < labels.Length; i++)
            {
                string prefix = i == selected ? "> " : "  ";
                if (GUILayout.Button(prefix + labels[i], adminButtonStyle))
                {
                    selected = i;
                    openDropdown = string.Empty;
                }
            }

            GUILayout.EndVertical();
            return selected;
        }

        private void DrawAdminFooter()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply", adminButtonStyle))
            {
                ApplyActiveTab(false);
            }

            if (GUILayout.Button("Apply & Close", adminButtonStyle))
            {
                ApplyActiveTab(true);
            }

            if (GUILayout.Button("Close Without Applying", adminButtonStyle))
            {
                showAdminWindow = false;
            }

            GUILayout.EndHorizontal();
        }

        private void ApplyActiveTab(bool closeAfterApply)
        {
            if (adminTab == 0)
            {
                SaveConfigFromUi();
            }
            else
            {
                MarkSelectedDiscoveryPortal(GetSelectedPortalRoleValue());
            }

            if (closeAfterApply)
            {
                showAdminWindow = false;
            }
        }

        private void ToggleAdminWindow()
        {
            if (!adminStatusKnown)
            {
                RequestConfig(false);
                ShowMessage("Checking portal limit permissions...");
                return;
            }

            if (!isAdmin)
            {
                showAdminWindow = false;
                ShowMessage("Only admins can open the Portal Limit admin UI.");
                return;
            }

            showAdminWindow = !showAdminWindow;
            openDropdown = string.Empty;
            if (showAdminWindow)
            {
                RequestConfig(true);
                RequestCount();
            }
        }

        private void OpenAdminWindowForPortal(TeleportWorld portal)
        {
            SelectPortalForEditing(portal, false);
            showAdminWindow = true;
            openDropdown = string.Empty;
            adminTab = 1;
            statusMessage = "Portal selected. Choose what this portal should do, then Apply or Apply & Close.";
            RequestConfig(true);
            RequestCount();
        }

        private void OpenAdminWindowAfterPortalPlacement()
        {
            showAdminWindow = true;
            openDropdown = string.Empty;
            adminTab = 1;
            selectedPortalRole = 0;
            statusMessage = "Portal placed. Look at it, choose what this portal should do, then use the buttons below.";
            RequestConfig(true);
            RequestCount();
        }

        private void OpenAdminWindowAfterPortalPlacement(TeleportWorld portal)
        {
            SelectPortalForEditing(portal, false);
            OpenAdminWindowAfterPortalPlacement();
        }

        private void SaveConfigFromUi()
        {
            int max;
            if (!int.TryParse(maxText, out max))
            {
                statusMessage = "Max portals must be a whole number.";
                return;
            }

            string payload = "max=" + Math.Max(0, max) +
                             "\nadminBypass=" + adminBypass +
                             "\nenforceOnServer=" + enforceOnServer +
                             "\nportalPrefabNames=" + Escape(portalNamesText) +
                             "\ndiscoveryEnabled=" + discoveryEnabled +
                             "\ndiscoveryAdminBypass=" + discoveryAdminBypass +
                             "\nshowDiscoveryHoverText=" + showDiscoveryHoverText +
                             "\ndiscoveryLockedHoverText=" + Escape(discoveryLockedHoverTextField) +
                             "\ndiscoveryRemoteHoverText=" + Escape(discoveryRemoteHoverTextField) +
                             "\ndiscoveryUnlockedHoverText=" + Escape(discoveryUnlockedHoverTextField) +
                             "\ndiscoveryLockedMessage=" + Escape(discoveryLockedMessageField) +
                             "\ndiscoveryUnlockMessage=" + Escape(discoveryUnlockMessageField) +
                             "\ndiscoveryShowWorldMarker=" + discoveryShowWorldMarker +
                             "\ndiscoveryTintPortalModel=" + discoveryTintPortalModel +
                             "\ndiscoverySuppressLockedGlow=" + discoverySuppressLockedGlow +
                             "\ndiscoveryLockedColor=" + Escape(discoveryLockedColorField) +
                             "\ndiscoveryRemoteColor=" + Escape(discoveryRemoteColorField) +
                             "\ndiscoveryUnlockedColor=" + Escape(discoveryUnlockedColorField);
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcConfigUpdate, payload);
            statusMessage = "Saving portal limit settings...";
        }

        private void RequestConfig(bool openUi)
        {
            configRequested = true;
            if (openUi)
            {
                statusMessage = "Loading portal limit settings...";
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(RpcConfigRequest, (openUi ? string.Empty : "[silent]") + GetPlayerName());
            RequestUnlockList();
        }

        private void RequestCount()
        {
            long playerId = GetLocalPlayerId();
            if (playerId != 0L)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcCountRequest, playerId);
            }
        }

        private void RequestCountForChat()
        {
            showCountMessageOnNextResponse = true;
            RequestCount();
            ShowMessage("Checking portal count...");
        }

        private void RequestUnlockList()
        {
            long playerId = GetLocalPlayerId();
            if (playerId != 0L)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcDiscoveryUnlockListRequest, playerId);
            }
        }

        private static void OnConfigResponse(long sender, string payload)
        {
            Instance.ApplyConfigPayload(payload);
        }

        private static void OnCountResponse(long sender, int count, int max, bool limitReached, string payload)
        {
            Instance.serverPortalCount = count;
            Instance.maxPortalsPerPlayer = max;
            Instance.ApplyConfigPayload(payload);
            if (Instance.showCountMessageOnNextResponse)
            {
                Instance.showCountMessageOnNextResponse = false;
                string maxText = max > 0 ? max.ToString() : "unlimited";
                Instance.ShowMessage("Portals: " + count + " / " + maxText);
            }
        }

        private static void OnNotice(long sender, string message)
        {
            Instance.ShowMessage(message);
        }

        private static void OnDiscoveryUnlockListResponse(long sender, string payload)
        {
            Instance.ApplyUnlockPayload(payload);
        }

        private void ApplyConfigPayload(string payload)
        {
            Dictionary<string, string> values = ParsePayload(payload);
            int max;
            bool flag;
            string value;

            if (values.TryGetValue("isAdmin", out value) && bool.TryParse(value, out flag))
            {
                isAdmin = flag;
                adminStatusKnown = true;
                if (!isAdmin)
                {
                    showAdminWindow = false;
                }
            }

            if (values.TryGetValue("max", out value) && int.TryParse(value, out max))
            {
                maxPortalsPerPlayer = max;
                maxText = max.ToString();
            }

            if (values.TryGetValue("adminBypass", out value) && bool.TryParse(value, out flag))
            {
                adminBypass = flag;
            }

            if (values.TryGetValue("enforceOnServer", out value) && bool.TryParse(value, out flag))
            {
                enforceOnServer = flag;
            }

            if (values.TryGetValue("portalPrefabNames", out value))
            {
                portalPrefabNames = value;
                portalNamesText = value;
            }

            if (values.TryGetValue("discoveryEnabled", out value) && bool.TryParse(value, out flag))
            {
                discoveryEnabled = flag;
            }

            if (values.TryGetValue("discoveryAdminBypass", out value) && bool.TryParse(value, out flag))
            {
                discoveryAdminBypass = flag;
            }

            if (values.TryGetValue("showDiscoveryHoverText", out value) && bool.TryParse(value, out flag))
            {
                showDiscoveryHoverText = flag;
            }

            if (values.TryGetValue("discoveryLockedHoverText", out value))
            {
                discoveryLockedHoverText = value;
                discoveryLockedHoverTextField = value;
            }

            if (values.TryGetValue("discoveryRemoteHoverText", out value))
            {
                discoveryRemoteHoverText = value;
                discoveryRemoteHoverTextField = value;
            }

            if (values.TryGetValue("discoveryUnlockedHoverText", out value))
            {
                discoveryUnlockedHoverText = value;
                discoveryUnlockedHoverTextField = value;
            }

            if (values.TryGetValue("discoveryLockedMessage", out value))
            {
                discoveryLockedMessage = value;
                discoveryLockedMessageField = value;
            }

            if (values.TryGetValue("discoveryUnlockMessage", out value))
            {
                discoveryUnlockMessage = value;
                discoveryUnlockMessageField = value;
            }

            if (values.TryGetValue("discoveryShowWorldMarker", out value) && bool.TryParse(value, out flag))
            {
                discoveryShowWorldMarker = flag;
            }

            if (values.TryGetValue("discoveryTintPortalModel", out value) && bool.TryParse(value, out flag))
            {
                discoveryTintPortalModel = flag;
            }

            if (values.TryGetValue("discoverySuppressLockedGlow", out value) && bool.TryParse(value, out flag))
            {
                discoverySuppressLockedGlow = flag;
            }

            if (values.TryGetValue("discoveryLockedColor", out value))
            {
                discoveryLockedColor = value;
                discoveryLockedColorField = value;
            }

            if (values.TryGetValue("discoveryRemoteColor", out value))
            {
                discoveryRemoteColor = value;
                discoveryRemoteColorField = value;
            }

            if (values.TryGetValue("discoveryUnlockedColor", out value))
            {
                discoveryUnlockedColor = value;
                discoveryUnlockedColorField = value;
            }

            if (values.TryGetValue("message", out value) && !string.IsNullOrWhiteSpace(value))
            {
                statusMessage = value;
                ShowMessage(value);
            }
        }

        private void ApplyUnlockPayload(string payload)
        {
            Dictionary<string, string> values = ParsePayload(payload);
            unlockedDiscoveryLinks.Clear();
            string linksText;
            if (values.TryGetValue("links", out linksText))
            {
                foreach (string raw in linksText.Split(';'))
                {
                    string link = raw.Trim();
                    if (!string.IsNullOrWhiteSpace(link))
                    {
                        unlockedDiscoveryLinks.Add(link);
                    }
                }
            }

            string message;
            if (values.TryGetValue("message", out message) && !string.IsNullOrWhiteSpace(message))
            {
                ShowMessage(message);
            }
        }

        private void MarkSelectedDiscoveryPortal(int role)
        {
            TeleportWorld portal = GetSelectedPortal();
            if (portal == null)
            {
                statusMessage = "No portal selected. Look at a portal and click Select first.";
                return;
            }

            ZNetView nview = portal.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
            {
                statusMessage = "That portal has no valid network view.";
                return;
            }

            string portalTag = discoveryLinkText.Trim();
            if (role != DiscoveryRoleNone && string.IsNullOrWhiteSpace(portalTag))
            {
                statusMessage = "Enter a portal tag first.";
                return;
            }

            discoveryLinkText = portalTag;
            SetPortalTag(portal, portalTag);

            string link = role == DiscoveryRoleNone ? string.Empty : portalTag;
            selectedPortalColorIndex = Mathf.Clamp(selectedPortalColorIndex, 0, MarkerColorValues.Length - 1);
            string markerColor = role == DiscoveryRoleNone ? string.Empty : MarkerColorValues[selectedPortalColorIndex];
            selectedPortalColorText = string.IsNullOrWhiteSpace(markerColor) ? MarkerColorValues[0] : markerColor;
            selectedPortalMarkerTextIndex = Mathf.Clamp(selectedPortalMarkerTextIndex, 0, MarkerTextValues.Length - 1);
            string markerText = role == DiscoveryRoleNone ? string.Empty : MarkerTextValues[selectedPortalMarkerTextIndex];
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcDiscoveryMarkRequest, nview.GetZDO().m_uid, role, link, markerColor, markerText);
            ApplyLocalPortalMarker(portal, role, link, markerColor, markerText);
            statusMessage = "Portal settings saved.";
            Log.LogInfo("Sent discovery portal marker request role=" + role + " tag='" + portalTag + "' portal=" + nview.GetZDO().m_uid);
        }

        private void ReadSelectedPortalSettings()
        {
            TeleportWorld portal = GetSelectedPortal();
            if (portal == null)
            {
                statusMessage = "No portal selected. Look at a portal and click Select first.";
                return;
            }

            LoadPortalSettings(portal, true);
        }

        private void SelectPortalForEditing(TeleportWorld portal, bool showStatus)
        {
            if (portal == null)
            {
                if (showStatus)
                {
                    statusMessage = "Look directly at a portal first.";
                }

                return;
            }

            selectedPortal = portal;
            LoadPortalSettings(portal, showStatus);
        }

        private TeleportWorld GetSelectedPortal()
        {
            if (selectedPortal != null)
            {
                ZNetView nview = selectedPortal.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                {
                    return selectedPortal;
                }
            }

            selectedPortal = null;
            loadedPortalKey = string.Empty;
            return null;
        }

        private void LoadPortalSettings(TeleportWorld portal, bool showStatus)
        {
            int role = GetDiscoveryRole(portal);
            selectedPortalRole = role == DiscoveryRoleLocked ? 1 : role == DiscoveryRoleRemote ? 2 : 0;
            string link = GetDiscoveryLink(portal);
            if (!string.IsNullOrWhiteSpace(link))
            {
                discoveryLinkText = link;
            }
            else
            {
                string tag = GetPortalTag(portal);
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    discoveryLinkText = tag;
                }
            }

            loadedPortalKey = GetPortalKey(portal);
            selectedPortalColorText = GetDiscoveryColor(portal);
            if (string.IsNullOrWhiteSpace(selectedPortalColorText))
            {
                selectedPortalColorText = MarkerColorValues[0];
            }

            selectedPortalColorIndex = GetMarkerColorIndex(selectedPortalColorText);
            selectedPortalMarkerTextIndex = GetMarkerTextIndex(GetDiscoveryMarkerText(portal));

            if (showStatus)
            {
                statusMessage = "Selected portal loaded. Choose what this portal does, then Apply.";
            }
        }

        private static void ApplyLocalPortalMarker(TeleportWorld portal, int role, string link, string markerColor, string markerText)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo == null)
            {
                return;
            }

            zdo.Set(DiscoveryRoleKey, role);
            zdo.Set(DiscoveryLinkKey, role == DiscoveryRoleNone ? string.Empty : link);
            zdo.Set(DiscoveryColorKey, role == DiscoveryRoleNone ? string.Empty : markerColor);
            zdo.Set(DiscoveryMarkerTextKey, role == DiscoveryRoleNone ? string.Empty : NormalizeMarkerTextValue(markerText));
        }

        private int GetSelectedPortalRoleValue()
        {
            if (selectedPortalRole == 1)
            {
                return DiscoveryRoleLocked;
            }

            if (selectedPortalRole == 2)
            {
                return DiscoveryRoleRemote;
            }

            return DiscoveryRoleNone;
        }

        private static string GetPortalRoleLabel(int role)
        {
            if (role == DiscoveryRoleLocked)
            {
                return "Locked entrance";
            }

            if (role == DiscoveryRoleRemote)
            {
                return "Unlocked entrance";
            }

            return "Normal portal";
        }

        private static TeleportWorld GetHoveredPortal()
        {
            if (Player.m_localPlayer == null)
            {
                return null;
            }

            GameObject hover = Player.m_localPlayer.GetHoverObject();
            if (hover == null)
            {
                return null;
            }

            TeleportWorld portal = hover.GetComponent<TeleportWorld>();
            if (portal == null)
            {
                portal = hover.GetComponentInParent<TeleportWorld>();
            }

            return portal;
        }

        private static string GetPortalKey(TeleportWorld portal)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? zdo.m_uid.ToString() : string.Empty;
        }

        private static string GetPortalTag(TeleportWorld portal)
        {
            MethodInfo method = AccessTools.Method(typeof(TeleportWorld), "GetText");
            if (portal != null && method != null)
            {
                object value = method.Invoke(portal, null);
                if (value != null)
                {
                    return value.ToString();
                }
            }

            return string.Empty;
        }

        private static string GetPortalTagForUi(TeleportWorld portal)
        {
            string link = GetDiscoveryLink(portal);
            if (!string.IsNullOrWhiteSpace(link))
            {
                return link;
            }

            return GetPortalTag(portal);
        }

        private static void SetPortalTag(TeleportWorld portal, string tag)
        {
            if (portal == null)
            {
                return;
            }

            MethodInfo method = AccessTools.Method(typeof(TeleportWorld), "SetText", new[] { typeof(string) });
            if (method != null)
            {
                method.Invoke(portal, new object[] { tag ?? string.Empty });
                return;
            }

            ZNetView nview = portal.GetComponent<ZNetView>();
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null)
            {
                zdo.Set("tag", tag ?? string.Empty);
            }
        }

        private static int GetDiscoveryRole(TeleportWorld portal)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? zdo.GetInt(DiscoveryRoleKey, DiscoveryRoleNone) : DiscoveryRoleNone;
        }

        private static string GetDiscoveryLink(TeleportWorld portal)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? zdo.GetString(DiscoveryLinkKey, string.Empty) : string.Empty;
        }

        private static string GetDiscoveryColor(TeleportWorld portal)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? zdo.GetString(DiscoveryColorKey, string.Empty) : string.Empty;
        }

        private static string GetDiscoveryMarkerText(TeleportWorld portal)
        {
            ZNetView nview = portal != null ? portal.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            return zdo != null ? NormalizeMarkerTextValue(zdo.GetString(DiscoveryMarkerTextKey, string.Empty)) : MarkerTextValues[0];
        }

        private Color GetDiscoveryVisualColor(TeleportWorld portal)
        {
            string portalColor = GetDiscoveryColor(portal);
            if (!string.IsNullOrWhiteSpace(portalColor))
            {
                return ParseHexColor(portalColor, LockedOrange);
            }

            int role = GetDiscoveryRole(portal);
            string link = GetDiscoveryLink(portal);
            if (role != DiscoveryRoleNone && !string.IsNullOrWhiteSpace(link) && IsDiscoveryUnlocked(link))
            {
                return ParseHexColor(discoveryUnlockedColor, Color.green);
            }

            if (role == DiscoveryRoleRemote)
            {
                return ParseHexColor(discoveryRemoteColor, Color.cyan);
            }

            return ParseHexColor(discoveryLockedColor, LockedOrange);
        }

        private bool ShouldShowDiscoveryWorldMarker(TeleportWorld portal)
        {
            return discoveryEnabled && discoveryShowWorldMarker && ShouldShowLockedEntranceX(portal);
        }

        private string GetDiscoveryMarkerDisplayText(TeleportWorld portal)
        {
            string value = GetDiscoveryMarkerText(portal);
            if (string.Equals(value, "Locked", StringComparison.OrdinalIgnoreCase))
            {
                return "LOCKED";
            }

            if (string.Equals(value, "X", StringComparison.OrdinalIgnoreCase))
            {
                return "X";
            }

            return "UNLOCK ON OTHER SIDE";
        }

        private bool ShouldShowLockedEntranceX(TeleportWorld portal)
        {
            if (GetDiscoveryRole(portal) != DiscoveryRoleLocked)
            {
                return false;
            }

            string link = GetDiscoveryLink(portal);
            return !string.IsNullOrWhiteSpace(link) && !IsDiscoveryUnlocked(link);
        }

        private bool ShouldTintDiscoveryPortal(TeleportWorld portal)
        {
            return false;
        }

        private bool ShouldSuppressLockedPortalGlow(TeleportWorld portal)
        {
            if (!discoveryEnabled || !discoverySuppressLockedGlow || GetDiscoveryRole(portal) != DiscoveryRoleLocked)
            {
                return false;
            }

            string link = GetDiscoveryLink(portal);
            return !string.IsNullOrWhiteSpace(link) && !IsDiscoveryUnlocked(link);
        }

        private static Color ParseHexColor(string text, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            string value = text.Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            int r;
            int g;
            int b;
            if (value.Length == 6 &&
                int.TryParse(value.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r) &&
                int.TryParse(value.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g) &&
                int.TryParse(value.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b))
            {
                return new Color(r / 255f, g / 255f, b / 255f, 1f);
            }

            return fallback;
        }

        private static string NormalizeHexColor(string text, string fallback)
        {
            string value = (text ?? string.Empty).Trim();
            if (!value.StartsWith("#", StringComparison.Ordinal))
            {
                value = "#" + value;
            }

            Color ignored;
            return ColorUtility.TryParseHtmlString(value, out ignored) ? value : fallback;
        }

        private static int GetMarkerColorIndex(string color)
        {
            string normalized = NormalizeHexColor(color, MarkerColorValues[0]);
            for (int i = 0; i < MarkerColorValues.Length; i++)
            {
                if (string.Equals(normalized, MarkerColorValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private static string NormalizeMarkerTextValue(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            for (int i = 0; i < MarkerTextValues.Length; i++)
            {
                if (string.Equals(normalized, MarkerTextValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return MarkerTextValues[i];
                }
            }

            return MarkerTextValues[0];
        }

        private static int GetMarkerTextIndex(string value)
        {
            string normalized = NormalizeMarkerTextValue(value);
            for (int i = 0; i < MarkerTextValues.Length; i++)
            {
                if (string.Equals(normalized, MarkerTextValues[i], StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return 0;
        }

        private bool IsDiscoveryUnlocked(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            return unlockedDiscoveryLinks.Contains(link);
        }

        private void UnlockDiscoveryLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link))
            {
                return;
            }

            unlockedDiscoveryLinks.Add(link);
            long playerId = GetLocalPlayerId();
            if (playerId != 0L && ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(RpcDiscoveryUnlockRequest, playerId, link.Trim());
            }
        }

        private static bool ShouldBlockPortalUse(TeleportWorld portal)
        {
            if (Instance == null || !Instance.discoveryEnabled || Player.m_localPlayer == null)
            {
                return false;
            }

            int role = GetDiscoveryRole(portal);
            if (role == DiscoveryRoleNone)
            {
                return false;
            }

            string link = GetDiscoveryLink(portal);
            if (string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            if (Instance.discoveryAdminBypass && Instance.isAdmin)
            {
                return false;
            }

            if (role == DiscoveryRoleRemote)
            {
                if (!Instance.IsDiscoveryUnlocked(link))
                {
                    Instance.UnlockDiscoveryLink(link);
                    Instance.ShowMessage(Instance.discoveryUnlockMessage);
                }

                return false;
            }

            if (role == DiscoveryRoleLocked && !Instance.IsDiscoveryUnlocked(link))
            {
                Instance.ShowMessage(Instance.discoveryLockedMessage);
                Instance.RequestUnlockList();
                return true;
            }

            return false;
        }

        internal static bool WouldExceedPortalLimit(Piece piece)
        {
            PortalLimitClientPlugin plugin = Instance;
            if (plugin == null || piece == null || !plugin.IsLimitedPortal(piece))
            {
                return false;
            }

            if (plugin.maxPortalsPerPlayer <= 0)
            {
                return false;
            }

            if (plugin.adminBypass && plugin.isAdmin)
            {
                return false;
            }

            long playerId = GetLocalPlayerId();
            int count = plugin.CountPortals(playerId);
            return count >= plugin.maxPortalsPerPlayer;
        }

        private bool IsLimitedPortal(Piece piece)
        {
            if (piece == null || piece.GetComponent<TeleportWorld>() == null)
            {
                return false;
            }

            HashSet<string> names = GetPortalPrefabNameSet();
            if (names.Count == 0)
            {
                return true;
            }

            ZNetView nview = piece.GetComponent<ZNetView>();
            string prefabName = GetPrefabName(nview, piece);
            return names.Contains(prefabName);
        }

        private static string GetPrefabName(ZNetView nview, Piece piece)
        {
            if (nview != null && GetPrefabNameMethod != null)
            {
                object value = GetPrefabNameMethod.Invoke(nview, null);
                if (value != null)
                {
                    return value.ToString();
                }
            }

            return piece != null ? piece.name.Replace("(Clone)", string.Empty) : string.Empty;
        }

        private int CountPortals(long creatorId)
        {
            if (creatorId == 0L || AllPiecesField == null)
            {
                return serverPortalCount;
            }

            int count = 0;
            IEnumerable pieces = AllPiecesField.GetValue(null) as IEnumerable;
            if (pieces == null)
            {
                return serverPortalCount;
            }

            foreach (object item in pieces)
            {
                Piece piece = item as Piece;
                if (piece != null && piece.GetCreator() == creatorId && IsLimitedPortal(piece))
                {
                    count++;
                }
            }

            return count;
        }

        private HashSet<string> GetPortalPrefabNameSet()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in portalPrefabNames.Split(','))
            {
                string name = raw.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private static long GetLocalPlayerId()
        {
            try
            {
                if (Player.m_localPlayer != null && GetPlayerIdMethod != null)
                {
                    object value = GetPlayerIdMethod.Invoke(Player.m_localPlayer, null);
                    if (value != null && value.GetType() == typeof(long))
                    {
                        return (long)value;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not read local player ID: " + ex.Message);
            }

            return 0L;
        }

        private static string GetPlayerName()
        {
            return Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "unknown-player";
        }

        internal void ShowMessage(string message)
        {
            if (MessageHud.instance != null)
            {
                MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message);
            }
            else
            {
                Log.LogMessage(message);
            }
        }

        private static Dictionary<string, string> ParsePayload(string payload)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(payload))
            {
                return values;
            }

            foreach (string line in payload.Split('\n'))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                values[line.Substring(0, separator)] = Unescape(line.Substring(separator + 1));
            }

            return values;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\n", "\\n").Replace("=", "\\e");
        }

        private static string Unescape(string value)
        {
            return (value ?? string.Empty).Replace("\\e", "=").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        [HarmonyPatch(typeof(Chat), "InputText")]
        private static class ChatInputPatch
        {
            private static bool Prefix(Chat __instance)
            {
                string text = __instance.m_input != null ? __instance.m_input.text : string.Empty;
                if (string.Equals(text, "/portals", StringComparison.OrdinalIgnoreCase))
                {
                    if (Instance != null)
                    {
                        Instance.RequestCountForChat();
                    }

                    if (__instance.m_input != null)
                    {
                        __instance.m_input.text = string.Empty;
                    }

                    __instance.Hide();
                    return false;
                }

                if (!string.Equals(text, "/portallimit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (Instance != null)
                {
                    Instance.ToggleAdminWindow();
                }

                if (__instance.m_input != null)
                {
                    __instance.m_input.text = string.Empty;
                }

                __instance.Hide();
                return false;
            }
        }

        [HarmonyPatch(typeof(Player), "TryPlacePiece")]
        private static class PlacePiecePatch
        {
            private static bool Prefix(Piece piece, ref bool __result)
            {
                if (!WouldExceedPortalLimit(piece))
                {
                    return true;
                }

                __result = false;
                Instance.ShowMessage("Portal limit reached: " + Instance.maxPortalsPerPlayer + " portal(s) per player.");
                Instance.RequestCount();
                return false;
            }

            private static void Postfix(Piece piece, bool __result)
            {
            }
        }

        [HarmonyPatch(typeof(Piece), "OnPlaced")]
        private static class ClientPieceOnPlacedPatch
        {
            private static void Postfix(Piece __instance)
            {
                if (Instance == null || !Instance.isAdmin || !Instance.autoOpenAdminUiOnPortalPlacement.Value || __instance == null || !__instance.IsCreator())
                {
                    return;
                }

                TeleportWorld portal = __instance.GetComponent<TeleportWorld>();
                if (portal == null)
                {
                    return;
                }

                Instance.OpenAdminWindowAfterPortalPlacement(portal);
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "Interact")]
        private static class TeleportWorldInteractPatch
        {
            private static bool Prefix(TeleportWorld __instance, Humanoid human, bool hold, ref bool __result)
            {
                if (Instance == null || hold || Player.m_localPlayer == null || human != Player.m_localPlayer)
                {
                    return true;
                }

                if (Instance.isAdmin && Instance.openAdminUiWithPortalInteract.Value)
                {
                    Instance.OpenAdminWindowForPortal(__instance);
                    __result = false;
                    return false;
                }

                if (!Instance.discoveryEnabled)
                {
                    return true;
                }

                if (ShouldBlockPortalUse(__instance))
                {
                    __result = false;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "Teleport")]
        private static class TeleportWorldTeleportPatch
        {
            private static bool Prefix(TeleportWorld __instance, Player player)
            {
                if (Player.m_localPlayer == null || player != Player.m_localPlayer)
                {
                    return true;
                }

                return !ShouldBlockPortalUse(__instance);
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "GetHoverText")]
        private static class TeleportWorldHoverTextPatch
        {
            private static void Postfix(TeleportWorld __instance, ref string __result)
            {
                if (Instance == null || !Instance.discoveryEnabled || !Instance.showDiscoveryHoverText)
                {
                    return;
                }

                int role = GetDiscoveryRole(__instance);
                if (role == DiscoveryRoleNone)
                {
                    return;
                }

                string link = GetDiscoveryLink(__instance);
                if (string.IsNullOrWhiteSpace(link))
                {
                    return;
                }

                string extra = string.Empty;
                if (Instance.IsDiscoveryUnlocked(link) || (Instance.discoveryAdminBypass && Instance.isAdmin))
                {
                    extra = Instance.discoveryUnlockedHoverText;
                }
                else if (role == DiscoveryRoleLocked)
                {
                    extra = Instance.discoveryLockedHoverText;
                }
                else if (role == DiscoveryRoleRemote)
                {
                    extra = Instance.discoveryRemoteHoverText;
                }

                if (!string.IsNullOrWhiteSpace(extra))
                {
                    __result += "\n<color=orange>" + extra + "</color>";
                }
            }
        }

        [HarmonyPatch(typeof(TeleportWorld), "Update")]
        private static class TeleportWorldVisualPatch
        {
            private static void Postfix(TeleportWorld __instance)
            {
                if (__instance == null)
                {
                    return;
                }

                PortalLimitDiscoveryVisual visual = __instance.GetComponent<PortalLimitDiscoveryVisual>();
                if (visual == null)
                {
                    visual = __instance.gameObject.AddComponent<PortalLimitDiscoveryVisual>();
                }

                visual.Refresh(__instance);
            }
        }

        private class PortalLimitDiscoveryVisual : MonoBehaviour
        {
            private GameObject markerRoot;
            private TextMesh markerText;
            private Renderer[] portalRenderers;
            private ParticleSystem[] portalParticles;
            private Light[] portalLights;
            private readonly Dictionary<Renderer, Color> originalColors = new Dictionary<Renderer, Color>();
            private readonly Dictionary<ParticleSystem, bool> originalParticleStates = new Dictionary<ParticleSystem, bool>();
            private readonly Dictionary<Light, bool> originalLightStates = new Dictionary<Light, bool>();

            internal void Refresh(TeleportWorld portal)
            {
                if (PortalLimitClientPlugin.Instance == null || portal == null)
                {
                    return;
                }

                bool showMarker = PortalLimitClientPlugin.Instance.ShouldShowDiscoveryWorldMarker(portal);
                bool tintPortal = PortalLimitClientPlugin.Instance.ShouldTintDiscoveryPortal(portal);
                bool suppressGlow = PortalLimitClientPlugin.Instance.ShouldSuppressLockedPortalGlow(portal);
                Color color = PortalLimitClientPlugin.Instance.GetDiscoveryVisualColor(portal);

                EnsureMarker();
                markerRoot.SetActive(showMarker);
                if (showMarker)
                {
                    SetMarkerWorldPose(portal);
                    SetMarkerText(PortalLimitClientPlugin.Instance.GetDiscoveryMarkerDisplayText(portal));
                    SetMarkerColor(color);
                }

                UpdatePortalTint(tintPortal, color);
                UpdatePortalEffects(suppressGlow);
            }

            private void EnsureMarker()
            {
                if (markerRoot != null)
                {
                    return;
                }

                markerRoot = new GameObject("PortalLimit_DiscoveryMarker");
                markerRoot.transform.SetParent(transform, false);
                markerRoot.transform.localPosition = new Vector3(0f, 1.55f, 0f);
                markerRoot.transform.localRotation = Quaternion.identity;
                markerRoot.transform.localScale = Vector3.one;

                markerText = markerRoot.AddComponent<TextMesh>();
                markerText.text = "UNLOCK ON OTHER SIDE";
                markerText.anchor = TextAnchor.MiddleCenter;
                markerText.alignment = TextAlignment.Center;
                markerText.fontSize = 96;
                markerText.characterSize = 0.010f;
                markerText.richText = false;

                SetMarkerColor(LockedOrange);
                markerRoot.SetActive(false);
            }

            private void SetMarkerText(string text)
            {
                if (markerText == null)
                {
                    return;
                }

                markerText.text = text;
                if (string.Equals(text, "X", StringComparison.Ordinal))
                {
                    markerText.fontSize = 128;
                    markerText.characterSize = 0.030f;
                }
                else if (string.Equals(text, "LOCKED", StringComparison.Ordinal))
                {
                    markerText.fontSize = 96;
                    markerText.characterSize = 0.018f;
                }
                else
                {
                    markerText.fontSize = 96;
                    markerText.characterSize = 0.010f;
                }
            }

            private void SetMarkerColor(Color color)
            {
                if (markerText != null)
                {
                    markerText.color = color;
                }
            }

            private void SetMarkerWorldPose(TeleportWorld portal)
            {
                if (markerRoot == null || portal == null)
                {
                    return;
                }

                Vector3 center = portal.transform.position + Vector3.up * 1.55f;
                if (Camera.main != null)
                {
                    Vector3 towardCamera = Camera.main.transform.position - center;
                    if (towardCamera.sqrMagnitude > 0.001f)
                    {
                        markerRoot.transform.position = center + towardCamera.normalized * 0.45f;
                        markerRoot.transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);
                        return;
                    }
                }

                markerRoot.transform.position = center;
                markerRoot.transform.rotation = portal.transform.rotation;
            }

            private void UpdatePortalTint(bool enabled, Color color)
            {
                if (portalRenderers == null)
                {
                    portalRenderers = GetComponentsInChildren<Renderer>(true);
                }

                foreach (Renderer renderer in portalRenderers)
                {
                    if (renderer == null || renderer.transform.IsChildOf(markerRoot.transform))
                    {
                        continue;
                    }

                    if (!originalColors.ContainsKey(renderer))
                    {
                        originalColors[renderer] = renderer.material.HasProperty("_Color") ? renderer.material.color : Color.white;
                    }

                    if (renderer.material.HasProperty("_Color"))
                    {
                        renderer.material.color = enabled ? Color.Lerp(originalColors[renderer], color, 0.45f) : originalColors[renderer];
                    }
                }
            }

            private void UpdatePortalEffects(bool suppress)
            {
                if (portalParticles == null)
                {
                    portalParticles = GetComponentsInChildren<ParticleSystem>(true);
                    foreach (ParticleSystem particle in portalParticles)
                    {
                        if (particle != null)
                        {
                            originalParticleStates[particle] = particle.gameObject.activeSelf;
                        }
                    }
                }

                if (portalLights == null)
                {
                    portalLights = GetComponentsInChildren<Light>(true);
                    foreach (Light light in portalLights)
                    {
                        if (light != null)
                        {
                            originalLightStates[light] = light.enabled;
                        }
                    }
                }

                foreach (ParticleSystem particle in portalParticles)
                {
                    if (particle == null || particle.transform.IsChildOf(markerRoot.transform))
                    {
                        continue;
                    }

                    bool originalActive;
                    if (!originalParticleStates.TryGetValue(particle, out originalActive))
                    {
                        originalActive = true;
                    }

                    particle.gameObject.SetActive(suppress ? false : true);
                    if (!suppress && !particle.isPlaying)
                    {
                        particle.Play(true);
                    }
                }

                foreach (Light light in portalLights)
                {
                    if (light == null || light.transform.IsChildOf(markerRoot.transform))
                    {
                        continue;
                    }

                    bool originalEnabled;
                    if (!originalLightStates.TryGetValue(light, out originalEnabled))
                    {
                        originalEnabled = true;
                    }

                    light.enabled = !suppress;
                }
            }
        }
    }
}
