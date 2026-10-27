#requires -Version 7.0
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\monitor\quota-monitor.ps1') -Library -All -Mode Monitor
$testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('cqm-resource-check-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testDirectory)
$script:RunDirectory=$testDirectory;$script:Journal=@{};$script:ThreadId='all'
$eventPath=Join-Path $testDirectory 'events.jsonl'
try {
    [IO.File]::WriteAllText($eventPath,('x' * 4MB))
    [IO.File]::WriteAllText("$eventPath.1",'recent-archive')
    [IO.File]::WriteAllText("$eventPath.2",'older-archive')
    [IO.File]::WriteAllText("$eventPath.3",'oldest-archive')
    $reader=[IO.File]::Open($eventPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {Write-Event 'monitor' @{remaining=50}}finally{$reader.Dispose()}
    if((Get-Item -LiteralPath $eventPath).Length -gt 1MB){throw 'Active log did not rotate.'}
    if([IO.File]::ReadAllText("$eventPath.3") -ne 'older-archive'){throw 'Archive retention failed.'}
    if((Get-ChildItem -LiteralPath $testDirectory -File).Count -ne 4){throw 'Log file count is not bounded.'}
    $entry=Get-Content -LiteralPath $eventPath -Raw|ConvertFrom-Json
    if($entry.event -ne 'monitor'){throw 'New event was not preserved.'}
    Write-Host 'PASS bounded log rotation preserves history and supports a concurrent shared reader.'
} finally {
    $resolved=[IO.Path]::GetFullPath($testDirectory)
    if(!$resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($resolved) -notlike 'cqm-resource-check-*'){throw 'Unexpected cleanup path.'}
    foreach($file in Get-ChildItem -LiteralPath $resolved -File){[IO.File]::Delete($file.FullName)}
    [IO.Directory]::Delete($resolved)
}
