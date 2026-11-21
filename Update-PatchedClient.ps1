[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$SourcePath,
    [string]$DistPath = (Join-Path $PSScriptRoot 'dist'),
    [ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedSourceHash
)

$ErrorActionPreference = 'Stop'
if ($ExpectedSourceHash -and [Console]::IsOutputRedirected) {
    [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
}

if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $sourceCandidates = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        $sourceCandidates.Add((Join-Path $env:LOCALAPPDATA 'AnimeSSS\animesss-desktop.exe'))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) {
        $sourceCandidates.Add((Join-Path $env:USERPROFILE 'AppData\Local\AnimeSSS\animesss-desktop.exe'))
    }
    $SourcePath = $sourceCandidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($SourcePath) -and $sourceCandidates.Count -gt 0) { $SourcePath = $sourceCandidates[0] }
}

function Convert-HexToBytes([string]$Hex) {
    $clean = $Hex -replace '\s', ''
    if (($clean.Length % 2) -ne 0) { throw 'Hex string has an odd length.' }
    $bytes = [byte[]]::new($clean.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($clean.Substring($i * 2, 2), 16)
    }
    return ,$bytes
}

function Get-PEInfo([byte[]]$Data) {
    if ($Data.Length -lt 0x100) { throw 'Source file is too small to be a PE executable.' }
    $peOffset = [BitConverter]::ToInt32($Data, 0x3c)
    if ($peOffset -lt 0x40 -or $peOffset + 24 -ge $Data.Length) { throw 'Invalid PE header offset.' }
    if ([Text.Encoding]::ASCII.GetString($Data, $peOffset, 4) -ne "PE`0`0") { throw 'PE signature is missing.' }

    $machine = [BitConverter]::ToUInt16($Data, $peOffset + 4)
    if ($machine -ne 0x8664) { throw ('Expected x64 PE; machine is 0x{0:x4}.' -f $machine) }
    $sectionCount = [BitConverter]::ToUInt16($Data, $peOffset + 6)
    $optionalSize = [BitConverter]::ToUInt16($Data, $peOffset + 20)
    $optional = $peOffset + 24
    if ([BitConverter]::ToUInt16($Data, $optional) -ne 0x20b) { throw 'Expected a PE32+ optional header.' }
    $imageBase = [BitConverter]::ToUInt64($Data, $optional + 24)
    $sectionTable = $optional + $optionalSize
    if ($sectionTable + ($sectionCount * 40) -gt $Data.Length) { throw 'PE section table extends past end of file.' }

    $sections = [System.Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $sectionCount; $i++) {
        $offset = $sectionTable + ($i * 40)
        $name = [Text.Encoding]::ASCII.GetString($Data, $offset, 8).Trim([char]0)
        $virtualSize = [BitConverter]::ToUInt32($Data, $offset + 8)
        $virtualAddress = [BitConverter]::ToUInt32($Data, $offset + 12)
        $rawSize = [BitConverter]::ToUInt32($Data, $offset + 16)
        $rawOffset = [BitConverter]::ToUInt32($Data, $offset + 20)
        $characteristics = [BitConverter]::ToUInt32($Data, $offset + 36)
        if ($rawOffset + $rawSize -gt $Data.Length) { throw "Section $name extends past end of file." }
        $sections.Add([pscustomobject]@{
            Name = $name
            VirtualSize = $virtualSize
            VirtualAddress = $virtualAddress
            RawSize = $rawSize
            RawOffset = $rawOffset
            Executable = (($characteristics -band 0x20000000) -ne 0)
        })
    }
    return [pscustomobject]@{ ImageBase = $imageBase; Sections = $sections.ToArray() }
}

function Find-MaskedMatches([byte[]]$Data, [int]$Start, [int]$Length, [byte[]]$Pattern, [byte[]]$Mask) {
    $matches = [System.Collections.Generic.List[int]]::new()
    $limit = $Start + $Length - $Pattern.Length
    for ($i = $Start; $i -le $limit; $i++) {
        if ($Data[$i] -ne $Pattern[0]) { continue }
        $same = $true
        for ($j = 1; $j -lt $Pattern.Length; $j++) {
            if (($Mask[$j] -ne 0) -and ($Data[$i + $j] -ne $Pattern[$j])) {
                $same = $false
                break
            }
        }
        if ($same) { $matches.Add($i) }
    }
    return $matches.ToArray()
}

function Find-ExactMatches([byte[]]$Data, [byte[]]$Pattern) {
    $mask = [byte[]]::new($Pattern.Length)
    for ($i = 0; $i -lt $mask.Length; $i++) { $mask[$i] = 0xff }
    return Find-MaskedMatches $Data 0 $Data.Length $Pattern $mask
}

function Get-SafeVersionName([string]$Version) {
    $safe = $Version -replace '[^A-Za-z0-9._-]', '_'
    if ([string]::IsNullOrWhiteSpace($safe)) { return 'unknown' }
    return $safe
}

function Get-Sha256Hex([byte[]]$Data) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Data)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Get-PlainClientEntries([string]$Directory) {
    $folder = Get-Item -LiteralPath $Directory -Force
    if (-not $folder.PSIsContainer -or ($folder.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Invalid client folder: $Directory"
    }
    foreach ($item in (Get-ChildItem -LiteralPath $folder.FullName -Force)) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Redirected client asset: $($item.FullName)" }
        $item
        if ($item.PSIsContainer) { Get-PlainClientEntries $item.FullName }
    }
}

function Copy-CheckedClientFile([IO.FileInfo]$File, [string]$Target) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Target) -Force | Out-Null
    Copy-Item -LiteralPath $File.FullName -Destination $Target
    $hash = (Get-FileHash -LiteralPath $File.FullName).Hash
    if ((Get-FileHash -LiteralPath $Target).Hash -ne $hash) {
        throw "Client asset verification failed: $($File.Name)"
    }
    return $hash.ToLowerInvariant()
}

function Assert-ClientChild([string]$Path, [string]$Root) {
    $full = [IO.Path]::GetFullPath($Path)
    if (-not $full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Client file operation left the manager folder: $Path"
    }
    for ($current = $full; $current -ine $Root; $current = Split-Path -Parent $current) {
        if ((Test-Path -LiteralPath $current) -and
            ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Redirected client update path: $current"
        }
    }
}

function Move-ClientDirectory([string]$From, [string]$To, [string]$Root) {
    Assert-ClientChild $From $Root
    Assert-ClientChild $To $Root
    if ((Resolve-Path -LiteralPath $From).Path -ine $From) { throw "Unexpected client directory: $From" }
    [IO.Directory]::Move($From, $To)
}

function Remove-ClientStaging([string]$Path, [string]$Root) {
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    Assert-ClientChild $Path $Root
    if ((Resolve-Path -LiteralPath $Path).Path -ine $Path) { throw "Unexpected staging directory: $Path" }
    $null = @(Get-PlainClientEntries $Path)
    Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Source AnimeSSS executable not found: $SourcePath"
}
$sourceResolved = (Resolve-Path -LiteralPath $SourcePath).Path
Assert-ClientChild $sourceResolved ([IO.Path]::GetPathRoot($sourceResolved))
$sourceInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($sourceResolved)
$version = $sourceInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($version)) { $version = $sourceInfo.FileVersion }
if ([string]::IsNullOrWhiteSpace($version)) { throw 'Source executable has no product or file version.' }

$signature = Get-AuthenticodeSignature -FilePath $sourceResolved
if ($signature.Status -ne 'NotSigned') {
    throw "Expected an unsigned source executable; signature status is $($signature.Status). No output was written."
}

$source = [IO.File]::ReadAllBytes($sourceResolved)
$sourceHash = Get-Sha256Hex $source
if ($ExpectedSourceHash -and $ExpectedSourceHash -ine $sourceHash) {
    throw 'The official executable changed during the check. Wait for the update to finish and try again.'
}
$sourceDirectory = Split-Path -Parent $sourceResolved
$pe = Get-PEInfo $source

# This is the complete 111-byte ICoreWebView2EnvironmentOptions6 getter from 0.7.9/0.7.17.
# Only its two RIP-relative calls vary between builds; those four-byte displacements are masked.
$getter = Convert-HexToBytes '5657534883ec304889d64889cb488d7c242848c7070000000031c94889faff15c7a52900488b3f4885f6741b4885ff7409488b074889f9ff50100fb68388000000890631f6eb1ebe034000804885ff741431c94889faff1597a52900488b074889f9ff501089f04883c4305b5f5ec3'
$mask = [byte[]]::new($getter.Length)
for ($i = 0; $i -lt $mask.Length; $i++) { $mask[$i] = 0xff }
foreach ($i in 32..35 + 88..91) { $mask[$i] = 0 }

$candidates = [System.Collections.Generic.List[object]]::new()
foreach ($section in $pe.Sections) {
    if (-not $section.Executable -or $section.RawSize -lt $getter.Length) { continue }
    foreach ($offset in (Find-MaskedMatches $source ([int]$section.RawOffset) ([int]$section.RawSize) $getter $mask)) {
        $rva = [uint64]$section.VirtualAddress + [uint64]$offset - [uint64]$section.RawOffset
        $candidates.Add([pscustomobject]@{
            Section = $section.Name
            FileOffset = [int]$offset
            Rva = $rva
            Va = [uint64]$pe.ImageBase + $rva
        })
    }
}
if ($candidates.Count -ne 1) {
    throw "Expected one verified WebView2 extension getter in executable sections; found $($candidates.Count). No output was written."
}
$candidate = $candidates[0]
$patchOffset = $candidate.FileOffset + 0x3a
$expectedGetter = Convert-HexToBytes '0fb68388000000'
$patchedGetter = Convert-HexToBytes 'b8010000009090'
for ($i = 0; $i -lt $expectedGetter.Length; $i++) {
    if ($source[$patchOffset + $i] -ne $expectedGetter[$i]) {
        throw 'Verified getter does not contain the expected return-field instruction. No output was written.'
    }
}

$oldId = 'com.animesss.desktop'
$newId = 'com.animesss.modktop'
if ($oldId.Length -ne $newId.Length) { throw 'Application IDs must have equal lengths.' }
$asciiOld = [Text.Encoding]::ASCII.GetBytes($oldId)
$unicodeOld = [Text.Encoding]::Unicode.GetBytes($oldId)
$asciiNew = [Text.Encoding]::ASCII.GetBytes($newId)
$unicodeNew = [Text.Encoding]::Unicode.GetBytes($newId)
$asciiMatches = @(Find-ExactMatches $source $asciiOld)
$unicodeMatches = @(Find-ExactMatches $source $unicodeOld)
$oldIdCount = $asciiMatches.Count + $unicodeMatches.Count
if ($oldIdCount -lt 1 -or $oldIdCount -gt 8) {
    throw "Expected 1-8 occurrences of the official app ID; found $oldIdCount. No output was written."
}
if (@(Find-ExactMatches $source $asciiNew).Count -gt 0 -or @(Find-ExactMatches $source $unicodeNew).Count -gt 0) {
    throw 'Source already contains the modified app ID; refusing to patch a previously modified executable.'
}

$patched = [byte[]]::new($source.Length)
[Array]::Copy($source, $patched, $source.Length)
[Array]::Copy($patchedGetter, 0, $patched, $patchOffset, $patchedGetter.Length)
foreach ($offset in $asciiMatches) { [Array]::Copy($asciiNew, 0, $patched, $offset, $asciiNew.Length) }
foreach ($offset in $unicodeMatches) { [Array]::Copy($unicodeNew, 0, $patched, $offset, $unicodeNew.Length) }

if ((Find-MaskedMatches $patched ([int]$candidate.FileOffset) $getter.Length $getter $mask).Count -ne 0) {
    throw 'Patched getter still matches the original signature.'
}
for ($i = 0; $i -lt $patchedGetter.Length; $i++) {
    if ($patched[$patchOffset + $i] -ne $patchedGetter[$i]) { throw 'Getter patch verification failed.' }
}
if (@(Find-ExactMatches $patched $asciiOld).Count -ne 0 -or @(Find-ExactMatches $patched $unicodeOld).Count -ne 0) {
    throw 'Official application ID remains after patching.'
}
if (@(Find-ExactMatches $patched $asciiNew).Count + @(Find-ExactMatches $patched $unicodeNew).Count -lt $oldIdCount) {
    throw 'Modified application ID verification failed.'
}
$outputHash = Get-Sha256Hex $patched

$DistPath = [IO.Path]::GetFullPath($DistPath).TrimEnd('\')
$destinationDirectory = Join-Path $DistPath 'app'
$destination = Join-Path $destinationDirectory 'animesss-desktop.exe'
$reportPath = Join-Path $DistPath 'CLIENT_PATCH_REPORT.json'
if ($DistPath -ieq $sourceDirectory -or
    $DistPath.StartsWith($sourceDirectory.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $sourceDirectory -ieq $destinationDirectory -or
    $sourceDirectory.StartsWith($destinationDirectory + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The official client and manager client directories must be separate.'
}
$report = [ordered]@{
    source_file = [IO.Path]::GetFileName($sourceResolved)
    source_version = $version
    source_sha256 = $sourceHash
    output_version = $version
    output_sha256 = $outputHash
    app_id = $newId
    app_id_replacements = $oldIdCount
    getter = [ordered]@{
        signature_bytes = $getter.Length
        executable_section = $candidate.Section
        file_offset = ('0x{0:x}' -f $candidate.FileOffset)
        rva = ('0x{0:x}' -f $candidate.Rva)
        va = ('0x{0:x}' -f $candidate.Va)
        patch_offset = ('0x{0:x}' -f $patchOffset)
        matching_count = $candidates.Count
    }
    profile = '%LOCALAPPDATA%\com.animesss.modktop\webview-profile'
    created_utc = [DateTime]::UtcNow.ToString('o')
    source_install_modified = $false
    profile_modified = $false
}

if ($PSCmdlet.ShouldProcess($destination, "Build AnimeSSS $version with Tampermonkey profile support")) {
    Assert-ClientChild $DistPath ([IO.Path]::GetPathRoot($DistPath))
    New-Item -ItemType Directory -Path $DistPath -Force | Out-Null
    $DistPath = (Resolve-Path -LiteralPath $DistPath).Path
    Assert-ClientChild $destinationDirectory $DistPath
    Assert-ClientChild $reportPath $DistPath
    Assert-ClientChild (Join-Path $DistPath 'CLIENT_PATCH.lock') $DistPath
    $writeLock = [IO.File]::Open((Join-Path $DistPath 'CLIENT_PATCH.lock'), [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try {
        if ((Get-FileHash -LiteralPath $sourceResolved).Hash -ine $sourceHash) {
            throw 'The official executable changed while the patch was being prepared. Try again after the update finishes.'
        }
        foreach ($process in @(Get-Process -Name 'animesss-desktop' -ErrorAction SilentlyContinue)) {
            try { $runningPath = $process.MainModule.FileName }
            catch { throw 'Cannot safely identify a running AnimeSSS process. Close AnimeSSS and try again.' }
            if ($runningPath -and ([IO.Path]::GetFullPath($runningPath) -ieq $destination)) {
                throw 'Close the modified AnimeSSS client before updating its files.'
            }
        }

        $reserved = @('animesss-desktop.exe', 'animesss-launcher.exe', 'uninstall.exe')
        $managed = @{}
        if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
            try {
                $previous = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
                if ($previous.client_assets) {
                    foreach ($item in $previous.client_assets.PSObject.Properties) { $managed[$item.Name] = $true }
                }
            } catch { Write-Verbose 'The previous resource inventory is unavailable.' }
        }

        $stageId = [Guid]::NewGuid().ToString('N')
        $stage = Join-Path $DistPath ('app.next-' + $stageId)
        $rollback = Join-Path $DistPath ('app.swap-' + $stageId)
        $reportStage = $reportPath + '.next-' + $stageId
        $reportBackup = $reportPath + '.swap-' + $stageId
        $assets = @{}
        $hasClient = Test-Path -LiteralPath $destinationDirectory -PathType Container
        $archive = $null
        if (Test-Path -LiteralPath $destination -PathType Leaf) {
            $oldHash = (Get-FileHash -LiteralPath $destination).Hash.ToLowerInvariant()
            if ($oldHash -ne $outputHash) {
                $oldVersion = Get-SafeVersionName ([Diagnostics.FileVersionInfo]::GetVersionInfo($destination).ProductVersion)
                $archiveRoot = Join-Path $DistPath 'app-versions'
                Assert-ClientChild $archiveRoot $DistPath
                New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
                $archive = Join-Path $archiveRoot $oldVersion
                if (Test-Path -LiteralPath $archive) {
                    $archive = Join-Path $archiveRoot ($oldVersion + '-' + $oldHash.Substring(0, 12) + '-' + $stageId.Substring(0, 8))
                }
            }
        }

        $oldMoved = $false
        $newMoved = $false
        try {
            New-Item -ItemType Directory -Path $stage | Out-Null
            foreach ($entry in (Get-PlainClientEntries $sourceDirectory)) {
                $relative = $entry.FullName.Substring($sourceDirectory.Length + 1)
                if ($reserved -icontains $relative) { continue }
                $target = Join-Path $stage $relative
                if ($entry.PSIsContainer) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
                else { $assets[$relative] = Copy-CheckedClientFile $entry $target }
            }
            if ($hasClient) {
                foreach ($file in (Get-PlainClientEntries $destinationDirectory | Where-Object { -not $_.PSIsContainer })) {
                    $relative = $file.FullName.Substring($destinationDirectory.Length + 1)
                    if ($reserved -icontains $relative -or $assets.ContainsKey($relative) -or $managed.ContainsKey($relative)) { continue }
                    $null = Copy-CheckedClientFile $file (Join-Path $stage $relative)
                }
            }
            $relay = Join-Path $destinationDirectory 'animesss-launcher.exe'
            if (-not (Test-Path -LiteralPath $relay -PathType Leaf)) { $relay = Join-Path $DistPath 'Dukhi.ClientUpdate.exe' }
            if (Test-Path -LiteralPath $relay -PathType Leaf) {
                Assert-ClientChild $relay $DistPath
                $null = Copy-CheckedClientFile (Get-Item -LiteralPath $relay) (Join-Path $stage 'animesss-launcher.exe')
            }

            [IO.File]::WriteAllBytes((Join-Path $stage 'animesss-desktop.exe'), $patched)
            $report['client_assets'] = $assets
            $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportStage -Encoding UTF8
            if ((Get-FileHash -LiteralPath $sourceResolved).Hash -ine $sourceHash) {
                throw 'The official executable changed while copying resources. Retry the update.'
            }
            if ($hasClient) {
                Move-ClientDirectory $destinationDirectory $rollback $DistPath
                $oldMoved = $true
            }
            Move-ClientDirectory $stage $destinationDirectory $DistPath
            $newMoved = $true
            if ($oldMoved -and $archive) {
                Move-ClientDirectory $rollback $archive $DistPath
                $rollback = $archive
            }
            if (Test-Path -LiteralPath $reportPath -PathType Leaf) {
                [IO.File]::Replace($reportStage, $reportPath, $reportBackup)
            } else {
                [IO.File]::Move($reportStage, $reportPath)
            }
        } catch {
            if ($newMoved) { Move-ClientDirectory $destinationDirectory $stage $DistPath }
            if ($oldMoved) { Move-ClientDirectory $rollback $destinationDirectory $DistPath }
            throw
        } finally {
            Remove-ClientStaging $stage $DistPath
            foreach ($leftover in @($reportStage, $reportBackup)) {
                if (Test-Path -LiteralPath $leftover -PathType Leaf) {
                    Remove-Item -LiteralPath $leftover -Force -ErrorAction SilentlyContinue
                }
            }
        }
        if ($oldMoved -and -not $archive) { Remove-ClientStaging $rollback $DistPath }
    } finally { $writeLock.Dispose() }
}

$report | ConvertTo-Json -Depth 6
