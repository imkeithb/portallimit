# Changelog

## 0.1.3

- Restored portal particles and lights when a locked portal becomes unlocked or is reset to normal.
- Prevented multiple locked or unlocked discovery endpoints from using the same portal tag.
- Clarified that `/portals` and `/portallimit` must be typed into regular in-game chat, not the F5/dev command panel.
- Replaced the README email contact with the Discord invite link.

## 0.1.2

- Updated the public README title and description to Portal Limit and Discovery.
- Moved discovery portal information closer to the top of the Thunderstore page.
- Added `FULL_README.md` for downloaded config and server-owner details.
- Added contact information for questions and suggestions.
- Kept the Thunderstore package ID as `PortalLimit` so this uploads as an update to the existing package.

## 0.1.1

- Changed the default locked discovery portal text color to orange.
- Added the packaged icon image.
- Included both client and server DLLs in the Thunderstore package for public dedicated servers.
- Added server-side global portal counting across unloaded zones.
- Added `portal_counts.tsv` for admin-readable portal ownership/count review.
- Hardened server RPC permissions for public servers.
- Fixed admin UI saving for locked/unlock popup message fields.

## 0.1.0

- Added server-authoritative portal limits per player.
- Added `/portals` chat command so players can check their current portal count.
- Added admin UI for changing server settings in game.
- Added admin-marked discovery portals with locked and unlock entrances.
- Added `UNLOCK ON OTHER SIDE` marker with per-portal color choices.
- Added marker text choices for `UNLOCK ON OTHER SIDE`, `LOCKED`, and `X`.
- Added optional locked-portal glow suppression for clients with the plugin.
