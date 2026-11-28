# Builds the release package: dist\PoE1Access (folder) and dist\PoE1Access.zip.
#
#   PoE1Access\
#     PoE1Access.exe        the launcher (self-contained, one file)
#     README.txt
#     app\plugin\           the ready-built plugin, installed into ExileApi at each start
#     app\reader\           reads the login and character screens before ExileApi starts
#
# ExileApi itself is not included: the launcher downloads it from GitHub on first run.
#
# Usage:  powershell -ExecutionPolicy Bypass -File build.ps1 -ExileApi "C:\path\to\ExileApi-Compiled-master"
# The ExileApi folder is only used to compile the plugin against its ExileCore.dll.
param(
    [Parameter(Mandatory = $true)][string]$ExileApi
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist\PoE1Access"
# Temporary build files go to a short path (deep paths break Windows tools).
$work = Join-Path ([IO.Path]::GetTempPath()) "poe1access-build"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue

if (-not (Test-Path (Join-Path $ExileApi "ExileCore.dll"))) { throw "ExileCore.dll not found in $ExileApi" }

Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dist, $work | Out-Null

# 1. Plugin (built from a copy, so no build output lands in the source folder)
$pluginSrc = Join-Path $work "plugin-src"
Copy-Item (Join-Path $root "PoE1Access") $pluginSrc -Recurse
dotnet build (Join-Path $pluginSrc "PoE1Access.csproj") -c Release "-p:exapiPackage=$ExileApi" "-p:OutDir=$work\plugin-out\" -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed" }
$pluginOut = Join-Path $dist "app\plugin"
New-Item -ItemType Directory -Force $pluginOut | Out-Null
Copy-Item (Join-Path $work "plugin-out\PoE1Access.dll"), (Join-Path $work "plugin-out\PoE1Access.deps.json") $pluginOut

# 2. Launcher (self-contained single file)
dotnet publish (Join-Path $root "Launcher\Launcher.csproj") -c Release -o (Join-Path $work "launcher-out") -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Launcher build failed" }
Copy-Item (Join-Path $work "launcher-out\PoE1AccessLauncher.exe") (Join-Path $dist "PoE1Access.exe")

# 2b. Pre-game reader (small, runs on the installed .NET 10 Desktop Runtime)
dotnet publish (Join-Path $root "Reader\Reader.csproj") -c Release -o (Join-Path $dist "app\reader") -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Reader build failed" }
Remove-Item (Join-Path $dist "app\reader\*.pdb") -ErrorAction SilentlyContinue

# 2c. Path of Building helper: runs an installed Path of Building's calculations without its
#     window (its LuaJIT and its Lua files stay where Path of Building is installed).
dotnet publish (Join-Path $root "PoBCalc\PoBCalc.csproj") -c Release -o (Join-Path $dist "app\pobcalc") -nologo -v:q
if ($LASTEXITCODE -ne 0) { throw "Path of Building helper build failed" }
Remove-Item (Join-Path $dist "app\pobcalc\*.pdb") -ErrorAction SilentlyContinue

# 3. Prism (screen reader output, MPL-2.0): next to the launcher and in the plugin folder,
#    with its license and notice.
$prism = Join-Path $root "third_party\prism"
Copy-Item (Join-Path $prism "prism.dll") $dist
Copy-Item (Join-Path $prism "prism.dll") $pluginOut
$lic = Join-Path $dist "licenses\prism"
New-Item -ItemType Directory -Force $lic | Out-Null
Copy-Item (Join-Path $prism "NOTICE"), (Join-Path $prism "SOURCE.txt") $lic
Copy-Item (Join-Path $prism "LICENSES") $lic -Recurse

# 4. Version (the launcher's updater compares it with the newest GitHub release), readme, zip
$version = (Get-Content (Join-Path $root "VERSION") -TotalCount 1).Trim()
Set-Content (Join-Path $dist "app\version.txt") $version -Encoding ascii
Copy-Item (Join-Path $root "Launcher\README.txt") $dist
Copy-Item (Join-Path $root "LICENSE") (Join-Path $dist "LICENSE.txt")
Remove-Item -Recurse -Force $work
Compress-Archive -Path $dist -DestinationPath (Join-Path $root "dist\PoE1Access.zip") -Force
Get-ChildItem -Recurse (Join-Path $root "dist") | Select-Object FullName, Length | Format-Table -AutoSize | Out-String -Width 200
