#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library
$script:Mode='Auto';$script:ObserveOnly=$false;$script:ResumeAll=$false;$script:ResumeThreadIds=@('chosen');$script:QuotaBasis='layered'
$script:PauseRemainingPercent=10;$script:ResumeRemainingPercent=20;$script:WeeklyPauseRemainingPercent=30;$script:AutoResumeEnabled=$true
$active=@{id='chosen';turns=@(@{turnId='old';status='inProgress'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='active'}}
$idle=@{id='chosen';turns=@(@{turnId='old';status='interrupted'});requests=@();latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='idle'}}
$count=0
foreach($five in @(0,10,11,19,20,100)){
 foreach($week in @(0,29,30,31,100)){
  $quota=@{remaining=$five;weeklyRemaining=$week;allowed=$true}
  $observing=@{phase='observing';threadId='chosen'}
  if(((Get-Decision $observing $active $quota 100) -eq 'Pause') -ne ($five -le 10 -or $week -le 30)){throw "Pause mismatch $five $week"}
  foreach($manual in @($false,$true)){
   $state=@{phase='paused';threadId='chosen';pausedTurnId='old';baselineMessageId='msg';manualStart=$manual}
   $ready=$week -gt 30 -and $(if($manual){$five -gt 10}else{$five -ge 20})
   if(((Get-Decision $state $idle $quota 100) -eq 'RecoveryReady') -ne $ready){throw "Recovery mismatch $five $week manual=$manual"}
   $count++
  };$count++
 }
}
foreach($missing in @('remaining','weeklyRemaining')){
 $quota=@{remaining=100;weeklyRemaining=100;allowed=$true};$quota[$missing]=$null
 if(!(Test-QuotaPause $quota)){throw 'Unknown quota did not protect running task'}
 foreach($manual in @($false,$true)){if(Test-ManagedQuotaReady $quota @{manualStart=$manual}){throw 'Unknown quota allowed recovery'};$count++}
}
$state=@{phase='paused';threadId='chosen';pausedTurnId='old';baselineMessageId='msg';manualStart=$true}
$reason=Get-RecoveryWaitReason $state $idle @{remaining=100;weeklyRemaining=30;allowed=$true}
if($reason -notmatch '保留底线 30%' -or $reason -notmatch '不能绕过'){throw 'Weekly floor reason missing'}
$script:AutoResumeEnabled=$false;$state.manualStart=$false
if((Get-Decision $state $idle @{remaining=100;weeklyRemaining=100;allowed=$true} 100) -ne 'Wait'){throw 'Disabled automatic continuation was ignored'}
$state.manualStart=$true
if((Get-Decision $state $idle @{remaining=100;weeklyRemaining=30;allowed=$true} 100) -ne 'Wait'){throw 'Manual continuation bypassed floor'}
Write-Host "PASS $($count+3) independent weekly-floor and five-hour policy checks. No real chats controlled."
