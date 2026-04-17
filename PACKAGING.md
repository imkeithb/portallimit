# PortalLimit Packaging

These notes are for building the Thunderstore release zip. They are not included in the public package.

## Build Package

Run:

```bat
mods\PortalLimit\package_thunderstore.bat
```

This builds the DLLs and creates one upload file:

```text
mods\PortalLimit\dist\PortalLimit-0.1.2-Thunderstore.zip
```

## Thunderstore Package

Upload this file to Thunderstore:

```text
PortalLimit-0.1.2-Thunderstore.zip
```

It contains:

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

## Versioning

When releasing a new version, update these together:

- `PortalLimitClient.cs` plugin version
- `PortalLimitServer.cs` plugin version
- `package/manifest.json` `version_number`
- zip names in `package_thunderstore.bat`
