@echo off
setlocal

set ROOT=%~dp0
set VERSION=0.1.3
set BUILD=%ROOT%build
set PACKAGE=%ROOT%package
set DIST=%ROOT%dist
set THUNDER=%DIST%\Thunderstore

call "%ROOT%build_plugins.bat"
if errorlevel 1 exit /b 1

if exist "%THUNDER%" rmdir /s /q "%THUNDER%"

mkdir "%THUNDER%\BepInEx\plugins"
mkdir "%THUNDER%\BepInEx\config\AzuAntiCheat_Whitelist"

copy /y "%PACKAGE%\manifest.json" "%THUNDER%\manifest.json" >nul
copy /y "%ROOT%README.md" "%THUNDER%\README.md" >nul
copy /y "%ROOT%FULL_README.md" "%THUNDER%\FULL_README.md" >nul
copy /y "%PACKAGE%\CHANGELOG.md" "%THUNDER%\CHANGELOG.md" >nul
copy /y "%PACKAGE%\icon.png" "%THUNDER%\icon.png" >nul
copy /y "%BUILD%\PortalLimitClient.dll" "%THUNDER%\BepInEx\plugins\PortalLimitClient.dll" >nul
copy /y "%BUILD%\PortalLimitServer.dll" "%THUNDER%\BepInEx\plugins\PortalLimitServer.dll" >nul
copy /y "%BUILD%\PortalLimitClient.dll" "%THUNDER%\BepInEx\config\AzuAntiCheat_Whitelist\PortalLimitClient.dll" >nul

if exist "%DIST%\PortalLimit-%VERSION%-Thunderstore.zip" del "%DIST%\PortalLimit-%VERSION%-Thunderstore.zip"

powershell -NoProfile -ExecutionPolicy Bypass -Command "Compress-Archive -Path '%THUNDER%\*' -DestinationPath '%DIST%\PortalLimit-%VERSION%-Thunderstore.zip' -Force"
if errorlevel 1 exit /b 1

echo.
echo Thunderstore package:
echo %DIST%\PortalLimit-%VERSION%-Thunderstore.zip
