#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library
$script:ResumeAll=$false
$script:ResumeThreadIds=@('chosen')
$script:PauseRemainingPercent=10
$script:WeeklyPauseRemainingPercent=10
$script:ResumeRemainingPercent=20
$state=@{phase='observing';threadId='other'}
$active=@{id='other';hostId='local';turns=@(@{turnId='one';status='inProgress'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='active'}}
$quota=@{remaining=1;weeklyRemaining=1;allowed=$true}
if((Get-Decision $state $active $quota 100) -ne 'Wait'){throw 'Unmanaged active task was eligible for interruption'}
$state.threadId='chosen';$active.id='chosen'
if((Get-Decision $state $active $quota 100) -ne 'Pause'){throw 'Managed low-quota task did not pause'}
$quota.remaining=80;$quota.weeklyRemaining=10
if((Get-Decision $state $active $quota 100) -ne 'Pause'){throw 'Weekly safety buffer was ignored'}
$state=@{phase='paused';threadId='chosen';pausedTurnId='one';baselineMessageId='msg';resumeMessageId=$null}
$idle=@{id='chosen';hostId='local';turns=@(@{turnId='one';status='interrupted'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='idle'}}
$quota.remaining=19;$quota.weeklyRemaining=80
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Primary hysteresis failed'}
$quota.remaining=80;$quota.weeklyRemaining=19
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Weekly hysteresis failed'}
$quota.remaining=20;$quota.weeklyRemaining=20
if((Get-Decision $state $idle $quota 100) -ne 'RecoveryReady'){throw 'Usable quota did not enter confirmation'}
$state.manualHold=$true
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Manual hold was overridden'}
$state.manualHold=$false;$quota.allowed=$false
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Server refusal was ignored'}
$quota.allowed=$true;$script:AutoResumeEnabled=$false
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Automatic recovery switch was ignored'}
$state.manualStart=$true
if((Get-Decision $state $idle $quota 100) -ne 'RecoveryReady'){throw 'Explicit manual continue was incorrectly blocked by automatic switch'}
$state=@{phase='observing';threadId='chosen'};$quota.remaining=10
if((Get-Decision $state $active $quota 100) -ne 'Pause'){throw 'Disabling automatic recovery also disabled quota protection'}
$script:PauseRemainingPercent=15;$quota.remaining=14
if((Get-Decision $state $active $quota 100) -ne 'Pause'){throw 'Custom pause threshold was ignored'}
$state=@{phase='paused';threadId='chosen';pausedTurnId='one';baselineMessageId='msg';manualStart=$true}
$script:ResumeRemainingPercent=30;$quota.remaining=29
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Custom recovery threshold was ignored'}
$script:Mode='Auto'
$script:PauseRemainingPercent=70;$script:WeeklyPauseRemainingPercent=70;$script:ResumeRemainingPercent=71
$state=@{phase='queued';threadId='chosen';manualStart=$true;pausedTurnId='one';baselineMessageId='msg'}
$quota=@{remaining=75;weeklyRemaining=65;allowed=$true}
$reason=Get-RecoveryWaitReason $state $idle $quota
if($reason -notmatch '65%' -or $reason -notmatch '71%' -or $reason -match '耗尽'){throw 'Threshold buffer was mislabeled as exhausted or missing numbers'}
if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Manual continue bypassed quota buffer'}
if((Get-QuotaStatus $quota) -match '耗尽'){throw 'Positive weekly quota was labeled exhausted'}
$quota.weeklyRemaining=0
if((Get-QuotaStatus $quota) -ne '周额度耗尽'){throw 'Actual weekly exhaustion lost its status'}
Write-Host 'PASS 17 manager-specific scope, configurable thresholds, recovery switch and manual-hold checks. No real chats controlled.'
