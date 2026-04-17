@echo off
setlocal

set ROOT=%~dp0
set CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OUT=%ROOT%build
set GAME=C:\Users\keith\198.73.57.168_27037
set MANAGED=%GAME%\valheim_server_Data\Managed
set CORE=%GAME%\BepInEx\core

if not exist "%OUT%" mkdir "%OUT%"

set REFS=/reference:"%CORE%\BepInEx.dll" /reference:"%CORE%\0Harmony.dll" /reference:"%MANAGED%\Assembly-CSharp.dll" /reference:"%MANAGED%\assembly_valheim.dll" /reference:"%MANAGED%\assembly_utils.dll" /reference:"%MANAGED%\gui_framework.dll" /reference:"%MANAGED%\SoftReferenceableAssets.dll" /reference:"%MANAGED%\UnityEngine.dll" /reference:"%MANAGED%\UnityEngine.CoreModule.dll" /reference:"%MANAGED%\UnityEngine.InputLegacyModule.dll" /reference:"%MANAGED%\UnityEngine.IMGUIModule.dll" /reference:"%MANAGED%\UnityEngine.ParticleSystemModule.dll" /reference:"%MANAGED%\UnityEngine.PhysicsModule.dll" /reference:"%MANAGED%\UnityEngine.TextRenderingModule.dll" /reference:"%MANAGED%\UnityEngine.UI.dll" /reference:"%MANAGED%\UnityEngine.PhysicsModule.dll" /reference:"%MANAGED%\Unity.TextMeshPro.dll" /reference:"%MANAGED%\netstandard.dll"

echo Building server plugin...
"%CSC%" /nologo /target:library /out:"%OUT%\PortalLimitServer.dll" %REFS% "%ROOT%Server\PortalLimitServer.cs"
if errorlevel 1 exit /b 1

echo Building client plugin...
"%CSC%" /nologo /target:library /out:"%OUT%\PortalLimitClient.dll" %REFS% "%ROOT%Client\PortalLimitClient.cs"
if errorlevel 1 exit /b 1

echo Build complete. DLLs are in:
echo %OUT%
