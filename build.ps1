param([string]$OutputName = 'AnimeSSS.Local.next.exe', [switch]$ManagerOnly)
$ErrorActionPreference = 'Stop'
$localModRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$localCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$localOutput = Join-Path $localModRoot 'dist'
if ($OutputName -notmatch '^AnimeSSS\.Local(?:\.next)?\.exe$') { throw 'Unexpected output name' }
$iconArgument = @()
if (!$ManagerOnly) { $iconArgument += "/win32icon:$localOutput\app\app-icon.ico" }
& $localCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$localOutput\$OutputName" @iconArgument /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Net.Http.dll /reference:System.IO.Compression.dll "/reference:$localOutput\Microsoft.Web.WebView2.Core.dll" "/reference:$localOutput\Microsoft.Web.WebView2.WinForms.dll" "$localModRoot\ExtensionManager.cs" "$localModRoot\ExtensionsUI.cs" "$localModRoot\ExtensionPackages.cs" "$localModRoot\ExtensionDiagnostics.cs" "$localModRoot\AdBlocker.cs" "$localModRoot\ClientHotkey.cs" "$localModRoot\WeatherMaskHost.cs" "$localModRoot\ClientUpdate.cs" "$localModRoot\ManagerUpdate.cs" "$localModRoot\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
& $localCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$localOutput\Dukhi.ClientUpdate.exe" /reference:System.dll /reference:System.Windows.Forms.dll "$localModRoot\ClientUpdateRelay.cs"
if ($LASTEXITCODE -ne 0) { throw 'Client update relay compilation failed' }
$localVerifier = Join-Path $localOutput 'vendor\minisign'
New-Item -ItemType Directory -Path $localVerifier -Force | Out-Null
Copy-Item -LiteralPath "$localModRoot\vendor\minisign\minisign.exe","$localModRoot\vendor\minisign\LICENSE.txt" -Destination $localVerifier -Force
if ($ManagerOnly) { return }
& $localCompiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$localOutput\Dukhi.Reminders.exe" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/reference:$localOutput\Microsoft.Web.WebView2.Core.dll" "/reference:$localOutput\Microsoft.Web.WebView2.WinForms.dll" "/resource:$localModRoot\reminder-toast.html,reminder-toast.html" "/resource:$localModRoot\reminder-tone.wav,reminder-tone.wav" "$localModRoot\ClickerReminders.cs"
if ($LASTEXITCODE -ne 0) { throw 'Reminder compilation failed' }
