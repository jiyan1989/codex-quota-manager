#requires -Version 7.0
# Isolated WinForms regression: no Codex connections or real settings are used.
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$sandbox=Join-Path ([IO.Path]::GetTempPath()) ('quota-ui-check-'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($sandbox)
$source=Get-Content -Raw -LiteralPath (Join-Path $root 'src\QuotaMonitor.cs')
$source=$source.Replace('string appData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaManager");','string appData=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sandbox");')
$source=$source.Replace('return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Codex 任务托管.lnk");','return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-disabled.lnk");')
$begin=$source.IndexOf('    void StartWorker() {')
$end=$source.IndexOf('    void WorkerOutput(', $begin)
if($begin -lt 0 -or $end -lt 0){throw 'Cannot isolate backend startup.'}
$source=$source.Substring(0,$begin)+'    void StartWorker() {stopRequested=false;worker=Process.GetCurrentProcess();}'+[Environment]::NewLine+$source.Substring($end)
Set-Content -LiteralPath (Join-Path $sandbox 'QuotaMonitor.cs') -Value $source -Encoding utf8
$harness=@'
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Windows.Forms;
class ActionCheck {
 static object Get(object f,string n){return f.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(f);}
 static void Set(object f,string n,object v){f.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(f,v);}
 static void Call(object f,string n,params object[] a){f.GetType().GetMethod(n,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,a);}
 static Button Find(Control c){foreach(Control x in c.Controls){if(x is Button && x.Text.Contains("继续任务"))return (Button)x;var b=Find(x);if(b!=null)return b;}return null;}
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static System.Collections.Generic.IEnumerable<Control> All(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(Control child in All(c))yield return child;}}
 [STAThread] static int Main(string[] args){try{if(args.Length==2 && args[0]=="--wake-event")return Program.SignalWakeEvent(args[1])?0:2;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
 var root=AppDomain.CurrentDomain.BaseDirectory;var state=Path.Combine(root,"state");Directory.CreateDirectory(state);
 File.WriteAllText(Path.Combine(root,"settings.json"),"{\"quotaBasis\":\"fiveHour\",\"pauseRemainingPercent\":12,\"resumeRemainingPercent\":31,\"weeklyPauseRemainingPercent\":44,\"autoResumeEnabled\":false}");
 using(var f=new MonitorWindow(new[]{"--no-start","--settings",Path.Combine(root,"settings.json"),"--state-directory",state})){
 f.Show();Application.DoEvents();((Timer)Get(f,"timer")).Stop();
 var grid=(DataGridView)Get(f,"chats");Set(f,"loading",true);int row=grid.Rows.Add(false,"Fixture task","目标已暂停","保持手动暂停");grid.Rows[row].Tag="00000000-0000-0000-0000-000000000001";grid.CurrentCell=grid.Rows[row].Cells[1];Set(f,"loading",false);
 Assert((bool)Get(f,"stopRequested"),"no-start window represents stopped monitoring");
 Assert(!All(f).Any(c=>c is ComboBox),"main screen has no quota basis dropdown");
 Assert(!All(f).OfType<Button>().Any(c=>c.Text.Contains("监控")),"main screen has no start or stop monitoring buttons");
 using(var settings=(Form)f.GetType().GetMethod("CreateSettingsDialog",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,null)){
 settings.Show(f);Application.DoEvents();Assert(!All(settings).Any(c=>c is ChoiceCard),"old mutually exclusive quota modes are removed");
 var pauseInput=All(settings).OfType<NumericUpDown>().Single(c=>c.Name=="fiveHourPause");var resumeInput=All(settings).OfType<NumericUpDown>().Single(c=>c.Name=="fiveHourResume");var weekInput=All(settings).OfType<NumericUpDown>().Single(c=>c.Name=="weeklyFloor");Assert(pauseInput.Value==12 && resumeInput.Value==31 && weekInput.Value==44,"migration preserves all three original numeric settings");Assert(((MonitorSettings)Get(f,"config")).quotaBasis=="layered" && Directory.GetFiles(root,"settings.json.before-layered-*.bak").Length==1,"legacy mode migration creates a settings backup");
 weekInput.Value=37;pauseInput.Value=12;Assert(File.ReadAllText(Path.Combine(root,"settings.json")).Contains("\"weeklyPauseRemainingPercent\":37"),"weekly floor is independent of five-hour pause");
 resumeInput.Value=11;Assert(pauseInput.Value==10 && weekInput.Value==37,"five-hour hysteresis adjustment does not overwrite weekly floor");
 Assert(All(settings).OfType<Button>().Count(c=>c.Name=="updateAction")==1 && !All(settings).OfType<Button>().Any(c=>c.Text=="关闭"),"settings uses one adaptive update action and the title-bar close button");
 pauseInput.Value=10;resumeInput.Value=20;weekInput.Value=10;
 foreach(float scale in new[]{1f,1.25f,1.2f}){if(scale!=1f)settings.Scale(new System.Drawing.SizeF(scale,scale));settings.PerformLayout();Application.DoEvents();foreach(var toggle in All(settings).OfType<ToggleSwitch>()){Control c=toggle;while(c.Parent!=null && c.Parent!=settings){Assert(c.Parent.ClientRectangle.Contains(c.Bounds),"setting switch remains inside each ancestor at layout scale "+scale);c=c.Parent;}}}settings.Close();}
 var wakeName=Program.WakeEventName(new[]{"--settings",Path.Combine(root,"settings.json")});
 using(var child=Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--wake-event "+wakeName){UseShellExecute=false,CreateNoWindow=true})){child.WaitForExit();Assert(child.ExitCode==0,"second process signals existing window wake event");}
 Call(f,"ProcessWakeRequest");Assert(Get(f,"worker")!=null && !(bool)Get(f,"stopRequested"),"wake starts stopped background monitor");
 Set(f,"stopRequested",true);((System.Threading.EventWaitHandle)Get(f,"wakeEvent")).Set();Call(f,"ProcessWakeRequest");Assert((bool)Get(f,"wakePending"),"wake is retained while previous background is stopping");
 Set(f,"worker",null);Call(f,"ProcessWakeRequest");Assert(Get(f,"worker")!=null && !(bool)Get(f,"wakePending"),"retained wake starts backend after shutdown");
 Set(f,"worker",null);Set(f,"stopRequested",true);
 Find(f).PerformClick();
 var commands=Path.Combine(state,"commands");Assert(Directory.GetFiles(commands,"*.json").Length==1,"Continue button queues one command while stopped");
 Assert(!(bool)Get(f,"stopRequested") && Get(f,"worker")!=null,"Continue starts isolated backend without manual monitoring step");
 Assert(((Label)Get(f,"taskState")).Text.Contains("已登记"),"queue result is visible on main window");
 Call(f,"UpdateStatusPanel");Assert(((Label)Get(f,"taskState")).Text.Contains("已登记"),"timer status refresh preserves immediate feedback");
 Call(f,"ProjectAction","AutoContinue");Assert(Directory.GetFiles(commands,"*.json").Length==2,"AutoContinue checkbox update does not recursively queue twice");
 Assert((bool)grid.Rows[row].Cells[0].Value,"AutoContinue selects managed task");
 Call(f,"ProjectAction","CancelAuto");Assert(Directory.GetFiles(commands,"*.json").Length==3 && !(bool)grid.Rows[row].Cells[0].Value,"CancelAuto queues once and deselects task");
 Set(f,"stopRequested",true);Call(f,"ProjectAction","Start");Assert(Directory.GetFiles(commands,"*.json").Length==3,"actual in-progress shutdown still blocks new command");
 Assert(((Label)Get(f,"taskState")).Text.Contains("正在停止"),"shutdown block reason is visible");
 Set(f,"worker",null);Call(f,"ProjectAction","Start");Assert(Directory.GetFiles(commands,"*.json").Length==4,"right-click Start works after shutdown finishes");
 ((CheckBox)Get(f,"observe")).Checked=true;Call(f,"ProjectAction","Start");Assert(Directory.GetFiles(commands,"*.json").Length==4,"read-only mode still blocks task controls");
 Assert(((Label)Get(f,"taskState")).Text.Contains("只读"),"read-only reason is visible");
 var stoppedArgs=(string[])f.GetType().GetMethod("UpdateRestartArguments",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{false});var runningArgs=(string[])f.GetType().GetMethod("UpdateRestartArguments",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,new object[]{true});Assert(stoppedArgs.Contains("--no-start") && !runningArgs.Contains("--no-start"),"updater restart respects monitoring on or off");
 File.WriteAllText(Path.Combine(state,"state.json"),"{\"lastCheckedAt\":\""+DateTime.UtcNow.ToString("o")+"\",\"counts\":{\"active\":8,\"ordinaryActive\":8,\"managedActive\":1,\"managedPaused\":1,\"managedPending\":0},\"chats\":[]}");Call(f,"UpdateScreen");Assert(((Label)Get(f,"counts")).Text.Contains("托管运行 1") && !((Label)Get(f,"counts")).Text.Contains("托管运行 8"),"main counts do not mix unmanaged running chats");

 }return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
'@
Set-Content -LiteralPath (Join-Path $sandbox 'ActionCheck.cs') -Value $harness -Encoding utf8
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /main:ActionCheck /platform:x64 /codepage:65001 "/out:$sandbox\ActionCheck.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Core.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll "/win32icon:$root\assets\quota-monitor.ico" "/resource:$root\monitor\quota-monitor.ps1,quota-monitor.ps1" "$sandbox\QuotaMonitor.cs" "$root\src\ModernControls.cs" "$root\src\LogWindow.cs" "$root\src\AssemblyInfo.cs" "$root\src\AppUpdater.cs" "$root\src\UpdateUi.cs" "/resource:$root\monitor\update-app.ps1,update-app.ps1" "$sandbox\ActionCheck.cs"
if($LASTEXITCODE -ne 0){throw 'UI check compilation failed.'}
& (Join-Path $sandbox 'ActionCheck.exe')
if($LASTEXITCODE -ne 0){throw 'UI action regression failed.'}
Write-Host 'UI action regression passed. No real backend, chats, startup settings or account were controlled.'

