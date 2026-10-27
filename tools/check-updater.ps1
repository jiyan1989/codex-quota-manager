#requires -Version 7.0
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('quota updater check '+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($sandbox)
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$stub=@'
using System;
using System.IO;
using System.Reflection;
using System.Threading;
[assembly:AssemblyVersion("1.5.0.0")]
class Fixture {
 static void Main(string[] args){
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"launched.txt"),String.Join("|",args));
  int i=Array.IndexOf(args,"--update-receipt");
  if(i>=0){File.WriteAllText(args[i+1],"{\"version\":\"1.5.0\"}");Thread.Sleep(5000);}
 }
}
'@
$newSource=Join-Path $sandbox 'New.cs';Set-Content -LiteralPath $newSource -Value $stub -Encoding utf8
$oldSource=Join-Path $sandbox 'Old.cs';Set-Content -LiteralPath $oldSource -Value ($stub.Replace('1.5.0','1.4.1')) -Encoding utf8
$badSource=Join-Path $sandbox 'Bad.cs';Set-Content -LiteralPath $badSource -Value 'using System;using System.IO;using System.Reflection; [assembly:AssemblyVersion("1.5.0.0")] class Bad {static void Main(string[] args){int i=Array.IndexOf(args,"--settings");if(i>=0)File.WriteAllText(args[i+1],"mutated-by-failed-start");}}' -Encoding utf8
foreach($name in @('New','Old','Bad')){
 & $compiler /nologo /target:winexe "/out:$sandbox\$name.exe" (Join-Path $sandbox ($name+'.cs'))
 if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed.'}
}
$test=@'
using System;
using System.IO;
using System.IO.Compression;
using System.Web.Script.Serialization;
class Check {
 static void Assert(bool v,string name){if(!v)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static bool Throws(Action action){try{action();return false;}catch{return true;}}
 static int Main(string[] args){try{
 string root=args[0],zip=Path.Combine(root,"package.zip");
 using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create)){archive.CreateEntryFromFile(Path.Combine(root,"New.exe"),"CodexQuotaMonitor.exe");var extra=archive.CreateEntry("../../unexpected.txt");using(var writer=new StreamWriter(extra.Open()))writer.Write("fixture");}
 string hash=AppUpdater.Hash(zip);string output=AppUpdater.ExtractVerified(zip,hash,"1.5.0",Path.Combine(root,"extract"));Assert(File.Exists(output),"valid package hash and executable version accepted");
 Assert(!File.Exists(Path.Combine(root,"unexpected.txt")),"archive extras cannot escape extraction directory");
 Assert(Throws(()=>AppUpdater.ExtractVerified(zip,new String('0',64),"1.5.0",Path.Combine(root,"corrupt"))),"corrupted package rejected");
 Assert(Throws(()=>AppUpdater.ExtractVerified(zip,hash,"1.5.1",Path.Combine(root,"wrong"))),"wrong executable version rejected");
 string url="https://github.com/jiyan1989/codex-quota-manager/releases/download/v1.5.0/CodexQuotaManager-v1.5.0-windows-x64.zip";
 var json=new JavaScriptSerializer();string data=json.Serialize(new {tag_name="v1.5.0",draft=false,prerelease=false,assets=new[]{new{name="CodexQuotaManager-v1.5.0-windows-x64.zip",browser_download_url=url,digest="sha256:"+hash}}});
 Assert(AppUpdater.ParseRelease(data).Version=="1.5.0","official repository metadata parsed");
 Assert(Throws(()=>AppUpdater.ParseRelease(data.Replace("github.com/","example.com/"))),"foreign download source rejected");
 Assert(Throws(()=>AppUpdater.ParseRelease(data.Replace("sha256:","missing:"))),"unverified release cannot install");
 Assert(AppUpdater.ParseRelease(data.Replace("\"prerelease\":false","\"prerelease\":true"))==null,"prereleases ignored");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
'@
Set-Content -LiteralPath (Join-Path $sandbox 'Check.cs') -Value $test -Encoding utf8
& $compiler /nologo /target:exe "/out:$sandbox\Check.exe" /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "$root\src\AppUpdater.cs" "$sandbox\Check.cs"
if($LASTEXITCODE -ne 0){throw 'Updater check compilation failed.'}
& (Join-Path $sandbox 'Check.exe') $sandbox
if($LASTEXITCODE -ne 0){throw 'Package validation check failed.'}
foreach($case in @('success','rollback','corrupt')){
 $folder=Join-Path $sandbox $case;[void][IO.Directory]::CreateDirectory($folder)
 $target=Join-Path $folder 'CodexQuotaMonitor.exe';Copy-Item -LiteralPath (Join-Path $sandbox 'Old.exe') -Destination $target
 $payload=Join-Path $sandbox $(if($case -eq 'rollback'){'Bad.exe'}else{'New.exe'})
 $settings=Join-Path $folder '设置 fixture.json';[IO.File]::WriteAllText($settings,'{"manualHold":true,"weeklyFloor":37}')
 $settingsBackup=Join-Path $folder 'settings-before.json';Copy-Item -LiteralPath $settings -Destination $settingsBackup
 $beforeSettings=(Get-FileHash -LiteralPath $settings).Hash;$oldHash=(Get-FileHash -LiteralPath $target).Hash
 $request=@{target=$target;payload=$payload;payloadHash=$(if($case -eq 'corrupt'){'0'*64}else{(Get-FileHash -LiteralPath $payload).Hash});previousHash=$oldHash;parentPid=0;version='1.5.0';settingsPath=$settings;settingsBackup=$settingsBackup;restartArgs=@('--no-start','--settings',$settings)}
 $requestPath=Join-Path $folder 'request.json';$request|ConvertTo-Json|Set-Content -LiteralPath $requestPath -Encoding utf8
 & (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $root 'monitor\update-app.ps1') -RequestPath $requestPath -BootTimeoutSeconds 2
 $result=Get-Content -Raw -LiteralPath (Join-Path $folder 'result.json')|ConvertFrom-Json
 if($case -eq 'success'){
  if($LASTEXITCODE -ne 0 -or $result.status -ne 'installed' -or (Get-FileHash -LiteralPath $target).Hash -eq $oldHash){throw 'Successful replacement check failed.'}
  $arguments=Get-Content -Raw -LiteralPath (Join-Path $folder 'launched.txt')
  if($arguments -notmatch '--no-start' -or !$arguments.Contains($settings)){throw 'Restart state or path quoting lost.'}
 }else{
  if((Get-FileHash -LiteralPath $target).Hash -ne $oldHash){throw 'Old executable was not preserved.'}
  $expected=if($case -eq 'rollback'){'rolled_back'}else{'cancelled'}
  if($result.status -ne $expected){throw "Unexpected result: $case $($result.status)"}
 }
 if((Get-FileHash -LiteralPath $settings).Hash -ne $beforeSettings){throw 'Update altered private settings.'}
 Write-Host "PASS isolated updater $case preserves settings and requested monitoring state"
}
Write-Host 'Updater checks passed. No installed application, real settings or chats were updated.'
