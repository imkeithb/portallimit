using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace PortalLimit.Server
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class PortalLimitServerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "keith.valheim.portallimit.server";
        public const string PluginName = "Portal Limit and Discovery Server";
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

        internal static PortalLimitServerPlugin Instance;
        internal static ManualLogSource Log;

        private static readonly FieldInfo AllPiecesField = AccessTools.Field(typeof(Piece), "s_allPieces");
        private static readonly FieldInfo PeerSocketField = AccessTools.Field(typeof(ZNetPeer), "m_socket");
        private static readonly FieldInfo OnZdoDestroyedField = AccessTools.Field(typeof(ZDOMan), "m_onZDODestroyed");
        private static readonly FieldInfo NetScenePrefabsField = AccessTools.Field(typeof(ZNetScene), "m_prefabs");
        private static readonly MethodInfo GetPrefabNameMethod = AccessTools.Method(typeof(ZNetView), "GetPrefabName");

        private Harmony harmony;
        private bool rpcRegistered;
        private bool portalIndexBuilt;
        private bool zdoDestroyedHooked;
        private string dataDirectory;
        private string unlockFilePath;
        private string portalCountsFilePath;
        private readonly Dictionary<long, HashSet<string>> unlockedLinksByPlayer = new Dictionary<long, HashSet<string>>();
        private readonly Dictionary<long, HashSet<ZDOID>> portalIdsByCreator = new Dictionary<long, HashSet<ZDOID>>();
        private readonly Dictionary<ZDOID, long> creatorByPortalId = new Dictionary<ZDOID, long>();
        private readonly Dictionary<long, string> creatorNamesByCreator = new Dictionary<long, string>();

        private ConfigEntry<int> maxPortalsPerPlayer;
        private ConfigEntry<bool> adminBypass;
        private ConfigEntry<bool> enforceOnServer;
        private ConfigEntry<string> portalPrefabNames;
        private ConfigEntry<bool> discoveryPortalsEnabled;
        private ConfigEntry<bool> discoveryAdminBypass;
        private ConfigEntry<bool> showDiscoveryHoverText;
        private ConfigEntry<string> discoveryLockedHoverText;
        private ConfigEntry<string> discoveryRemoteHoverText;
        private ConfigEntry<string> discoveryUnlockedHoverText;
        private ConfigEntry<string> discoveryLockedMessage;
        private ConfigEntry<string> discoveryUnlockMessage;
        private ConfigEntry<bool> discoveryShowWorldMarker;
        private ConfigEntry<bool> discoveryTintPortalModel;
        private ConfigEntry<bool> discoverySuppressLockedGlow;
        private ConfigEntry<string> discoveryLockedColor;
        private ConfigEntry<string> discoveryRemoteColor;
        private ConfigEntry<string> discoveryUnlockedColor;

        private void Awake()
        {
            if (!IsDedicatedServerProcess())
            {
                Logger.LogInfo("Portal Limit Server disabled outside the dedicated server process.");
                enabled = false;
                return;
            }

            Instance = this;
            Log = Logger;

            maxPortalsPerPlayer = Config.Bind("General", "MaxPortalsPerPlayer", 4, "Maximum number of matching portals each player may own. Set to 0 or below to disable the limit.");
            adminBypass = Config.Bind("General", "AdminBypass", true, "Allow Valheim admins to exceed the portal limit.");
            enforceOnServer = Config.Bind("General", "EnforceOnServer", true, "Delete newly placed portals on the server when they exceed the limit. Keep this enabled for real enforcement.");
            portalPrefabNames = Config.Bind("General", "PortalPrefabNames", "portal_wood,portal_stone", "Comma-separated portal prefab names to limit. Leave empty to limit every piece with a TeleportWorld component.");
            discoveryPortalsEnabled = Config.Bind("Discovery Portals", "Enabled", true, "Enable per-player discovery-gated portals.");
            discoveryAdminBypass = Config.Bind("Discovery Portals", "AdminBypass", true, "Allow Valheim admins to use locked discovery portals without unlocking them.");
            showDiscoveryHoverText = Config.Bind("Discovery Portals", "ShowHoverText", true, "Show discovery portal status lines in portal hover text for clients with the plugin.");
            discoveryLockedHoverText = Config.Bind("Discovery Portals", "LockedHoverText", "Locked: find the other end first.", "Hover text shown for locked discovery portals before a player unlocks the link.");
            discoveryRemoteHoverText = Config.Bind("Discovery Portals", "RemoteHoverText", "Discovery portal: use this side to unlock the route.", "Hover text shown for the discovery side of a locked route.");
            discoveryUnlockedHoverText = Config.Bind("Discovery Portals", "UnlockedHoverText", "Discovery route unlocked.", "Hover text shown after the player unlocks the route.");
            discoveryLockedMessage = Config.Bind("Discovery Portals", "LockedMessage", "This portal is locked. Find and enter the other end first.", "Message shown when a player tries to use a locked discovery portal.");
            discoveryUnlockMessage = Config.Bind("Discovery Portals", "UnlockMessage", "Discovery portal unlocked.", "Message shown when a player unlocks a discovery portal route.");
            discoveryShowWorldMarker = Config.Bind("Discovery Portals", "ShowWorldMarker", true, "Show a visible marker on marked discovery portals for clients with the plugin.");
            discoveryTintPortalModel = Config.Bind("Discovery Portals", "TintPortalModel", false, "Tint the portal model color for clients with the plugin.");
            discoverySuppressLockedGlow = Config.Bind("Discovery Portals", "SuppressLockedGlow", true, "Hide connected portal glow and particle effects on locked portals before the route is unlocked.");
            discoveryLockedColor = Config.Bind("Discovery Portals", "LockedColor", "#ff9f1a", "Hex color for locked portals before the route is unlocked.");
            discoveryRemoteColor = Config.Bind("Discovery Portals", "RemoteColor", "#38a6ff", "Hex color for the far-side portal that unlocks the route.");
            discoveryUnlockedColor = Config.Bind("Discovery Portals", "UnlockedColor", "#42ff68", "Hex color for portals after the player has unlocked the route.");

            dataDirectory = Path.Combine(Paths.ConfigPath, "PortalLimit");
            unlockFilePath = Path.Combine(dataDirectory, "discovery_unlocks.txt");
            portalCountsFilePath = Path.Combine(dataDirectory, "portal_counts.tsv");
            Directory.CreateDirectory(dataDirectory);
            LoadUnlocks();

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

            UnhookZdoDestroyed();
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

            ZRoutedRpc.instance.Register<string>(RpcConfigRequest, OnConfigRequest);
            ZRoutedRpc.instance.Register<string>(RpcConfigUpdate, OnConfigUpdate);
            ZRoutedRpc.instance.Register<long>(RpcCountRequest, OnCountRequest);
            ZRoutedRpc.instance.Register<ZDOID, int, string, string, string>(RpcDiscoveryMarkRequest, OnDiscoveryMarkRequest);
            ZRoutedRpc.instance.Register<long, string>(RpcDiscoveryUnlockRequest, OnDiscoveryUnlockRequest);
            ZRoutedRpc.instance.Register<long>(RpcDiscoveryUnlockListRequest, OnDiscoveryUnlockListRequest);
            rpcRegistered = true;
            CancelInvoke("TryRegisterRpcs");
            HookZdoDestroyed();
            Log.LogInfo("Registered portal limit server RPC handlers.");
        }

        private void OnConfigRequest(long sender, string playerName)
        {
            bool silent = false;
            if (!string.IsNullOrEmpty(playerName) && playerName.StartsWith("[silent]", StringComparison.Ordinal))
            {
                silent = true;
                playerName = playerName.Substring("[silent]".Length);
            }

            bool isAdmin = IsSenderAdmin(sender);
            string message = silent ? string.Empty : (isAdmin ? "Portal limit settings loaded." : "Only admins can change portal limit settings.");
            string payload = BuildConfigPayload(isAdmin, message);
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, payload);
            Log.LogInfo("Config request from " + playerName + " admin=" + isAdmin);
        }

        private void OnConfigUpdate(long sender, string payload)
        {
            if (!IsSenderAdmin(sender))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(false, "Only admins can change portal limit settings."));
                return;
            }

            Dictionary<string, string> values = ParsePayload(payload);
            int max;
            bool flag;
            string value;

            if (values.TryGetValue("max", out value) && int.TryParse(value, out max))
            {
                maxPortalsPerPlayer.Value = Math.Max(0, max);
            }

            if (values.TryGetValue("adminBypass", out value) && bool.TryParse(value, out flag))
            {
                adminBypass.Value = flag;
            }

            if (values.TryGetValue("enforceOnServer", out value) && bool.TryParse(value, out flag))
            {
                enforceOnServer.Value = flag;
            }

            if (values.TryGetValue("portalPrefabNames", out value))
            {
                portalPrefabNames.Value = value.Trim();
                portalIndexBuilt = false;
            }

            if (values.TryGetValue("discoveryEnabled", out value) && bool.TryParse(value, out flag))
            {
                discoveryPortalsEnabled.Value = flag;
            }

            if (values.TryGetValue("discoveryAdminBypass", out value) && bool.TryParse(value, out flag))
            {
                discoveryAdminBypass.Value = flag;
            }

            if (values.TryGetValue("showDiscoveryHoverText", out value) && bool.TryParse(value, out flag))
            {
                showDiscoveryHoverText.Value = flag;
            }

            if (values.TryGetValue("discoveryLockedHoverText", out value))
            {
                discoveryLockedHoverText.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryRemoteHoverText", out value))
            {
                discoveryRemoteHoverText.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryUnlockedHoverText", out value))
            {
                discoveryUnlockedHoverText.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryLockedMessage", out value))
            {
                discoveryLockedMessage.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryUnlockMessage", out value))
            {
                discoveryUnlockMessage.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryShowWorldMarker", out value) && bool.TryParse(value, out flag))
            {
                discoveryShowWorldMarker.Value = flag;
            }

            if (values.TryGetValue("discoveryTintPortalModel", out value) && bool.TryParse(value, out flag))
            {
                discoveryTintPortalModel.Value = flag;
            }

            if (values.TryGetValue("discoverySuppressLockedGlow", out value) && bool.TryParse(value, out flag))
            {
                discoverySuppressLockedGlow.Value = flag;
            }

            if (values.TryGetValue("discoveryLockedColor", out value))
            {
                discoveryLockedColor.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryRemoteColor", out value))
            {
                discoveryRemoteColor.Value = value.Trim();
            }

            if (values.TryGetValue("discoveryUnlockedColor", out value))
            {
                discoveryUnlockedColor.Value = value.Trim();
            }

            Config.Save();
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, "Portal limit settings saved."));
            Log.LogInfo("Portal limit config updated by peer " + sender + ".");
        }

        private void OnCountRequest(long sender, long creatorId)
        {
            EnsurePortalIndexBuilt();
            bool isAdmin = IsSenderAdmin(sender);
            long effectiveCreatorId = isAdmin ? creatorId : sender;
            int count = CountPortals(effectiveCreatorId);
            int max = maxPortalsPerPlayer.Value;
            bool limitReached = max > 0 && count >= max;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcCountResponse, count, max, limitReached, BuildConfigPayload(isAdmin, string.Empty));
        }

        private void OnDiscoveryMarkRequest(long sender, ZDOID portalId, int role, string link, string markerColor, string markerText)
        {
            if (!IsSenderAdmin(sender))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(false, "Only admins can mark discovery portals."));
                return;
            }

            if (ZDOMan.instance == null || portalId.IsNone())
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, "No valid portal was targeted."));
                return;
            }

            ZDO zdo = ZDOMan.instance.GetZDO(portalId);
            if (zdo == null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, "Could not find that portal on the server."));
                return;
            }

            int normalizedRole = role == DiscoveryRoleLocked || role == DiscoveryRoleRemote ? role : DiscoveryRoleNone;
            string normalizedLink = (link ?? string.Empty).Trim();
            string normalizedColor = (markerColor ?? string.Empty).Trim();
            string normalizedMarkerText = NormalizeMarkerTextValue(markerText);
            if (normalizedRole != DiscoveryRoleNone && string.IsNullOrWhiteSpace(normalizedLink))
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, "Discovery portals need a link id."));
                return;
            }

            if (normalizedRole != DiscoveryRoleNone)
            {
                ZDOID conflictId;
                if (TryFindDiscoveryEndpointConflict(portalId, normalizedRole, normalizedLink, out conflictId))
                {
                    string roleName = normalizedRole == DiscoveryRoleLocked ? "locked entrance" : "unlocked entrance";
                    ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, "A " + roleName + " already exists for portal tag '" + normalizedLink + "'."));
                    Log.LogWarning("Rejected duplicate discovery " + roleName + " for link '" + normalizedLink + "' existing=" + conflictId + " attempted=" + portalId);
                    return;
                }
            }

            zdo.Set(DiscoveryRoleKey, normalizedRole);
            zdo.Set(DiscoveryLinkKey, normalizedRole == DiscoveryRoleNone ? string.Empty : normalizedLink);
            zdo.Set(DiscoveryColorKey, normalizedRole == DiscoveryRoleNone ? string.Empty : normalizedColor);
            zdo.Set(DiscoveryMarkerTextKey, normalizedRole == DiscoveryRoleNone ? string.Empty : normalizedMarkerText);
            ZDOMan.instance.ForceSendZDO(portalId);

            string message = normalizedRole == DiscoveryRoleNone ? "Discovery portal marker cleared." : "Discovery portal marker saved for link '" + normalizedLink + "'.";
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcConfigResponse, BuildConfigPayload(true, message));
            Log.LogInfo(message + " zdo=" + portalId);
        }

        private bool TryFindDiscoveryEndpointConflict(ZDOID currentPortalId, int role, string link, out ZDOID conflictId)
        {
            conflictId = ZDOID.None;
            if (ZDOMan.instance == null || string.IsNullOrWhiteSpace(link))
            {
                return false;
            }

            EnsurePortalIndexBuilt();
            foreach (ZDOID id in new List<ZDOID>(creatorByPortalId.Keys))
            {
                ZDO zdo = ZDOMan.instance.GetZDO(id);
                if (zdo == null || zdo.m_uid.Equals(currentPortalId))
                {
                    continue;
                }

                if (zdo.GetInt(DiscoveryRoleKey, DiscoveryRoleNone) != role)
                {
                    continue;
                }

                if (!string.Equals(zdo.GetString(DiscoveryLinkKey, string.Empty), link, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                conflictId = zdo.m_uid;
                return true;
            }

            return false;
        }

        private void OnDiscoveryUnlockRequest(long sender, long playerId, string link)
        {
            bool isAdmin = IsSenderAdmin(sender);
            long effectivePlayerId = isAdmin ? playerId : sender;
            if (!discoveryPortalsEnabled.Value || effectivePlayerId == 0L || string.IsNullOrWhiteSpace(link))
            {
                return;
            }

            string normalizedLink = link.Trim();
            HashSet<string> links = GetOrCreateUnlockSet(effectivePlayerId);
            bool added = links.Add(normalizedLink);
            if (added)
            {
                SaveUnlocks();
                Log.LogInfo("Unlocked discovery portal link '" + normalizedLink + "' for player " + effectivePlayerId + ".");
            }

            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcDiscoveryUnlockListResponse, BuildUnlockPayload(effectivePlayerId, added ? discoveryUnlockMessage.Value : string.Empty));
        }

        private void OnDiscoveryUnlockListRequest(long sender, long playerId)
        {
            bool isAdmin = IsSenderAdmin(sender);
            long effectivePlayerId = isAdmin ? playerId : sender;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcDiscoveryUnlockListResponse, BuildUnlockPayload(effectivePlayerId, string.Empty));
        }

        internal static void EnforceIfNeeded(Piece piece)
        {
            if (Instance == null || !Instance.enforceOnServer.Value || !IsDedicatedOrHostServer())
            {
                return;
            }

            if (!Instance.IsLimitedPortal(piece))
            {
                return;
            }

            long creatorId = piece.GetCreator();
            if (creatorId == 0L)
            {
                Instance.StartCoroutine(Instance.EnforceNextFrame(piece));
                return;
            }

            if (Instance.maxPortalsPerPlayer.Value <= 0)
            {
                return;
            }

            if (Instance.adminBypass.Value && Instance.IsCreatorAdmin(creatorId))
            {
                return;
            }

            Instance.EnsurePortalIndexBuilt();
            Instance.TrackPortal(piece, creatorId);
            int count = Instance.CountPortals(creatorId);
            if (count <= Instance.maxPortalsPerPlayer.Value)
            {
                Instance.SavePortalCounts();
                return;
            }

            Instance.StartCoroutine(Instance.RemoveExcessPortalNextFrame(piece, creatorId, count));
        }

        private IEnumerator EnforceNextFrame(Piece piece)
        {
            yield return null;

            if (piece == null)
            {
                yield break;
            }

            long creatorId = piece.GetCreator();
            if (creatorId == 0L)
            {
                Log.LogWarning("Could not enforce portal limit because placed portal has no creator id.");
                yield break;
            }

            EnforceIfNeeded(piece);
        }

        private IEnumerator RemoveExcessPortalNextFrame(Piece piece, long creatorId, int count)
        {
            yield return null;

            if (piece == null)
            {
                yield break;
            }

            Log.LogInfo("Removing portal from creator " + creatorId + " because they have " + count + "/" + maxPortalsPerPlayer.Value + ".");
            UntrackPortal(piece);
            RemovePiece(piece);
            if (ZRoutedRpc.instance != null)
            {
                ZRoutedRpc.instance.InvokeRoutedRPC(creatorId, RpcNotice, "Portal limit reached: " + maxPortalsPerPlayer.Value + " portal(s) per player.");
            }

            SavePortalCounts();
        }

        private void RemovePiece(Piece piece)
        {
            WearNTear wear = piece.GetComponent<WearNTear>();
            if (wear != null)
            {
                wear.Remove(true);
                return;
            }

            ZNetView nview = piece.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.Destroy();
            }
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
            if (creatorId == 0L)
            {
                return 0;
            }

            EnsurePortalIndexBuilt();
            HashSet<ZDOID> portalIds;
            return portalIdsByCreator.TryGetValue(creatorId, out portalIds) ? portalIds.Count : 0;
        }

        private void EnsurePortalIndexBuilt()
        {
            if (portalIndexBuilt || ZDOMan.instance == null)
            {
                return;
            }

            RebuildPortalIndex();
        }

        private void RebuildPortalIndex()
        {
            portalIdsByCreator.Clear();
            creatorByPortalId.Clear();
            creatorNamesByCreator.Clear();

            if (ZDOMan.instance == null)
            {
                return;
            }

            HashSet<string> names = GetStrictPortalPrefabNameSet();
            foreach (string prefabName in names)
            {
                int index = 0;
                List<ZDO> zdos = new List<ZDO>();
                while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefabName, zdos, ref index))
                {
                }

                foreach (ZDO zdo in zdos)
                {
                    TrackPortal(zdo);
                }
            }

            portalIndexBuilt = true;
            SavePortalCounts();
            Log.LogInfo("Built global portal index with " + creatorByPortalId.Count + " portal(s) across " + portalIdsByCreator.Count + " creator(s).");
        }

        private HashSet<string> GetStrictPortalPrefabNameSet()
        {
            HashSet<string> names = GetPortalPrefabNameSet();
            if (names.Count > 0)
            {
                return names;
            }

            if (ZNetScene.instance == null || NetScenePrefabsField == null)
            {
                return names;
            }

            IEnumerable prefabs = NetScenePrefabsField.GetValue(ZNetScene.instance) as IEnumerable;
            if (prefabs == null)
            {
                return names;
            }

            foreach (object item in prefabs)
            {
                GameObject prefab = item as GameObject;
                if (prefab != null && prefab.GetComponent<TeleportWorld>() != null)
                {
                    names.Add(prefab.name.Replace("(Clone)", string.Empty));
                }
            }

            return names;
        }

        private void TrackPortal(Piece piece, long creatorId)
        {
            ZNetView nview = piece != null ? piece.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            TrackPortal(zdo, creatorId);
        }

        private void TrackPortal(ZDO zdo)
        {
            TrackPortal(zdo, zdo != null ? zdo.GetLong(ZDOVars.s_creator, 0L) : 0L);
        }

        private void TrackPortal(ZDO zdo, long creatorId)
        {
            if (zdo == null || creatorId == 0L)
            {
                return;
            }

            ZDOID id = zdo.m_uid;
            string creatorName = zdo.GetString(ZDOVars.s_creatorName, string.Empty);
            long oldCreatorId;
            if (creatorByPortalId.TryGetValue(id, out oldCreatorId) && oldCreatorId != creatorId)
            {
                RemovePortalIdFromCreator(oldCreatorId, id);
            }

            creatorByPortalId[id] = creatorId;
            if (!string.IsNullOrWhiteSpace(creatorName))
            {
                creatorNamesByCreator[creatorId] = creatorName;
            }

            HashSet<ZDOID> portalIds;
            if (!portalIdsByCreator.TryGetValue(creatorId, out portalIds))
            {
                portalIds = new HashSet<ZDOID>();
                portalIdsByCreator[creatorId] = portalIds;
            }

            portalIds.Add(id);
        }

        private void UntrackPortal(Piece piece)
        {
            ZNetView nview = piece != null ? piece.GetComponent<ZNetView>() : null;
            ZDO zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
            if (zdo != null)
            {
                UntrackPortal(zdo.m_uid);
            }
        }

        private void UntrackPortal(ZDOID id)
        {
            long creatorId;
            if (!creatorByPortalId.TryGetValue(id, out creatorId))
            {
                return;
            }

            creatorByPortalId.Remove(id);
            RemovePortalIdFromCreator(creatorId, id);
        }

        private void RemovePortalIdFromCreator(long creatorId, ZDOID id)
        {
            HashSet<ZDOID> portalIds;
            if (!portalIdsByCreator.TryGetValue(creatorId, out portalIds))
            {
                return;
            }

            portalIds.Remove(id);
            if (portalIds.Count == 0)
            {
                portalIdsByCreator.Remove(creatorId);
                creatorNamesByCreator.Remove(creatorId);
            }
        }

        private void HookZdoDestroyed()
        {
            if (zdoDestroyedHooked || ZDOMan.instance == null || OnZdoDestroyedField == null)
            {
                return;
            }

            Action<ZDO> current = OnZdoDestroyedField.GetValue(ZDOMan.instance) as Action<ZDO>;
            current = (Action<ZDO>)Delegate.Combine(current, new Action<ZDO>(OnZdoDestroyed));
            OnZdoDestroyedField.SetValue(ZDOMan.instance, current);
            zdoDestroyedHooked = true;
        }

        private void UnhookZdoDestroyed()
        {
            if (!zdoDestroyedHooked || ZDOMan.instance == null || OnZdoDestroyedField == null)
            {
                return;
            }

            Action<ZDO> current = OnZdoDestroyedField.GetValue(ZDOMan.instance) as Action<ZDO>;
            current = (Action<ZDO>)Delegate.Remove(current, new Action<ZDO>(OnZdoDestroyed));
            OnZdoDestroyedField.SetValue(ZDOMan.instance, current);
            zdoDestroyedHooked = false;
        }

        private void OnZdoDestroyed(ZDO zdo)
        {
            if (zdo != null)
            {
                UntrackPortal(zdo.m_uid);
                SavePortalCounts();
            }
        }

        private HashSet<string> GetPortalPrefabNameSet()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in portalPrefabNames.Value.Split(','))
            {
                string name = raw.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names;
        }

        private bool IsCreatorAdmin(long creatorId)
        {
            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return false;
            }

            ZNetPeer peer = znet.GetPeer(creatorId);
            return peer != null && IsPeerAdmin(peer);
        }

        private bool IsSenderAdmin(long sender)
        {
            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                return false;
            }

            ZNetPeer peer = znet.GetPeer(sender);
            return peer != null && IsPeerAdmin(peer);
        }

        private bool IsPeerAdmin(ZNetPeer peer)
        {
            try
            {
                ISocket socket = PeerSocketField != null ? PeerSocketField.GetValue(peer) as ISocket : null;
                string hostName = socket != null ? socket.GetHostName() : string.Empty;
                return !string.IsNullOrWhiteSpace(hostName) && ZNet.instance != null && ZNet.instance.IsAdmin(hostName);
            }
            catch (Exception ex)
            {
                Log.LogWarning("Could not check admin status: " + ex.Message);
                return false;
            }
        }

        private string BuildConfigPayload(bool isAdmin, string message)
        {
            return "isAdmin=" + isAdmin +
                   "\nmax=" + maxPortalsPerPlayer.Value +
                   "\nadminBypass=" + adminBypass.Value +
                   "\nenforceOnServer=" + enforceOnServer.Value +
                   "\nportalPrefabNames=" + Escape(portalPrefabNames.Value) +
                   "\ndiscoveryEnabled=" + discoveryPortalsEnabled.Value +
                   "\ndiscoveryAdminBypass=" + discoveryAdminBypass.Value +
                   "\nshowDiscoveryHoverText=" + showDiscoveryHoverText.Value +
                   "\ndiscoveryLockedHoverText=" + Escape(discoveryLockedHoverText.Value) +
                   "\ndiscoveryRemoteHoverText=" + Escape(discoveryRemoteHoverText.Value) +
                   "\ndiscoveryUnlockedHoverText=" + Escape(discoveryUnlockedHoverText.Value) +
                   "\ndiscoveryLockedMessage=" + Escape(discoveryLockedMessage.Value) +
                   "\ndiscoveryUnlockMessage=" + Escape(discoveryUnlockMessage.Value) +
                   "\ndiscoveryShowWorldMarker=" + discoveryShowWorldMarker.Value +
                   "\ndiscoveryTintPortalModel=" + discoveryTintPortalModel.Value +
                   "\ndiscoverySuppressLockedGlow=" + discoverySuppressLockedGlow.Value +
                   "\ndiscoveryLockedColor=" + Escape(discoveryLockedColor.Value) +
                   "\ndiscoveryRemoteColor=" + Escape(discoveryRemoteColor.Value) +
                   "\ndiscoveryUnlockedColor=" + Escape(discoveryUnlockedColor.Value) +
                   "\nmessage=" + Escape(message);
        }

        private string BuildUnlockPayload(long playerId, string message)
        {
            HashSet<string> links = GetOrCreateUnlockSet(playerId);
            return "links=" + Escape(string.Join(";", new List<string>(links).ToArray())) +
                   "\nmessage=" + Escape(message);
        }

        private HashSet<string> GetOrCreateUnlockSet(long playerId)
        {
            HashSet<string> links;
            if (!unlockedLinksByPlayer.TryGetValue(playerId, out links))
            {
                links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                unlockedLinksByPlayer[playerId] = links;
            }

            return links;
        }

        private void LoadUnlocks()
        {
            unlockedLinksByPlayer.Clear();
            if (!File.Exists(unlockFilePath))
            {
                return;
            }

            foreach (string rawLine in File.ReadAllLines(unlockFilePath))
            {
                if (string.IsNullOrWhiteSpace(rawLine) || rawLine.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = rawLine.Split('|');
                if (parts.Length < 2)
                {
                    continue;
                }

                long playerId;
                if (!long.TryParse(parts[0].Trim(), out playerId))
                {
                    continue;
                }

                string link = parts[1].Trim();
                if (!string.IsNullOrWhiteSpace(link))
                {
                    GetOrCreateUnlockSet(playerId).Add(link);
                }
            }
        }

        private void SaveUnlocks()
        {
            List<string> lines = new List<string>();
            lines.Add("# player id | discovery link id");
            foreach (KeyValuePair<long, HashSet<string>> entry in unlockedLinksByPlayer)
            {
                foreach (string link in entry.Value)
                {
                    lines.Add(entry.Key + " | " + link);
                }
            }

            File.WriteAllLines(unlockFilePath, lines.ToArray());
        }

        private void SavePortalCounts()
        {
            if (string.IsNullOrEmpty(portalCountsFilePath))
            {
                return;
            }

            List<string> lines = new List<string>();
            lines.Add("# PortalLimit global portal count export");
            lines.Add("# Generated from saved Valheim portal ZDO creator data. The world save remains authoritative.");
            lines.Add("creatorId\tcreatorName\tportalCount\tportalZdoIds");

            List<long> creatorIds = new List<long>(portalIdsByCreator.Keys);
            creatorIds.Sort();
            foreach (long creatorId in creatorIds)
            {
                HashSet<ZDOID> portalIds;
                if (!portalIdsByCreator.TryGetValue(creatorId, out portalIds))
                {
                    continue;
                }

                List<string> idTexts = new List<string>();
                foreach (ZDOID id in portalIds)
                {
                    idTexts.Add(id.ToString());
                }

                idTexts.Sort(StringComparer.Ordinal);
                string creatorName;
                creatorNamesByCreator.TryGetValue(creatorId, out creatorName);
                lines.Add(creatorId + "\t" + EscapeReportField(creatorName) + "\t" + portalIds.Count + "\t" + EscapeReportField(string.Join(",", idTexts.ToArray())));
            }

            File.WriteAllLines(portalCountsFilePath, lines.ToArray());
        }

        private static string EscapeReportField(string value)
        {
            return (value ?? string.Empty).Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
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

        private static string NormalizeMarkerTextValue(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            if (string.Equals(normalized, "Locked", StringComparison.OrdinalIgnoreCase))
            {
                return "Locked";
            }

            if (string.Equals(normalized, "X", StringComparison.OrdinalIgnoreCase))
            {
                return "X";
            }

            return "UnlockOnOtherSide";
        }

        private static string Unescape(string value)
        {
            return (value ?? string.Empty).Replace("\\e", "=").Replace("\\n", "\n").Replace("\\\\", "\\");
        }

        private static bool IsDedicatedOrHostServer()
        {
            return ZNet.instance != null && ZNet.instance.IsServer();
        }

        [HarmonyPatch(typeof(Piece), "OnPlaced")]
        private static class PieceOnPlacedPatch
        {
            private static void Postfix(Piece __instance)
            {
                EnforceIfNeeded(__instance);
            }
        }
    }
}
