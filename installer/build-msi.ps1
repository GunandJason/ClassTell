# ClassTell - build the release MSI (WiX v4+ / dotnet tool)
#   Usage: powershell -ExecutionPolicy Bypass -File installer\build-msi.ps1
#   Steps: clean -> build icon -> Release build into dist\stage -> stage extras ->
#          license.rtf -> wix build -> validate
#
#   One-time setup (WiX 5.x is used on purpose: WiX 6/7 require accepting the
#   Open Source Maintenance Fee EULA, which this project does not):
#       dotnet tool install -g wix --version 5.0.2
#       wix extension add -g WixToolset.UI.wixext/5.0.2
#
#   Options: -OutputDir <dir>   where the MSI goes (default: dist)
#            -SkipTests         do not run the self tests
#            -SkipValidate      skip ICE validation and the msiexec /a file check
#   Note: an msiexec /a verification can leave the produced MSI locked by the
#   Windows Installer service; if the script reports a stale/locked output,
#   restart the "Windows Installer" service as administrator (or reboot) and rebuild.
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$OutputDir = 'dist',
    [switch]$SkipTests,
    [switch]$SkipValidate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root $OutputDir          # MSI 输出目录（可切换，例如 release）
$dist = Join-Path $root 'dist'                # 暂存目录：必须与 ClassTell.wxs 里的相对路径一致
$stage = Join-Path $dist 'stage'
$extra = Join-Path $dist 'extra'
$msiName = 'ClassTell-1.0.0_r.msi'             # 文件名带发行标记（MSI 的 Version 仍必须是纯数字 1.0.0）
$msi = Join-Path $outDir $msiName
$wxs = Join-Path $PSScriptRoot 'ClassTell.wxs'
$icon = Join-Path $PSScriptRoot 'ClassTell.ico'
$wix = Join-Path $env:USERPROFILE '.dotnet\tools\wix.exe'
if (-not (Test-Path $wix)) { $wix = 'wix' }

function Step([string]$text) { Write-Host ""; Write-Host ("=== " + $text) -ForegroundColor Cyan }

Step "1/7 clean staging and output"
Remove-Item $stage, $extra -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $msi -Force -ErrorAction SilentlyContinue
New-Item $outDir, $stage, $extra -ItemType Directory -Force | Out-Null

Step "2/7 build (Debug, for the icon tool) and export the app icon"
& dotnet build (Join-Path $root 'ClassTell.sln') -c Debug -v q | Out-Host
& (Join-Path $root 'tests\ClassTell.SelfTest\bin\Debug\net48\ClassTell.SelfTest.exe') --make-icon $icon | Out-Host
if (-not (Test-Path $icon)) { throw "icon export failed: $icon" }

Step "3/7 build $Configuration into dist\stage"
& dotnet build (Join-Path $root 'ClassTell.sln') -c $Configuration -p:OutDir="$stage\" -v m | Out-Host

if (-not $SkipTests) {
    Step "4/7 run self tests on the staged build (so the tested bits are the shipped bits)"
    & (Join-Path $stage 'ClassTell.SelfTest.exe') | Select-Object -Last 1 | Out-Host
}
else { Step "4/7 self tests skipped" }

Step "5/7 stage extras (diagnostic tool, docs) and drop pdb files"
Move-Item (Join-Path $stage 'ClassTell.SelfTest.*') $extra -Force
Get-ChildItem $stage, $extra -Filter *.pdb | Remove-Item -Force -ErrorAction SilentlyContinue
foreach ($doc in 'LICENSE', 'README.md') { Copy-Item (Join-Path $root $doc) $stage -Force }
Get-ChildItem $stage | Select-Object -ExpandProperty Name | Sort-Object | ForEach-Object { Write-Host ("    stage: " + $_) }
Get-ChildItem $extra | Select-Object -ExpandProperty Name | Sort-Object | ForEach-Object { Write-Host ("    extra: " + $_) }

Step "6/7 generate license.rtf from license.txt (unicode escapes, ASCII file)"
$licenseText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'license.txt'), [Text.Encoding]::UTF8)
$sb = New-Object System.Text.StringBuilder
[void]$sb.Append('{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset134 Microsoft YaHei;}}\viewkind4\uc1\pard\f0\fs20 ')
foreach ($ch in $licenseText.ToCharArray()) {
    $code = [int]$ch
    if ($code -lt 128) {
        if ($ch -eq '{') { [void]$sb.Append('\{') }
        elseif ($ch -eq '}') { [void]$sb.Append('\}') }
        elseif ($ch -eq '\') { [void]$sb.Append('\\') }
        elseif ($ch -eq "`r") { }
        elseif ($ch -eq "`n") { [void]$sb.Append('\par ') }
        else { [void]$sb.Append($ch) }
    }
    else {
        if ($code -gt 32767) { $code -= 65536 }   # RTF \u takes a signed 16-bit value
        [void]$sb.Append('\u' + $code + '?')
    }
}
[void]$sb.Append('}')
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'license.rtf'), $sb.ToString(), [Text.Encoding]::ASCII)

Step "7/7 build the MSI"
& $wix extension add -g WixToolset.UI.wixext 2>&1 | Out-Null
Remove-Item $msi, (Join-Path $dist ([IO.Path]::GetFileNameWithoutExtension($msiName) + '.wixpdb')) -Force -ErrorAction SilentlyContinue
$wixArgs = @('build', '-b', $PSScriptRoot, '-arch', 'x64', '-ext', 'WixToolset.UI.wixext', '-o', $msi, $wxs)
& $wix @wixArgs '-culture' 'zh-cn' | Out-Host
$buildExit = $LASTEXITCODE
if (-not (Test-Path $msi)) {
    Write-Host "zh-cn UI attempt failed, retrying with the default culture (Package/@Codepage=936 keeps Chinese text valid)" -ForegroundColor Yellow
    & $wix @wixArgs | Out-Host
    $buildExit = $LASTEXITCODE
}
if (-not (Test-Path $msi)) { throw "wix build failed" }
if ($buildExit -ne 0) {
    Write-Host "note: wix reported a Windows Installer service warning (1631) but produced the MSI; the ICE validation below is the real check." -ForegroundColor Yellow
}

# 新鲜度校验：若输出被其它进程占用，wix 会“看似成功”却留下旧文件，必须挡住这种假成功
$stagedExe = Get-Item (Join-Path $stage 'ClassTell.exe')
$builtMsi = Get-Item $msi
if ($builtMsi.LastWriteTime -lt $stagedExe.LastWriteTime) {
    throw ("the produced MSI is older than the staged exe - the output file is locked by another process " +
           "(usually the Windows Installer service after an msiexec /a verification). " +
           "Restart the Windows Installer service as administrator or reboot, or pass -OutputDir <dir>.")
}

# 体积校验：空包（暂存目录没被 WiX 找到）会小到几百 KB，必须挡住
if ($builtMsi.Length -lt 1MB) {
    throw ("the produced MSI is only " + [Math]::Round($builtMsi.Length / 1KB) + " KB - the staged files were not packaged " +
           "(check the 'Files Include' paths in ClassTell.wxs against the staging directory: " + $stage + ").")
}

if (-not $SkipValidate) {
    Step "validate the MSI"
    $report = Join-Path $outDir 'msi-validation.txt'
    & $wix msi validate $msi 2>&1 | Out-File -FilePath $report -Encoding UTF8
    Get-Content $report -Encoding UTF8 | Where-Object { $_ -match 'validation|ICE|error|warning' } |
        Select-Object -First 12 | ForEach-Object { Write-Host ("    " + $_) }

    $extract = Join-Path $dist '_msi-extract'
    Write-Host "    extracting with msiexec /a for a file-level check ..."
    & msiexec /a $msi /qn TARGETDIR=$extract | Out-Null
    Start-Sleep -Seconds 3
    $installed = @(Get-ChildItem (Join-Path $extract 'ClassTell') -File -Recurse -ErrorAction SilentlyContinue)
    if ($installed.Count -gt 0) {
        $expected = @(Get-ChildItem $stage -File) + @(Get-ChildItem $extra -File)
        Write-Host ("    extracted files: " + $installed.Count + " / expected: " + $expected.Count)
        $missing = @()
        foreach ($f in $expected) { if (-not (Test-Path (Join-Path $extract ('ClassTell\' + $f.Name)))) { $missing += $f.Name } }
        if ($missing.Count -eq 0) { Write-Host "    all staged files are present in the MSI" -ForegroundColor Green }
        else { Write-Host ("    MISSING in MSI: " + ($missing -join ', ')) -ForegroundColor Yellow }
    }
    else { Write-Host "    (administrative install did not run - run the MSI manually to verify)" -ForegroundColor Yellow }
}

$info = Get-Item $msi
Write-Host ""
Write-Host ("OK -> " + $info.FullName + "  (" + [Math]::Round($info.Length / 1MB, 2) + " MB)") -ForegroundColor Green
Write-Host "Install with: msiexec /i `"$($info.FullName)`"   (or just double-click)"
