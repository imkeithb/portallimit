# Portal Limit and Discovery

Portal Limit and Discovery is a Valheim server mod for communities that want portals to stay useful without letting every player cover the world in unlimited fast travel.

The mod enforces a server-side portal limit per player, counts portals across the whole saved world including unloaded zones, and gives players a simple `/portals` command so they can check their own count. Admins can also create discovery-locked portal pairs where one entrance stays locked until each player finds and uses the matching unlock side first.

Portal Limit and Discovery is built for public dedicated servers. The server DLL handles enforcement and saved-world counting. The client DLL adds player feedback, portal visuals, the `/portals` command, and the admin UI.

All Portal Limit and Discovery commands are typed into regular in-game chat, not the F5/dev command panel.

## Main Features

- Server-enforced per-player portal limits.
- Strict global counts across loaded and unloaded world zones.
- Player `/portals` command to check current portal count.
- Admin-only in-game settings UI.
- Discovery-locked portal pairs for progression, exploration gates, events, and custom routes.
- Admin-readable portal count export for server review.
- AzuAntiCheat-friendly package layout.

## Discovery Portals

Discovery portals let admins create a pair where one side is locked until a player discovers the other side first.

Example use cases:

- Lock a shortcut until players find the far-side entrance.
- Create dungeon, boss, biome, or event progression routes.
- Add travel rewards without permanently disabling normal portals.
- Make exploration portals feel earned instead of immediately available.

To create a discovery-locked portal pair:

1. Look at the locked-side portal.
2. Open the Portal Limit and Discovery admin UI.
3. Set a shared portal tag, for example `Bog Witch`.
4. Mark that portal as `LOCKED ENTRANCE`.
5. Choose the marker color and marker text.
6. Apply the settings.

Then configure the unlock-side portal:

1. Look at the other portal.
2. Use the same portal tag.
3. Mark it as `UNLOCKED ENTRANCE`.
4. Apply the settings.

Each player unlocks the route individually after using the unlocked entrance.

## Player Commands

Players can type this into regular in-game chat:

```text
/portals
```

This shows their current counted portal total and the server limit.

## Admin Usage

Admins can type this into regular in-game chat to open the Portal Limit and Discovery UI:

```text
/portallimit
```

Admins can also press `F10`, or look at a portal and press `E` if that option is enabled.

Non-admin players cannot open or use the admin editor.

## Install

Install the Thunderstore package on both the dedicated server and players.

The package includes:

- `PortalLimitServer.dll`: runs only on dedicated servers.
- `PortalLimitClient.dll`: runs only on player clients.
- `BepInEx/config/AzuAntiCheat_Whitelist/PortalLimitClient.dll`: convenience copy for servers using AzuAntiCheat.

If your server uses AzuAntiCheat, whitelist the exact `PortalLimitClient.dll` that players install. The Thunderstore package already includes a matching copy at:

```text
BepInEx/config/AzuAntiCheat_Whitelist/PortalLimitClient.dll
```

## Existing Servers

Back up your world before installing.

Existing portals are not automatically deleted just because a player is already over the limit. The global limit applies when new portals are placed. Players who are already over the limit will need to remove portals before they can place more.

For a live server, consider installing with a high temporary limit first, confirming everything works, warning players, and then lowering the limit.

## More Information

The downloaded package includes `FULL_README.md` with config settings, server storage paths, manual install notes, and admin audit file details.

## Thunderstore Package Contents

This Thunderstore package contains the files needed by both public dedicated servers and players:

```text
manifest.json
README.md
FULL_README.md
CHANGELOG.md
icon.png
BepInEx/plugins/PortalLimitClient.dll
BepInEx/plugins/PortalLimitServer.dll
BepInEx/config/AzuAntiCheat_Whitelist/PortalLimitClient.dll
```

## Contact

Join the Discord for questions, bug reports, balance feedback, or suggestions:

https://discord.gg/3Ewv8v2D
