#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RequestPath,[ValidateRange(1,120)][int]$BootTimeoutSeconds=30)
$ErrorActionPreference='Stop'
$request=Get-Content -Raw -LiteralPath $RequestPath|ConvertFrom-Json -AsHashtable
$target=[IO.Path]::GetFullPath([string]$request.target)
$payload=[IO.Path]::GetFullPath([string]$request.payload)
$folder=Split-Path $target -Parent
$backup=Join-Path $folder ('CodexQuotaMonitor.rollback-'+[guid]::NewGuid().ToString('N')+'.exe')
$staged=Join-Path $folder ('CodexQuotaMonitor.staged-'+[guid]::NewGuid().ToString('N')+'.exe')
$receipt=Join-Path (Split-Path ([IO.Path]::GetFullPath($RequestPath)) -Parent) 'boot-receipt.json'
$result=Join-Path (Split-Path ([IO.Path]::GetFullPath($RequestPath)) -Parent) 'result.json'
function Save-Result($status,$detail){@{status=$status;detail=$detail;version=$request.version;time=[DateTimeOffset]::UtcNow.ToString('o')}|ConvertTo-Json|Set-Content -LiteralPath $result -Encoding utf8}
function Quote-Argument([string]$value){
    if($value -notmatch '[\s"]'){return $value}
    $escaped=[regex]::Replace($value,'(\\*)"','$1$1\"')
    $escaped=[regex]::Replace($escaped,'(\\+)$','$1$1')
    return '"'+$escaped+'"'
}
function Start-Application([switch]$Acknowledge){
    $arguments=@($request.restartArgs)
    if($Acknowledge){$arguments+=@('--update-receipt',$receipt)}
    $start=[Diagnostics.ProcessStartInfo]::new($target,(@($arguments|ForEach-Object {Quote-Argument ([string]$_)}) -join ' '))
    $start.UseShellExecute=$false
    return [Diagnostics.Process]::Start($start)
}
$newProcess=$null;$moved=$false;$validTarget=$false
try {
    if([IO.Path]::GetFileName($target) -ne 'CodexQuotaMonitor.exe' -or $payload -eq $target){throw '无效的更新目标。'}
    $validTarget=$true
    if((Get-FileHash -LiteralPath $payload -Algorithm SHA256).Hash -ne $request.payloadHash){throw '待安装程序校验失败。'}
    if([int]$request.parentPid -gt 0){
        $parent=Get-Process -Id ([int]$request.parentPid) -ErrorAction SilentlyContinue
        if($parent -and !$parent.WaitForExit(60000)){throw '原程序尚未退出，更新已取消。'}
    }
    if((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $request.previousHash){throw '当前程序已变化，更新已取消。'}
    if([Reflection.AssemblyName]::GetAssemblyName($target).Version -ge [version]$request.version){throw '当前程序已是同版或更新版本，不进行覆盖。'}
    Copy-Item -LiteralPath $payload -Destination $staged
    Move-Item -LiteralPath $target -Destination $backup
    $moved=$true
    Move-Item -LiteralPath $staged -Destination $target
    $newProcess=Start-Application -Acknowledge
    $deadline=[DateTime]::UtcNow.AddSeconds($BootTimeoutSeconds)
    while([DateTime]::UtcNow -lt $deadline){
        if(Test-Path -LiteralPath $receipt){
            $boot=Get-Content -Raw -LiteralPath $receipt|ConvertFrom-Json -AsHashtable
            if($boot.version -eq $request.version -and !$newProcess.HasExited){Save-Result 'installed' '更新完成，设置和任务记录已保留。';exit 0}
        }
        if($newProcess.HasExited){break}
        Start-Sleep -Milliseconds 200
    }
    throw '新版未能正常启动。'
} catch {
    $detail=$_.Exception.Message
    if($moved){
        try {
            if($newProcess -and !$newProcess.HasExited){$newProcess.Kill();$newProcess.WaitForExit(10000)|Out-Null}
            if(Test-Path -LiteralPath $target){Move-Item -LiteralPath $target -Destination ($target+'.failed-'+[guid]::NewGuid().ToString('N'))}
            Move-Item -LiteralPath $backup -Destination $target
            if($request.settingsPath -and $request.settingsBackup -and (Test-Path -LiteralPath $request.settingsBackup)){Copy-Item -LiteralPath $request.settingsBackup -Destination $request.settingsPath -Force}
            Save-Result 'rolled_back' ('更新失败，已恢复旧版：'+$detail)
            [void](Start-Application)
        } catch {Save-Result 'recovery_required' ('恢复旧版时出错，备份位于 '+$backup+'；'+$_.Exception.Message)}
    } else {
        Save-Result 'cancelled' ('保留原版本：'+$detail)
        $parentAlive=[int]$request.parentPid -gt 0 -and (Get-Process -Id ([int]$request.parentPid) -ErrorAction SilentlyContinue)
        if($validTarget -and !$parentAlive -and (Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target).Hash -eq $request.previousHash){try{[void](Start-Application)}catch{}}
    }
    exit 1
}
