using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

partial class MonitorWindow {
    readonly Button updateNotice=new ActionButton{Text="发现新版",Visible=false,Dock=DockStyle.Fill,Margin=new Padding(0,9,8,10)};
    string updateDirectory,updateCache,updateStatus="",lastUpdateResult="";
    string[] originalArguments;
    UpdateRelease availableRelease;
    bool checkingUpdate,installingUpdate;
    DateTime nextUpdateCheck=DateTime.MinValue;
    event Action UpdateStatusChanged;
    void InitializeUpdates(string[] args,string appData){
        originalArguments=args;updateDirectory=Path.Combine(appData,"updates");updateCache=Path.Combine(updateDirectory,"check-state.json");
        updateStatus="可检查新版；更新前校验，启动失败自动回退。";
        var cached=AppUpdater.CachedRelease(updateCache);
        if(cached!=null && new Version(cached.Version)>new Version(AppUpdater.CurrentVersion)){availableRelease=cached;updateNotice.Visible=true;updateNotice.Text="更新至 v"+cached.Version;updateStatus="发现 v"+cached.Version+"，可一键更新";}
        updateNotice.Click+=delegate{OpenSettings();};
        Shown+=delegate{
            string receipt=Argument(args,"--update-receipt");
            if(receipt!=null){try{AppUpdater.WriteObject(receipt,new {version=AppUpdater.CurrentVersion,startedAt=DateTime.UtcNow.ToString("o")});}catch(IOException){}}
            if(!smokeTest && Argument(args,"--settings")==null && config.checkUpdatesAutomatically)BeginInvoke(new Action(delegate{CheckForUpdates(false);}));
        };
    }
    void OnUi(Action action){if(IsDisposed || !IsHandleCreated)return;try{BeginInvoke(action);}catch(InvalidOperationException){}}
    void SetUpdateStatus(string text){updateStatus=text;if(UpdateStatusChanged!=null)UpdateStatusChanged();}
    void CheckForUpdates(bool manual){
        if(checkingUpdate || installingUpdate || !manual && !AppUpdater.AutoCheckDue(updateCache))return;
        checkingUpdate=true;SetUpdateStatus("正在检查 GitHub 最新版本…");
        ThreadPool.QueueUserWorkItem(delegate{
            try{
                var release=AppUpdater.Check(updateDirectory);
                AppUpdater.WriteObject(updateCache,new {checkedAt=DateTime.UtcNow.ToString("o"),release=release});
                OnUi(delegate{
                    checkingUpdate=false;availableRelease=release!=null && new Version(release.Version)>new Version(AppUpdater.CurrentVersion)?release:null;
                    updateNotice.Visible=availableRelease!=null;
                    updateNotice.Text=availableRelease==null?"发现新版":"更新至 v"+availableRelease.Version;
                    SetUpdateStatus(availableRelease==null?"已是最新版 v"+AppUpdater.CurrentVersion:"发现 v"+availableRelease.Version+"，可一键更新");
                    if(!manual && availableRelease!=null){tray.BalloonTipTitle="Codex 任务托管有新版本";tray.BalloonTipText="点击右上角更新入口，安装 v"+availableRelease.Version;tray.ShowBalloonTip(5000);}
                });
            }catch(Exception error){OnUi(delegate{checkingUpdate=false;SetUpdateStatus("检查更新失败，可稍后重试："+error.Message);});}
        });
    }
    Control CreateUpdatePanel(Form dialog){
        var panel=Rows(24,28,38,28);panel.BackColor=dialog.BackColor;
        panel.Controls.Add(new Label{Text="当前版本 v"+AppUpdater.CurrentVersion+" · 更新保留设置和任务记录",Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,0);
        var status=new Label{Text=updateStatus,Dock=DockStyle.Fill,AutoEllipsis=true,ForeColor=Theme.Ink};panel.Controls.Add(status,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};
        var check=MakeButton("检查更新",delegate{CheckForUpdates(true);});
        var install=MakeButton("一键更新",delegate{InstallAvailableUpdate();});buttons.Controls.Add(check);buttons.Controls.Add(install);panel.Controls.Add(buttons,0,2);
        var automatic=new CheckBox{Text="启动时及每天自动检查（仅提醒，不自动安装）",Checked=config.checkUpdatesAutomatically,AutoSize=true,Margin=new Padding(0,5,0,0)};
        automatic.CheckedChanged+=delegate{config.checkUpdatesAutomatically=automatic.Checked;nextUpdateCheck=DateTime.MinValue;WriteSettings();};panel.Controls.Add(automatic,0,3);
        Action changed=delegate{if(dialog.IsDisposed)return;status.Text=updateStatus;check.Enabled=!checkingUpdate&&!installingUpdate;install.Enabled=availableRelease!=null&&!checkingUpdate&&!installingUpdate;};
        UpdateStatusChanged+=changed;dialog.FormClosed+=delegate{UpdateStatusChanged-=changed;};changed();return panel;
    }
    string[] UpdateRestartArguments(bool restartMonitoring){
        var result=new List<string>();
        foreach(string key in new[]{"--settings","--state-directory","--log-directory"}){string value=Argument(originalArguments,key);if(value!=null){result.Add(key);result.Add(value);}}
        if(!restartMonitoring)result.Add("--no-start");
        if(!Visible || WindowState==FormWindowState.Minimized)result.Add("--tray");
        return result.ToArray();
    }
    void InstallAvailableUpdate(){
        if(availableRelease==null || installingUpdate || checkingUpdate)return;
        var release=availableRelease;
        try{string probe=Path.Combine(Path.GetDirectoryName(Application.ExecutablePath),".update-probe-"+Guid.NewGuid().ToString("N"));File.WriteAllText(probe,"");File.Delete(probe);}
        catch(Exception e){SetUpdateStatus("程序目录不可写，保留当前版本："+e.Message);return;}
        installingUpdate=true;SetUpdateStatus("正在下载并校验 v"+release.Version+"，后台暂时继续运行…");
        ThreadPool.QueueUserWorkItem(delegate{
            try{
                string stage=Path.Combine(updateDirectory,release.Version+"-"+Guid.NewGuid().ToString("N"));
                string payload=AppUpdater.Prepare(release,stage);
                OnUi(delegate{
                    try{
                        string helper=Path.Combine(stage,"update-app.ps1");
                        using(var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("update-app.ps1"))using(var file=File.Create(helper)){source.CopyTo(file);}
                        if(File.Exists(settingsPath))File.Copy(settingsPath,Path.Combine(stage,"settings-before.json"),true);
                        string request=Path.Combine(stage,"request.json");
                        string[] restartArgs=UpdateRestartArguments(WorkerRunning()&&!stopRequested);
                        AppUpdater.WriteObject(request,new {target=Application.ExecutablePath,payload=payload,payloadHash=AppUpdater.Hash(payload),previousHash=AppUpdater.Hash(Application.ExecutablePath),parentPid=Process.GetCurrentProcess().Id,version=release.Version,restartArgs=restartArgs,settingsPath=settingsPath,settingsBackup=Path.Combine(stage,"settings-before.json")});
                        File.WriteAllText(Path.Combine(updateDirectory,"latest-request.txt"),stage);
                        var helperStart=new ProcessStartInfo(FindRuntime(),"-NoProfile -File "+Quote(helper)+" -RequestPath "+Quote(request)){UseShellExecute=false,CreateNoWindow=true};
                        Process.Start(helperStart);SetUpdateStatus("校验通过，正在退出并安装；失败将恢复旧版。");RequestExit();
                    }catch(Exception e){installingUpdate=false;SetUpdateStatus("无法安装，保留当前版本："+e.Message);}
                });
            }catch(Exception e){OnUi(delegate{installingUpdate=false;SetUpdateStatus("更新失败，保留当前版本："+e.Message);});}
        });
    }
    void MaybeCheckUpdates(){
        if(smokeTest || originalArguments==null || Argument(originalArguments,"--settings")!=null || !config.checkUpdatesAutomatically || DateTime.UtcNow<nextUpdateCheck)return;
        nextUpdateCheck=DateTime.UtcNow.AddHours(1);CheckForUpdates(false);
    }
    void RefreshUpdateResult(){
        MaybeCheckUpdates();
        if(updateDirectory==null)return;
        try{
            string pointer=Path.Combine(updateDirectory,"latest-request.txt");if(!File.Exists(pointer))return;
            string path=Path.Combine(File.ReadAllText(pointer),"result.json");if(!File.Exists(path))return;
            string text=File.ReadAllText(path);if(text==lastUpdateResult)return;lastUpdateResult=text;
            var result=AppUpdater.ReadObject(path);object detail;if(result.TryGetValue("detail",out detail))SetUpdateStatus(Convert.ToString(detail));
        }catch(IOException){}
    }
}
