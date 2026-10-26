#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library
$script:Mode='Auto';$script:ObserveOnly=$false;$script:ResumeAll=$false;$script:ResumeThreadIds=@('chosen')
$script:PauseRemainingPercent=55;$script:WeeklyPauseRemainingPercent=55;$script:ResumeRemainingPercent=71
$checks=0
foreach($basis in @('fiveHour','weekly','both')){
 $script:QuotaBasis=$basis
 foreach($enabled in @($true,$false)){
  $script:AutoResumeEnabled=$enabled
  $idle=@{id='chosen';requests=@();turns=@(@{turnId='old';status='interrupted'});threadRuntimeStatus=@{type='idle'};latestTurnStartMessageId='msg'}
  $state=@{phase='queued';threadId='chosen';manualStart=$true;pausedTurnId='old';baselineMessageId='msg'}
  foreach($remaining in @(54,55,56,62,71)){
   $quota=@{remaining=$remaining;weeklyRemaining=$remaining;allowed=$true}
   $expected=$(if($remaining -gt 55){'RecoveryReady'}else{'Wait'})
   if((Get-Decision $state $idle $quota 100) -ne $expected){throw "Manual boundary failed: $basis $enabled $remaining"}
   $goal=@{status='paused';objective='fixture';createdAt=1;updatedAt=2;tokenBudget=100}
   $goalIdle=$idle.Clone();$goalIdle.threadGoal=$goal
   $goalState=$state.Clone();$goalState.pausedForGoal=$true;$goalState.pausedGoal=$goal.Clone();$goalState.pausedGoalUpdatedAt=2
   if((Get-Decision $goalState $goalIdle $quota 100) -ne $expected){throw "Manual Goal boundary failed: $basis $enabled $remaining"}
   $state.manualStart=$false
   $automatic=$(if($enabled -and $remaining -ge 71){'RecoveryReady'}else{'Wait'})
   if((Get-Decision $state $idle $quota 100) -ne $automatic){throw "Automatic threshold failed: $basis $enabled $remaining"}
   $state.manualStart=$true;$checks+=3
  }
  $quota=@{remaining=62;weeklyRemaining=62;allowed=$false}
  if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Manual control bypassed server refusal'}
  $quota.allowed=$true;$idle.requests=@(@{type='approval'})
  if((Get-Decision $state $idle $quota 100) -ne 'Wait'){throw 'Manual control bypassed pending input'}
  $checks+=2
 }
}
Write-Host "PASS $checks manual-vs-automatic recovery checks. No real chats controlled."
