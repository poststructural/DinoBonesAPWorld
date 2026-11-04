<#
Dino Bones Archipelago: Windows installer (native loader, no BepInEx).

  powershell -ExecutionPolicy Bypass -File install_windows.ps1                 find the game in your Steam libraries
  powershell -ExecutionPolicy Bypass -File install_windows.ps1 -GamePath "D:\Games\DinoBones Tidy Up the Museum"
  powershell -ExecutionPolicy Bypass -File install_windows.ps1 -Uninstall

Install:
  - copies DinoBonesAP.dll and Mono.Cecil.dll into DinoBones_Data\Managed (taken from -SourceDir, this script's folder
    or ..\release; downloaded from the latest GitHub release if none has them)
  - backs up ScriptingAssemblies.json and RuntimeInitializeOnLoads.json (*.dinoap.bak, refreshed while vanilla)
  - adds DinoBonesAP.dll to ScriptingAssemblies.json and the DinoBonesAP.Plugin.Boot entry to RuntimeInitializeOnLoads.json
  - removes the old BepInEx plugin (BepInEx\plugins\DinoBonesAP.dll) if present; BepInEx itself is left alone
On the first game start the plugin saves DinoMuseum.dll and IndieSouls.Runtime.dll as *.dll.orig and replaces them with
woven builds (its hooks); the game then asks for one restart. The plugin re-weaves by itself after a game update.

Uninstall: removes our DLLs, puts the *.dll.orig game files back, and restores both JSON files.
Running the installer again is safe (it updates the DLLs and changes nothing else).
#>
param(
    [string]$GamePath = "",
    [string]$SourceDir = "",
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$ReleaseUrl = 'https://github.com/poststructural/DinoBonesAPWorld/releases/latest/download'
$OurDlls = @('DinoBonesAP.dll', 'Mono.Cecil.dll')
$Utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Say([string]$m) { Write-Host $m }
function Die([string]$m) { Write-Host "ERROR: $m" -ForegroundColor Red; exit 1 }

# ---- find the game ------------------------------------------------------------------------------
function Find-Game {
    $steam = $null
    foreach ($k in 'HKCU:\Software\Valve\Steam', 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam', 'HKLM:\SOFTWARE\Valve\Steam') {
        try {
            $p = Get-ItemProperty -Path $k -ErrorAction Stop
            if ($p.SteamPath) { $steam = $p.SteamPath; break }
            if ($p.InstallPath) { $steam = $p.InstallPath; break }
        } catch { }
    }
    if (-not $steam) { return $null }
    $libs = @($steam)
    $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if (Test-Path -LiteralPath $vdf) {
        foreach ($line in Get-Content -LiteralPath $vdf) {
            if ($line -match '^\s*"path"\s*"(.+)"\s*$') { $libs += ($Matches[1] -replace '\\\\', '\') }
        }
    }
    foreach ($lib in $libs) {
        $common = Join-Path $lib 'steamapps\common'
        if (-not (Test-Path -LiteralPath $common)) { continue }
        foreach ($d in Get-ChildItem -LiteralPath $common -Directory -ErrorAction SilentlyContinue) {
            if (Test-Path -LiteralPath (Join-Path $d.FullName 'DinoBones.exe')) { return $d.FullName }
        }
    }
    return $null
}

if (-not $GamePath) {
    $GamePath = Find-Game
    if (-not $GamePath) { Die 'Could not find DinoBones in your Steam libraries. Run again with -GamePath "<game folder>".' }
}
$GamePath = (Resolve-Path -LiteralPath $GamePath).Path.TrimEnd('\')
$Data = Join-Path $GamePath 'DinoBones_Data'
$Managed = Join-Path $Data 'Managed'
$SaPath = Join-Path $Data 'ScriptingAssemblies.json'
$RioPath = Join-Path $Data 'RuntimeInitializeOnLoads.json'
foreach ($p in $Managed, $SaPath, $RioPath) { if (-not (Test-Path -LiteralPath $p)) { Die "$p not found. Is $GamePath the DinoBones game folder (Windows Mono build)?" } }
if (-not (Test-Path -LiteralPath (Join-Path $Managed 'DinoMuseum.dll'))) { Die "DinoMuseum.dll not found in $Managed." }
Say "Game: $GamePath"

if (Get-Process -Name DinoBones -ErrorAction SilentlyContinue) { Die 'DinoBones is running. Quit the game first.' }

# ---- JSON helpers (keep UTF-8 without BOM, compact like Unity writes them) ----------------------
function Read-Json([string]$path) { return ([IO.File]::ReadAllText($path) | ConvertFrom-Json) }
function Write-Json([string]$path, $obj) {
    $text = $obj | ConvertTo-Json -Compress -Depth 10
    if ((Test-Path -LiteralPath $path) -and ([IO.File]::ReadAllText($path)).EndsWith("`n")) { $text += "`n" }
    $tmp = "$path.dinoap.tmp"
    [IO.File]::WriteAllText($tmp, $text, $Utf8NoBom)
    Copy-Item -LiteralPath $tmp -Destination $path -Force
    Remove-Item -LiteralPath $tmp -Force
}
# The plugin weaves these in place (DinoMuseum.dll -> woven build, original kept as DinoMuseum.dll.orig).
$WovenAsms = @('DinoMuseum', 'IndieSouls.Runtime')
function Is-Woven([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    return [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($path)).Contains('DinoBonesAP.Woven')
}
function Is-Original([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $false }
    $b = [IO.File]::ReadAllBytes($path)
    return ($b.Length -gt 2 -and $b[0] -eq 0x4D -and $b[1] -eq 0x5A -and -not (Is-Woven $path))
}
function Same-Json($a, $b) { return (($a | ConvertTo-Json -Compress -Depth 10) -eq ($b | ConvertTo-Json -Compress -Depth 10)) }

function Has-Ours-SA($j) { return @(@($j.names) | Where-Object { $_ -eq 'DinoBonesAP.dll' }).Count -gt 0 }
function Has-Ours-RIO($j) { return @(@($j.root) | Where-Object { $_.assemblyName -eq 'DinoBonesAP' }).Count -gt 0 }

function Clean-SA($j) {
    $names = @(); $types = @()
    $n = @($j.names); $t = @($j.types)
    for ($i = 0; $i -lt $n.Count; $i++) {
        if ($n[$i] -eq 'DinoBonesAP.dll') { continue }
        $names += $n[$i]
        $types += $t[$i]
    }
    $j.names = $names; $j.types = $types
    return $j
}
function Clean-RIO($j) {
    $j.root = @(@($j.root) | Where-Object { $_.assemblyName -ne 'DinoBonesAP' })
    return $j
}

function Backup-IfVanilla([string]$path, [bool]$hasOurs) {
    $bak = "$path.dinoap.bak"
    if (-not $hasOurs) { Copy-Item -LiteralPath $path -Destination $bak -Force; Say "Backed up $(Split-Path $path -Leaf)" }
    elseif (-not (Test-Path -LiteralPath $bak)) { Say "Warning: $(Split-Path $path -Leaf) already has our entries and no backup exists." }
}

function Remove-OldBepInExPlugin {
    foreach ($p in (Join-Path $GamePath 'BepInEx\plugins\DinoBonesAP.dll'), (Join-Path $GamePath 'BepInEx\plugins\DinoBonesAP')) {
        if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Recurse -Force; Say "Removed the old BepInEx plugin: $p" }
    }
}

# ---- uninstall ------------------------------------------------------------------------------------
if ($Uninstall) {
    # Check first, change nothing if a woven game DLL cannot be put back (the game would not start without our plugin).
    foreach ($asm in $WovenAsms) {
        $dll = Join-Path $Managed "$asm.dll"
        if ((Is-Woven $dll) -and -not (Is-Original "$dll.orig")) {
            Die "$asm.dll contains the Archipelago hooks but $asm.dll.orig is missing or not the original. Nothing was changed. Use Steam's 'Verify integrity of game files', then run -Uninstall again."
        }
    }
    foreach ($pair in @(@($SaPath, 'SA'), @($RioPath, 'RIO'))) {
        $path = $pair[0]; $kind = $pair[1]; $bak = "$path.dinoap.bak"
        $cur = Read-Json $path
        if ($kind -eq 'SA') { $clean = Clean-SA $cur } else { $clean = Clean-RIO $cur }
        if ((Test-Path -LiteralPath $bak) -and (Same-Json $clean (Read-Json $bak))) {
            Copy-Item -LiteralPath $bak -Destination $path -Force
            Say "Restored $(Split-Path $path -Leaf) from the backup"
        } else {
            Write-Json $path $clean
            if (Test-Path -LiteralPath $bak) { Say "Removed our entries from $(Split-Path $path -Leaf) (the backup differs, probably a game update; kept the current file)" }
            else { Say "Removed our entries from $(Split-Path $path -Leaf)" }
        }
        if (Test-Path -LiteralPath $bak) { Remove-Item -LiteralPath $bak -Force }
    }
    $gone = @()
    foreach ($f in $OurDlls) { $p = Join-Path $Managed $f; if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; $gone += $f } }
    foreach ($asm in $WovenAsms) {
        $dll = Join-Path $Managed "$asm.dll"; $orig = "$dll.orig"
        if (Is-Woven $dll) {
            Copy-Item -LiteralPath $orig -Destination $dll -Force; Say "Restored the original $asm.dll"
        }
        foreach ($p in $orig, "$orig.tmp", "$dll.tmp") { if (Test-Path -LiteralPath $p) { Remove-Item -LiteralPath $p -Force; $gone += (Split-Path $p -Leaf) } }
    }
    if ($gone.Count) { Say ("Removed: " + ($gone -join ', ')) }
    Remove-OldBepInExPlugin
    Say 'Uninstalled. The game is vanilla again (your normal save was never touched; AP saves stay in %USERPROFILE%\DinoBonesAP).'
    exit 0
}

# ---- install --------------------------------------------------------------------------------------
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$tmpDir = Join-Path ([IO.Path]::GetTempPath()) ('dinoap_' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpDir | Out-Null
try {
    $cands = @()
    if ($SourceDir) { $cands += $SourceDir }
    $cands += $here
    $cands += (Join-Path $here '..\release')
    $src = $null
    foreach ($c in $cands) {
        $ok = $true
        foreach ($f in $OurDlls) { if (-not (Test-Path -LiteralPath (Join-Path $c $f))) { $ok = $false } }
        # A DinoBonesAP.dll that references Harmony is the old BepInEx build (0.1.x): ignore it.
        if ($ok -and ([IO.File]::ReadAllText((Join-Path $c 'DinoBonesAP.dll'), [Text.Encoding]::ASCII)).Contains('0Harmony')) {
            Say "Ignoring the old BepInEx DinoBonesAP.dll in $c (0.1.x)."
            $ok = $false
        }
        if ($ok) { $src = (Resolve-Path -LiteralPath $c).Path; break }
    }
    if ($src) {
        Say "Using the DLLs in $src"
    } else {
        Say 'Downloading the latest DinoBonesAP.dll and Mono.Cecil.dll...'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        foreach ($f in $OurDlls) {
            try { Invoke-WebRequest -UseBasicParsing -Uri "$ReleaseUrl/$f" -OutFile (Join-Path $tmpDir $f) }
            catch { Die "Download of $f failed: $($_.Exception.Message)" }
        }
        $src = $tmpDir
    }

    $saJson = Read-Json $SaPath
    $rioJson = Read-Json $RioPath
    Backup-IfVanilla $SaPath (Has-Ours-SA $saJson)
    Backup-IfVanilla $RioPath (Has-Ours-RIO $rioJson)

    foreach ($f in $OurDlls) { Copy-Item -LiteralPath (Join-Path $src $f) -Destination (Join-Path $Managed $f) -Force }
    Say "Copied $($OurDlls -join ', ') into $Managed"

    if (@($saJson.names) -notcontains 'DinoBonesAP.dll') {
        $saJson.names = @($saJson.names) + 'DinoBonesAP.dll'
        $saJson.types = @($saJson.types) + 16
        Write-Json $SaPath $saJson
        Say 'Added DinoBonesAP.dll to ScriptingAssemblies.json'
    } else { Say 'ScriptingAssemblies.json already lists DinoBonesAP.dll' }

    if (-not (Has-Ours-RIO $rioJson)) {
        $entry = [pscustomobject][ordered]@{ assemblyName = 'DinoBonesAP'; nameSpace = 'DinoBonesAP'; className = 'Plugin'; methodName = 'Boot'; loadTypes = 1; isUnityClass = $false }
        $rioJson.root = @($rioJson.root) + $entry
        Write-Json $RioPath $rioJson
        Say 'Added the DinoBonesAP boot entry to RuntimeInitializeOnLoads.json'
    } else { Say 'RuntimeInitializeOnLoads.json already has the DinoBonesAP entry' }

    Remove-OldBepInExPlugin
    if (Test-Path -LiteralPath (Join-Path $GamePath 'version.dll')) {
        Say 'Note: BepInEx (version.dll) is still installed. It is no longer needed; the Archipelago plugin no longer uses it.'
    }
} finally {
    Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

Say ''
Say 'Done. Start the Dino Bones Client in the Archipelago Launcher, then start the game.'
Say 'The first start sets up the hooks and asks you to quit and start the game once more.'
Say 'Log: %USERPROFILE%\DinoBonesAP\plugin.log'
