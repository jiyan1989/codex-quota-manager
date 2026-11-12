#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library -All -Mode Monitor

$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('cqm-discovery-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($sandbox)
$checks=0
function Assert-Discovery($condition,$message) {
    if(!$condition){throw $message}
    $script:checks++
    Write-Host "PASS $message"
}
function Fixture-File($relative) {
    $path=Join-Path $sandbox $relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path,'fixture')
    return $path
}
function Get-RunningCodexLocations {return $script:RunningLocations}
function Get-CodexCommonLocations {return $script:CommonLocations}
function Get-AppxPackage {throw 'Appx cmdlet unavailable in this fixture'}
$originalPackageLookup=${function:Get-CodexPackageLocations}

try {
    $desktop=Fixture-File '便携 安装\Codex.exe'
    $cli=Fixture-File '便携 安装\resources\codex.exe'
    $null=Fixture-File '便携 安装\resources\app.asar'
    $script:RunningLocations=@($desktop)
    $script:CommonLocations=@()
    $script:CodexInstallation=$null
    $found=Resolve-CodexInstallation
    Assert-Discovery ($found.cliPath -eq $cli -and $found.desktopPath -eq $desktop) 'running portable desktop resolves its companion CLI'
    Assert-Discovery ($found.source -eq 'running') 'running installation has priority'
    Assert-Discovery (![string]::IsNullOrWhiteSpace((Get-DesktopVersion))) 'missing desktop version metadata does not block startup'
    $found=Find-CodexAtLocation $cli 'manual'
    Assert-Discovery ($found.cliPath -eq $cli -and $found.desktopPath -eq $desktop) 'selected companion CLI still locates its desktop executable'
    $found=Find-CodexAtLocation ([IO.Path]::GetDirectoryName($desktop)) 'manual'
    Assert-Discovery ($found.cliPath -eq $cli) 'installation folder with spaces and Unicode resolves'

    $packageRoot=Join-Path $sandbox 'store-package'
    $storeDesktop=Fixture-File 'store-package\app\Codex.exe'
    $storeCli=Fixture-File 'store-package\app\resources\codex.exe'
    $null=Fixture-File 'store-package\app\resources\app.asar'
    function Get-CodexPackageLocations {return $packageRoot}
    $script:RunningLocations=@()
    $script:CodexInstallation=$null
    $found=Resolve-CodexInstallation
    Assert-Discovery ($found.cliPath -eq $storeCli -and $found.desktopPath -eq $storeDesktop -and $found.source -eq 'package') 'registered package locates app resources without PATH'
    $script:RunningLocations=@($desktop)
    $script:CodexInstallation=$null
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $cli) 'running desktop wins over another installed package'

    $standalone=Fixture-File 'legacy-bin\version-1\codex.exe'
    $script:CodexLocation=$standalone
    $script:CodexInstallation=$null
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $standalone) 'manual location takes precedence over automatic discovery'
    $script:CodexLocation=Join-Path $sandbox 'missing\codex.exe'
    $script:CodexInstallation=$null
    $reason=''
    try {$null=Resolve-CodexInstallation}catch{$reason=$_.Exception.Message}
    Assert-Discovery ($reason -match 'Codex location invalid') 'invalid manual location does not silently select another installation'

    $script:CodexLocation=''
    $script:RunningLocations=@()
    Set-Item Function:Get-CodexPackageLocations $originalPackageLookup
    $script:CommonLocations=@($standalone)
    $script:CodexInstallation=$null
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $standalone) 'common CLI location works when Appx lookup is unavailable'

    $npmRoot=Join-Path $sandbox 'npm'
    $npmCli=Fixture-File 'npm\node_modules\@openai\codex\vendor\x86_64-pc-windows-msvc\codex\codex.exe'
    Assert-Discovery ((Find-CodexAtLocation $npmRoot 'common').cliPath -eq $npmCli) 'npm vendor CLI layout is supported'
    $launcher=Fixture-File 'incomplete\Codex.exe'
    $null=Fixture-File 'incomplete\resources\app.asar'
    Assert-Discovery ($null -eq (Find-CodexAtLocation $launcher 'manual')) 'desktop launcher is never substituted for a missing CLI'
    $other=Fixture-File 'other\unrelated.exe'
    Assert-Discovery ($null -eq (Find-CodexAtLocation $other 'manual')) 'unrelated executable selection is rejected'

    $script:CommonLocations=@()
    $script:CodexInstallation=$null
    $reason=''
    try {$null=Resolve-CodexInstallation}catch{$reason=$_.Exception.Message}
    Assert-Discovery ($reason -eq 'Codex CLI executable was not found.') 'missing installation has a specific error'

    $script:CommonLocations=@($cli)
    $script:CodexInstallation=@{cliPath=(Join-Path $sandbox 'old\codex.exe')}
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $cli) 'removed cached executable is rediscovered after an update'
    $script:SettingsPath=Join-Path $sandbox 'settings.json'
    $script:CodexLocation=''
    Write-JsonAtomic $script:SettingsPath @{pollSeconds=30;pauseRemainingPercent=10;codexLocation=$standalone}
    Read-MonitorSettings
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $standalone) 'saved location changes invalidate previous discovery'
    Write-JsonAtomic $script:SettingsPath @{pollSeconds=30;pauseRemainingPercent=10}
    Read-MonitorSettings
    Assert-Discovery ((Resolve-CodexInstallation).cliPath -eq $cli) 'older settings without location retain automatic discovery'

    foreach($case in @(
        @('Codex location invalid.','location-invalid'),
        @('Codex CLI executable was not found.','codex-missing'),
        @('Codex desktop is not connected.','disconnected'),
        @('Unsupported snapshot protocol version.','incompatible'),
        @('request-version-mismatch','incompatible'),
        @('Snapshot identity mismatch.','identity-mismatch'),
        @('A ChatGPT account login is required.','login-required')
    )){
        $problem=Get-ConnectionProblem $case[0]
        Assert-Discovery ($problem.code -eq $case[1] -and $problem.message.Length -gt 0) "diagnostic maps $($case[1]) to an actionable message"
    }
    $script:Journal=$null
    $output=& {Write-ConnectionStatus (Get-ConnectionProblem 'Codex CLI executable was not found.')} 6>&1
    $line=([string]$output).Trim()
    $payload=$line.Substring('CQM_CONNECTION '.Length)|ConvertFrom-Json
    Assert-Discovery ($line.StartsWith('CQM_CONNECTION ') -and $payload.code -eq 'codex-missing') 'startup failure can reach the GUI before state.json exists'
    $startupState=Join-Path $sandbox 'startup-state'
    $startupSettings=Join-Path $sandbox 'startup-settings.json'
    Write-JsonAtomic $startupSettings @{pollSeconds=30;pauseRemainingPercent=10;codexLocation=(Join-Path $sandbox 'absent\Codex.exe')}
    $startupOutput=& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $PSScriptRoot 'quota-monitor.ps1') -All -Mode Monitor -Once -StateDirectory $startupState -SettingsPath $startupSettings
    $startupExit=$LASTEXITCODE
    $startupStatus=@($startupOutput|Where-Object {$_ -like 'CQM_CONNECTION *'})
    Assert-Discovery ($startupExit -eq 1 -and $startupStatus.Count -eq 1 -and ($startupStatus[0].Substring('CQM_CONNECTION '.Length)|ConvertFrom-Json).code -eq 'location-invalid') 'real startup entry reports invalid selection before any client connection'
    $global:LASTEXITCODE=0
    Write-Host "$checks discovery checks passed. No real client or task was controlled."
} finally {
    $resolved=[IO.Path]::GetFullPath($sandbox)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'cqm-discovery-*'){
        throw 'Unexpected test cleanup path.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
