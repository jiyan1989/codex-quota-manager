#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library
$script:Mode='Auto';$script:ResumeAll=$false;$script:ResumeThreadIds=@('chosen')
$script:PauseRemainingPercent=10;$script:WeeklyPauseRemainingPercent=10;$script:ResumeRemainingPercent=20
$script:AutoResumeEnabled=$true
$active=@{id='chosen';turns=@(@{turnId='one';status='inProgress'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='active'}}
$idle=@{id='chosen';turns=@(@{turnId='one';status='interrupted'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='idle'}}
$checks=0
foreach($basis in @('both','fiveHour','weekly')) {
    $script:QuotaBasis=$basis
    foreach($pair in @(@(5,80),@(80,5),@(5,5),@(80,80))) {
        $quota=@{remaining=$pair[0];weeklyRemaining=$pair[1];allowed=$true}
        $expectedPause=($basis -ne 'weekly' -and $pair[0] -le 10) -or ($basis -ne 'fiveHour' -and $pair[1] -le 10)
        $state=@{phase='observing';threadId='chosen'}
        if(((Get-Decision $state $active $quota 100) -eq 'Pause') -ne $expectedPause){throw "Pause basis mismatch: $basis $pair"}
        $state=@{phase='paused';threadId='chosen';pausedTurnId='one';baselineMessageId='msg'}
        $expectedReady=($basis -eq 'weekly' -or $pair[0] -ge 20) -and ($basis -eq 'fiveHour' -or $pair[1] -ge 20)
        if(((Get-Decision $state $idle $quota 100) -eq 'RecoveryReady') -ne $expectedReady){throw "Recovery basis mismatch: $basis $pair"}
        $goalActive=$active.Clone();$goalActive.threadGoal=@{status='active'}
        if(((Get-Decision @{phase='observing';threadId='chosen'} $goalActive $quota 100) -eq 'Pause') -ne $expectedPause){throw "Goal pause basis mismatch: $basis $pair"}
        $checks+=3
    }
    $quota=@{remaining=80;weeklyRemaining=80;allowed=$false}
    if(Test-ManagedQuotaReady $quota){throw "Server refusal bypassed: $basis"}
    if(!(Test-QuotaPause $quota)){throw "Server refusal did not pause: $basis"}
    $checks+=2
}
$script:QuotaBasis='weekly'
if(Test-ManagedQuotaReady @{remaining=80;weeklyRemaining=$null;allowed=$true}){throw 'Unknown selected weekly quota allowed recovery'}
if(!(Test-QuotaPause @{remaining=80;weeklyRemaining=$null;allowed=$true})){throw 'Unknown selected weekly quota did not protect active task'}
$script:QuotaBasis='fiveHour'
$state=@{phase='queued';threadId='chosen';manualStart=$true}
$reason=Get-RecoveryWaitReason $state $idle @{remaining=15;weeklyRemaining=1;allowed=$true}
if($reason -notmatch '五小时' -or $reason -match '周剩余|周额度'){throw 'Five-hour-only wait reason mentions unselected weekly quota'}
$checks+=3
Write-Host "PASS $checks quota basis matrix checks. No real chats controlled."
