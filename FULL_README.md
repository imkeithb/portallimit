# Portal Limit and Discovery Full Readme

Portal Limit and Discovery is a Valheim server mod for public dedicated servers that want portal limits, global portal counting, and optional discovery-locked portal routes.

This file contains the extra config and server-owner details that are intentionally kept out of the shorter Thunderstore page.

## Install

### Thunderstore

Install the Thunderstore package on both the dedicated server and players.

The package includes:

- `PortalLimitServer.dll`: runs only on dedicated servers.
- `PortalLimitClient.dll`: runs only on player clients.
- `BepInEx/config/AzuAntiCheat_Whitelist/PortalLimitClient.dll`: convenience copy for servers using AzuAntiCheat.

### Manual Dedicated Server Install

If installing manually, install:

- `PortalLimitServer.dll`

to:

```text
BepInEx/plugins/
```

### Manual Player/Admin Install

Install:

- `PortalLimitClient.dll`

to:

```text
BepInEx/plugins/
```

The server plugin enforces the portal limit. The client plugin adds the player `/portals` command, placement feedback, locked-portal visuals, hover text, and the admin UI.

All Portal Limit and Discovery commands are typed into regular in-game chat, not the F5/dev command panel.

### AzuAntiCheat

If your server uses AzuAntiCheat, whitelist the exact `PortalLimitClient.dll` that players install. The Thunderstore package already includes a copy at this path.

Copy it to:

```text
BepInEx/config/AzuAntiCheat_Whitelist/PortalLimitClient.dll
```

## Regular Player Experience

Players can build and use normal Valheim portals as usual until they reach the server portal limit.

Players can type this into regular in-game chat:

```text
/portals
```

to see their current portal count and the server limit.

Counts are calculated server-side from saved portal ZDOs, so portals in unloaded zones still count.

The server also writes an admin-readable count export at:

```text
BepInEx/config/PortalLimit/portal_counts.tsv
```

This report includes creator ID, saved creator name when available, portal count, and portal ZDO IDs. The Valheim world save remains the source of truth; the TSV is for audit/review.

If a portal is marked as a locked discovery portal, players must find and use the unlocked entrance before they can use the locked entrance.

## Admin Usage

Admins can type this into regular in-game chat to open the Portal Limit and Discovery UI:

```text
/portallimit
```

or press `F10`.

Admins can also look at a portal and press `E` to edit that portal's Portal Limit and Discovery settings.

The admin UI has two tabs:

- `Server Settings`: global portal limit and discovery portal behavior.
- `This Portal`: settings for the portal the admin is looking at.

## Discovery Portals

To create a discovery-locked portal pair:

1. Look at the locked-side portal.
2. Open `This Portal`.
3. Set the portal tag, for example `Bog Witch`.
4. Choose `LOCKED ENTRANCE`.
5. Choose marker color and marker text.
6. Click `Apply`.

Then configure the unlock-side portal:

1. Look at the unlock-side portal.
2. Use the same portal tag.
3. Choose `UNLOCKED ENTRANCE`.
4. Click `Apply`.

Each player unlocks the route individually after using the unlocked entrance.

## Config

The server creates:

```text
BepInEx/config/keith.valheim.portallimit.server.cfg
```

Important settings:

- `MaxPortalsPerPlayer`: maximum counted portals per player. `0` disables the limit.
- `AdminBypass`: lets Valheim admins exceed the portal limit.
- `EnforceOnServer`: removes newly placed portals that exceed the limit.
- `PortalPrefabNames`: comma-separated prefab names counted as portals. Leave empty to count every loaded prefab type that has a `TeleportWorld` component.
- `Discovery Portals.Enabled`: enables discovery-locked portal behavior.
- `Discovery Portals.ShowHoverText`: shows lock/unlock text on portals.
- `Discovery Portals.ShowWorldMarker`: shows the portal marker text.
- `Discovery Portals.SuppressLockedGlow`: hides connected portal glow while locked.

## Data Storage

Player discovery unlocks are stored on the server at:

```text
BepInEx/config/PortalLimit/discovery_unlocks.txt
```

Global portal counts are exported on the server at:

```text
BepInEx/config/PortalLimit/portal_counts.tsv
```

## Existing Servers

Back up your world before installing.

Existing portals are not automatically deleted just because a player is already over the limit. The global limit applies when new portals are placed. Players who are already over the limit will need to remove portals before they can place more.

For a live server, consider installing with a high temporary limit first, confirming everything works, then lowering the limit after players have been warned.

## Notes

- Normal portals work like normal Valheim portals unless an admin marks them as discovery-locked.
- Non-admin players cannot open or use the admin editor.
- Client and server DLLs should be updated together.

## Contact

Join the Discord for questions, bug reports, balance feedback, or suggestions:

https://discord.gg/3Ewv8v2D
