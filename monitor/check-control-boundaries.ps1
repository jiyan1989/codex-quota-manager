#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'quota-monitor.ps1') -Library
function Save-Journal {}
function Write-Event {}
$script:Mode='Auto';$script:ObserveOnly=$false;$script:ResumeAll=$false;$script:ResumeThreadIds=@();$script:ResumeExcludedThreadIds=@()
$script:QuotaBasis='both';$script:PauseRemainingPercent=10;$script:WeeklyPauseRemainingPercent=10;$script:ResumeRemainingPercent=20;$script:AutoResumeEnabled=$true
$script:Checks=0
function Check($Condition,$Name){if(!$Condition){throw $Name};$script:Checks++;Write-Host "PASS $Name"}
$id='00000000-0000-0000-0000-000000000001'
$snapshot=@{id=$id;requests=@();turns=@(@{turnId='current';status='inProgress'});latestTurnStartMessageId='msg';threadRuntimeStatus=@{type='active'}}
foreach($phase in @('observing','resumed','paused','queued','pause_pending','resume_submitting')){
 $script:Journal=@{threadId=$id;phase=$phase;manualStart=$true;forcePause=$true;pendingCommand=@{id='cancel';action='CancelAuto'}}
 Apply-ThreadCommand $snapshot
 Check ($script:Journal.phase -eq 'cancelled' -and !$script:Journal.manualStart -and !$script:Journal.forcePause) "cancellation clears control intent from $phase"
 Check ((Get-Decision $script:Journal $snapshot @{remaining=1;weeklyRemaining=1;allowed=$true} 100) -eq 'Wait') "cancelled $phase does not pause or resume"
}
foreach($phase in @('observing','resumed','queued','paused','resume_submitting')){
 Check ((Get-Decision @{threadId=$id;phase=$phase} $snapshot @{remaining=1;weeklyRemaining=1;allowed=$true} 100) -eq 'Wait') "unmanaged $phase is outside control scope"
}
$script:ResumeThreadIds=@($id)
foreach($manual in @($true,$false)){
 foreach($missing in @('remaining','weeklyRemaining')){
  $quota=@{remaining=80;weeklyRemaining=80;allowed=$true};$quota[$missing]=$null
  Check (!(Test-ManagedQuotaReady $quota @{manualStart=$manual})) "both mode blocks missing $missing with manual=$manual"
 }
}
$script:Opened=[Collections.Generic.List[string]]::new()
function Open-DesktopThread([string]$Id){$script:Opened.Add($Id)}
$catalog=@{ids=@($id)};$owners=@{};$quota=@{remaining=80;weeklyRemaining=80;allowed=$true}
$record=@{threadId=$id;phase='paused';manualHold=$false;manualStart=$false};$fleet=@{threads=@{($id)=$record}}
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 1 -and $record.loadAttempts -eq 1) 'eligible saved pause opens its original chat'
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 1) 'loading requests are throttled'
$record.loadRequestedAt=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds()-61
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 2) 'loading retries after backoff'
$owners[$id]='fixture-owner';Request-UnloadedThreads $fleet $catalog $owners $quota
Check (!$record.loadRequestedAt -and $record.loadAttempts -eq 0 -and $script:Opened.Count -eq 2) 'loaded original clears retry state without another open'
$owners=@{};$script:AutoResumeEnabled=$false
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 2) 'automatic loading respects disabled auto-resume'
$record.pendingCommand=@{action='Start'};$record.manualHold=$true
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 3) 'explicit manual continue loads original despite disabled automatic recovery'
$record.loadRequestedAt=$null;$record.pendingCommand=@{action='CancelAuto'}
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 3) 'cancellation never loads chat'
$record.pendingCommand=$null;$record.manualHold=$false;$script:AutoResumeEnabled=$true;$script:ResumeThreadIds=@()
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 3) 'unmanaged chat never auto-loads'
$script:ResumeThreadIds=@($id);$script:ObserveOnly=$true
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 3) 'read-only mode does not open chats'
$script:ObserveOnly=$false;$quota.weeklyRemaining=$null
Request-UnloadedThreads $fleet $catalog $owners $quota
Check ($script:Opened.Count -eq 3) 'unknown selected quota blocks automatic chat loading'
Write-Host "$script:Checks control boundary checks passed. No real chats or shells opened."
