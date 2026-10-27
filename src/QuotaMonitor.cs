using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class MonitorSettings {
    public int pollSeconds = 30;
    public double pauseRemainingPercent = 10;
    public double resumeRemainingPercent = 20;
    public bool autoResumeEnabled = true;
    public string quotaBasis = "layered";
    public bool checkUpdatesAutomatically = true;
    public bool trayBackgroundEnabled = true;
    public double weeklyPauseRemainingPercent = 10;
    public bool observeOnly = false;
    public bool resumeAll = false;
    public string[] resumeThreadIds = new string[0];
    public string[] resumeExcludedThreadIds = new string[0];
    public bool autoResetEnabled = false;
    public double resetWeeklyRemainingPercent = 1;
    public double nearLimitRemainingPercent = 10;
    public int nearLimitPollSeconds = 10;
    public int maxConsecutiveFailures = 0;
    public bool showAllProjects = false;
    public string stateDirectory;
    public string pwshPath;
}

static class Program {
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern IntPtr FindWindow(string className,string title);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    public static string WakeEventName(string[] args) {
        int index=Array.IndexOf(args,"--settings");
        string suffix=index>=0 && index+1<args.Length ? "-"+BitConverter.ToString(System.Security.Cryptography.SHA256.Create().ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(args[index+1])))).Replace("-","").Substring(0,16) : "";
        return "Local\\CodexQuotaManagerWake"+suffix+(args.Contains("--smoke-test")?"-ReadonlyCheck":"");
    }
    public static bool SignalWakeEvent(string name) {
        for(int attempt=0;attempt<20;attempt++){
            try {using(var signal=EventWaitHandle.OpenExisting(name)){signal.Set();return true;}}
            catch(WaitHandleCannotBeOpenedException){Thread.Sleep(100);}
        }
        return false;
    }
    [STAThread] static void Main(string[] args) {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        bool first;
        using (var singleton = new Mutex(true, "Local\\CodexQuotaMonitorDesktop"+(args.Contains("--smoke-test") ? "-ReadonlyCheck" : ""), out first)) {
            if (!first) { if((args.Contains("--start-monitor") || args.Contains("--tray") && !args.Contains("--no-start")) && !SignalWakeEvent(WakeEventName(args)))Environment.ExitCode=1; if(!args.Contains("--tray")) {var existing=FindWindow(null,"Codex 任务托管");if(existing!=IntPtr.Zero){ShowWindow(existing,9);SetForegroundWindow(existing);}} return; }
            try { Application.Run(new MonitorWindow(args)); }
            catch (Exception e) { MessageBox.Show("无法打开额度监控器：" + e.Message, "额度监控器"); }
        }
    }
}

class QuotaMeter : Control {
    public int Value;
    public Color Accent=Color.FromArgb(30,133,125);
    public QuotaMeter(){DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e); e.Graphics.Clear(Color.White);
        using(var background=new SolidBrush(Color.FromArgb(231,238,245))) e.Graphics.FillRectangle(background,0,2,Width,6);
        using(var fill=new SolidBrush(Accent)) e.Graphics.FillRectangle(fill,0,2,Width*Math.Max(0,Math.Min(100,Value))/100,6);
    }
}
partial class MonitorWindow : Form {
    readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16*1024*1024, RecursionLimit = 150 };
    readonly string stateDir, settingsPath, backendPath, checkDir, logDir;
    readonly bool smokeTest;
    readonly QuotaCard five=new QuotaCard("五小时额度"), week=new QuotaCard("周额度");
    readonly Label mode=new Label(), freshness=new Label(), counts=new Label(), cardInfo=new Label(), resetStatus=new Label(), listTitle=new Label();
    readonly NumericUpDown poll=new NumericUpDown(), threshold=new NumericUpDown(), resetThreshold=new NumericUpDown(), nearThreshold=new NumericUpDown(), nearPoll=new NumericUpDown();
    readonly CheckBox observe=new CheckBox(), resumeAll=new CheckBox(), autoReset=new ToggleSwitch();
    readonly CheckBox stopOnFailures=new CheckBox();
    readonly CheckBox showAll=new CheckBox();
    readonly CheckBox autoResume=new ToggleSwitch(), loginStart=new ToggleSwitch();
    readonly NumericUpDown resumeThreshold=new NumericUpDown(), weeklyFloor=new NumericUpDown();
    readonly Label policySummary=new Label();
    readonly ContextMenuStrip projectMenu=new ContextMenuStrip();
    readonly NumericUpDown failureLimit=new NumericUpDown();
    readonly DataGridView chats=new DataGridView();
    readonly TextBox log=new TextBox();
    readonly TextBox search=new SearchBox();
    readonly Label connectionState=new Label(), monitorState=new Label(), quotaFreshState=new Label(), taskState=new Label();
    readonly Label selectedTask=new Label();
    string settingsSeen="";
    string runtimeFailure;
    string cachedRuntime;
    DateTime stateStamp=DateTime.MinValue, settingsStamp=DateTime.MinValue;
    long stateLength=-1, settingsLength=-1;
    bool logDirty;
    int restartFailures;
    long stateRevision;
    DateTime lastStateRead=DateTime.MinValue,lastSettingsRead=DateTime.MinValue;
    DateTime nextHiddenRefresh=DateTime.MinValue;
    readonly object outputLock=new object();
    readonly Queue<string> pendingOutput=new Queue<string>();
    readonly Queue<string> logLines=new Queue<string>();
    readonly Dictionary<string,DateTime> recentActions=new Dictionary<string,DateTime>();
    readonly EventWaitHandle wakeEvent;
    bool wakePending;
    string captureDir;
    readonly Button apply=new Button(), refresh=new Button();
    readonly Label protectionStatus=new Label();
    DateTime nextProtectionRestart=DateTime.MinValue;
    readonly NotifyIcon tray=new NotifyIcon();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    MonitorSettings config;
    readonly MonitorSettings initialConfig;
    Process worker;
    LogWindow viewer;
    Dictionary<string,object> lastState;
    string lastChecked="", lastRendered="", actionFeedback="";
    DateTime actionFeedbackUntil=DateTime.MinValue;
    DateTime startedAt=DateTime.Now;
    bool loading=true, stopRequested=false, exiting=false, allowClose=false, checkConfigured=false, checkFinished=false, choicesPassed=false, quotaDisplayPassed=false;

    public MonitorWindow(string[] args) {
        smokeTest=args.Contains("--smoke-test"); captureDir=Argument(args,"--capture-directory");
        checkDir=Argument(args,"--check-directory");
        string appData=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexQuotaManager");
        string legacy=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"..","..","work","quota-monitor-v1","all-Auto"));
        settingsPath=Argument(args,"--settings") ?? Path.Combine(appData,"settings.json");
        wakeEvent=new EventWaitHandle(false,EventResetMode.AutoReset,Program.WakeEventName(args));
        config=File.Exists(settingsPath) ? json.Deserialize<MonitorSettings>(ReadShared(settingsPath)) : new MonitorSettings();
        if(config.quotaBasis!="layered"){
            if(File.Exists(settingsPath))File.Copy(settingsPath,settingsPath+".before-layered-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+".bak",true);
            config.quotaBasis="layered";
        }
        stateDir=Argument(args,"--state-directory") ?? config.stateDirectory ?? (Directory.Exists(legacy) ? legacy : Path.Combine(appData,"state"));
        logDir=Argument(args,"--log-directory") ?? stateDir;
        config.stateDirectory=stateDir;
        Directory.CreateDirectory(stateDir); Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)); Directory.CreateDirectory(appData);
        backendPath=Path.Combine(appData,smokeTest ? "backend-check.ps1" : "backend.ps1");
        using (var resource=Assembly.GetExecutingAssembly().GetManifestResourceStream("quota-monitor.ps1"))
        using (var destination=File.Create(backendPath)) { resource.CopyTo(destination); }
        if(config.resumeThreadIds==null) config.resumeThreadIds=new string[0];
        if(config.resumeExcludedThreadIds==null) config.resumeExcludedThreadIds=new string[0];
        if (smokeTest) config.observeOnly=true;
        initialConfig=json.Deserialize<MonitorSettings>(json.Serialize(config));
        Text="Codex 任务托管"; Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        ClientSize=new Size(1240,930); MinimumSize=new Size(1120,880); StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",10); ForeColor=Color.FromArgb(38,53,73); BackColor=Color.FromArgb(242,246,251); AutoScaleMode=AutoScaleMode.Dpi;
        BuildWindow();
        poll.Value=Math.Max(1,Math.Min(3600,config.pollSeconds)); threshold.Value=(decimal)Math.Max(0,Math.Min(99,config.pauseRemainingPercent));weeklyFloor.Value=(decimal)Math.Max(0,Math.Min(99,config.weeklyPauseRemainingPercent));
        observe.Checked=config.observeOnly; resumeAll.Checked=config.resumeAll;
        autoReset.Checked=config.autoResetEnabled; resetThreshold.Value=(decimal)Math.Max(0,Math.Min(99,config.resetWeeklyRemainingPercent));
        nearThreshold.Value=(decimal)Math.Max(0,Math.Min(100,config.nearLimitRemainingPercent));nearPoll.Value=Math.Max(1,Math.Min(3600,config.nearLimitPollSeconds));
        stopOnFailures.Checked=config.maxConsecutiveFailures>0;failureLimit.Value=Math.Max(1,Math.Min(9999,config.maxConsecutiveFailures>0 ? config.maxConsecutiveFailures : 5));failureLimit.Enabled=stopOnFailures.Checked;
        showAll.Checked=config.showAllProjects;
        resumeThreshold.Value=(decimal)Math.Max(1,Math.Min(100,config.resumeRemainingPercent));autoResume.Checked=config.autoResumeEnabled;loginStart.Checked=File.Exists(StartupShortcut());
        loading=false;
        WriteSettings();
        InitializeUpdates(args,appData);
        var trayMenu=new ContextMenuStrip();
        trayMenu.Items.Add("打开窗口",null,delegate { RestoreWindow(); });
        trayMenu.Items.Add("退出管家（停止额度保护）",null,delegate { RequestExit(); });
        tray.Icon=Icon; tray.Text="Codex 任务托管"; tray.ContextMenuStrip=trayMenu; tray.Visible=true;
        tray.DoubleClick+=delegate { RestoreWindow(); };
        Resize+=delegate { if(WindowState==FormWindowState.Minimized && !smokeTest && config.trayBackgroundEnabled) Hide(); };
        FormClosing+=delegate(object sender,FormClosingEventArgs e) {
            if(allowClose) return;
            e.Cancel=true;
            if(e.CloseReason==CloseReason.WindowsShutDown) RequestExit();
            else if(config.trayBackgroundEnabled) { Hide(); } else RequestExit();
        };
        Shown+=delegate { if(!args.Contains("--no-start"))StartWorker();else stopRequested=true; if(args.Contains("--tray")) Hide(); };
        timer.Interval=1000; timer.Tick+=delegate { FlushOutput(); if(Visible || smokeTest || exiting || DateTime.UtcNow>=nextHiddenRefresh){UpdateScreen();nextHiddenRefresh=DateTime.UtcNow.AddSeconds(5);} ProcessWakeRequest(); KeepProtectionRunning(); if(captureDir!=null && lastState!=null && Get(lastState,"lastQuota")!=null) { try {Directory.CreateDirectory(captureDir);using(var image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(Point.Empty,Size));image.Save(Path.Combine(captureDir,"live-window.png"));}captureDir=null;}catch(Exception e){AppendLog("预览保存失败："+e.Message);} } }; timer.Start();
        FormClosed+=delegate { timer.Dispose(); wakeEvent.Dispose();tray.Visible=false; tray.Dispose(); DisposeWorker(); };
    }
    static string Argument(string[] args,string key) { int i=Array.IndexOf(args,key); return i>=0 && i+1<args.Length ? args[i+1] : null; }
    static Dictionary<string,object> Obj(object value) { return value as Dictionary<string,object> ?? new Dictionary<string,object>(); }
    static object Get(Dictionary<string,object> value,string key) { object result; return value.TryGetValue(key,out result) ? result : null; }
    static string Str(object value) { return value==null ? "" : Convert.ToString(value); }
    static IEnumerable<object> Items(object value) { return value as IEnumerable<object> ?? new object[0]; }
    static string ReadShared(string path) {
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
        using(var reader=new StreamReader(stream,Encoding.UTF8)) return reader.ReadToEnd();
    }
    void AtomicJson(string path,object value) {
        string temp=path+".gui.tmp"; File.WriteAllText(temp,json.Serialize(value),new UTF8Encoding(false));
        for(int attempt=0;;attempt++) {
            try { if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path); break; }
            catch(IOException) { if(attempt==3) throw; Thread.Sleep(30); }
        }
    }
    Button MakeButton(string text,EventHandler action) {
        var button=new Button {Text=text,AutoSize=false,Size=new Size(TextRenderer.MeasureText(text,Font).Width+20,36),FlatStyle=FlatStyle.Flat,BackColor=Color.White,Margin=new Padding(0,3,9,0)};
        button.FlatAppearance.BorderColor=Color.FromArgb(195,207,215); button.Click+=action; return button;
    }
    Label Inline(string text) { return new Label {Text=text,AutoSize=true,Margin=new Padding(0,9,8,0)}; }
    Label Heading(string text,int size=15) {return new Label{Text=text,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",size,FontStyle.Bold,GraphicsUnit.Pixel),ForeColor=Theme.Ink,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};}
    TableLayoutPanel Rows(params int[] heights) {var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=heights.Length,Margin=new Padding(0),BackColor=Color.White};foreach(int height in heights)panel.RowStyles.Add(new RowStyle(height<0?SizeType.Percent:SizeType.Absolute,height<0?100:height));return panel;}
    void BuildWindow() {
        poll.Minimum=1;poll.Maximum=3600;threshold.Minimum=0;threshold.Maximum=99;threshold.DecimalPlaces=1;
        resumeThreshold.Minimum=1;resumeThreshold.Maximum=100;resumeThreshold.DecimalPlaces=1;
        weeklyFloor.Minimum=0;weeklyFloor.Maximum=99;weeklyFloor.DecimalPlaces=1;nearThreshold.Minimum=0;nearThreshold.Maximum=100;nearPoll.Minimum=1;nearPoll.Maximum=3600;resetThreshold.Minimum=0;resetThreshold.Maximum=99;failureLimit.Minimum=1;failureLimit.Maximum=9999;
        var root=Rows(64,-1,146);root.Padding=new Padding(18,10,18,14);root.BackColor=BackColor;
        var header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=new Padding(4,0,4,8),BackColor=BackColor};header.RowStyles.Add(new RowStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,52));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));
        var icon=new PictureBox{Image=Icon.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom,Dock=DockStyle.Fill,Margin=new Padding(2,7,8,8)};header.Controls.Add(icon,0,0);
        var names=Rows(31,25);names.BackColor=BackColor;names.Controls.Add(Heading("Codex 任务托管",23),0,0);names.Controls.Add(new Label{Text="让 Codex 持续为你工作",Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,1);header.Controls.Add(names,1,0);
        header.Controls.Add(updateNotice,2,0);header.Controls.Add(new ActionButton{Text="⚙  设置",Dock=DockStyle.Fill,Margin=new Padding(0,9,0,10)},3,0);((Button)header.GetControlFromPosition(3,0)).Click+=delegate{OpenSettings();};root.Controls.Add(header,0,0);
        var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,0,0,14),BackColor=BackColor};content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,62));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,38));
        var targets=new SurfacePanel{Dock=DockStyle.Fill,Margin=new Padding(0,0,14,0),Padding=new Padding(20)};
        var left=Rows(42,27,42,36,-1,36,54);var titleRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,BackColor=Color.White};titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));titleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,124));titleRow.Controls.Add(Heading("◉  托管目标",21),0,0);
        protectionStatus.Text="● 额度保护";protectionStatus.Dock=DockStyle.Fill;protectionStatus.TextAlign=ContentAlignment.MiddleRight;protectionStatus.ForeColor=Theme.Muted;titleRow.Controls.Add(protectionStatus,1,0);left.Controls.Add(titleRow,0,0);
        left.Controls.Add(new Label{Text="搜索原任务并勾选托管，支持按工作目录查找",Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,1);
        var searchRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,2,0,6),BackColor=Color.White};searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));searchRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));search.Dock=DockStyle.Fill;search.Font=new Font("Microsoft YaHei UI",11);search.Margin=new Padding(0,4,9,0);search.AccessibleName="搜索任务或工作目录";search.TextChanged+=delegate{RenderChats(true);};searchRow.Controls.Add(search,0,0);
        refresh.Text="↻";refresh.Dock=DockStyle.Fill;refresh.FlatStyle=FlatStyle.Flat;refresh.FlatAppearance.BorderColor=Theme.Border;refresh.Click+=delegate{if(WorkerRunning())Signal("Refresh");else StartWorker(true);};searchRow.Controls.Add(refresh,1,0);left.Controls.Add(searchRow,0,2);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,BackColor=Color.White};showAll.Text="显示全部任务";showAll.AutoSize=true;showAll.Margin=new Padding(0,5,14,0);showAll.CheckedChanged+=delegate{if(!loading){WriteSettings();RenderChats(true);}};filters.Controls.Add(showAll);listTitle.AutoSize=true;listTitle.ForeColor=Theme.Muted;listTitle.Margin=new Padding(0,6,0,0);filters.Controls.Add(listTitle);left.Controls.Add(filters,0,3);
        chats.Dock=DockStyle.Fill; chats.BackgroundColor=Color.White; chats.BorderStyle=BorderStyle.None; chats.AllowUserToAddRows=false; chats.AllowUserToDeleteRows=false;
        chats.RowHeadersVisible=false; chats.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.None; chats.RowTemplate.Height=34;
        chats.SelectionMode=DataGridViewSelectionMode.FullRowSelect; chats.MultiSelect=false; chats.EnableHeadersVisualStyles=false;
        chats.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(232,239,248); chats.ColumnHeadersDefaultCellStyle.ForeColor=Color.FromArgb(72,91,117);chats.ColumnHeadersHeight=38;
        chats.DefaultCellStyle.SelectionBackColor=Color.FromArgb(224,243,240); chats.DefaultCellStyle.SelectionForeColor=Color.Black;
        chats.GridColor=Color.FromArgb(231,236,241); chats.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(247,249,250);
        chats.Columns.Add(new DataGridViewCheckBoxColumn {Name="resume",HeaderText="托管",Width=58,SortMode=DataGridViewColumnSortMode.NotSortable});
        chats.Columns.Add(new DataGridViewTextBoxColumn {Name="title",HeaderText="任务名称",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=160});
        chats.Columns.Add(new DataGridViewTextBoxColumn {Name="runtime",HeaderText="执行状态",ReadOnly=true,Width=100});
        chats.Columns.Add(new DataGridViewTextBoxColumn {Name="phase",HeaderText="托管状态 / 等待原因",ReadOnly=true,Width=205});
        chats.CurrentCellDirtyStateChanged+=delegate { if(chats.IsCurrentCellDirty) chats.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        chats.CellValueChanged+=delegate(object sender,DataGridViewCellEventArgs e) {
            if(loading || e.RowIndex<0 || e.ColumnIndex!=0) return;
            var row=chats.Rows[e.RowIndex];string id=Str(row.Tag);if(id.Length==0)return;
            var ids=new HashSet<string>(config.resumeThreadIds);var excluded=new HashSet<string>(config.resumeExcludedThreadIds);
            if(Convert.ToBoolean(row.Cells[0].Value)){ids.Add(id);excluded.Remove(id);}else{ids.Remove(id);excluded.Add(id);}
            config.resumeThreadIds=ids.ToArray();config.resumeExcludedThreadIds=excluded.ToArray();SaveAndApply();
            chats.CurrentCell=row.Cells[1]; ProjectAction(Convert.ToBoolean(row.Cells[0].Value) ? "AutoContinue" : "CancelAuto");
        };
        projectMenu.Items.Add("手动暂停（保持暂停）",null,delegate {ProjectAction("Pause");});
        projectMenu.Items.Add("手动开始 / 继续",null,delegate {ProjectAction("Start");});
        projectMenu.Items.Add("登记自动继续（空闲 / 已结束也可）",null,delegate {ProjectAction("AutoContinue");});
        projectMenu.Items.Add("取消自动继续",null,delegate {ProjectAction("CancelAuto");});
        projectMenu.Opening+=delegate(object sender,System.ComponentModel.CancelEventArgs e){e.Cancel=chats.CurrentRow==null;foreach(ToolStripItem item in projectMenu.Items)item.Enabled=!observe.Checked && !(stopRequested && WorkerRunning());};
        chats.ContextMenuStrip=projectMenu;
        chats.CellMouseDown+=delegate(object sender,DataGridViewCellMouseEventArgs e){if(e.Button==MouseButtons.Right && e.RowIndex>=0){chats.ClearSelection();chats.CurrentCell=chats.Rows[e.RowIndex].Cells[1];chats.Rows[e.RowIndex].Selected=true;}};
        left.Controls.Add(chats,0,4);
        counts.Dock=DockStyle.Fill;counts.ForeColor=Theme.Muted;counts.TextAlign=ContentAlignment.MiddleLeft;left.Controls.Add(counts,0,5);
        var actions=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,BackColor=Color.White};actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,158));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,158));actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var pause=new ActionButton{Text="Ⅱ  暂停任务",Primary=true,Dock=DockStyle.Fill};pause.Click+=delegate{ProjectAction("Pause");};actions.Controls.Add(pause,0,0);var resume=new ActionButton{Text="▶  继续任务",Dock=DockStyle.Fill};resume.Click+=delegate{ProjectAction("Start");};actions.Controls.Add(resume,1,0);selectedTask.Dock=DockStyle.Fill;selectedTask.ForeColor=Theme.Muted;selectedTask.TextAlign=ContentAlignment.MiddleLeft;selectedTask.AutoEllipsis=true;actions.Controls.Add(selectedTask,2,0);left.Controls.Add(actions,0,6);targets.Controls.Add(left);content.Controls.Add(targets,0,0);
        var right=Rows(192,12,192,12,-1);right.BackColor=BackColor;five.Margin=week.Margin=new Padding(0);right.Controls.Add(five,0,0);right.Controls.Add(week,0,2);
        var policy=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(18,12,18,12)};var rules=Rows(35,39,39,28,-1);rules.Controls.Add(Heading("✿  自动策略",19),0,0);
        rules.Controls.Add(SwitchRow("额度恢复后自动续跑",autoResume),0,1);rules.Controls.Add(SwitchRow("自动使用已有重置卡",autoReset),0,2);
        cardInfo.Dock=DockStyle.Fill;cardInfo.ForeColor=Theme.Muted;rules.Controls.Add(cardInfo,0,3);policySummary.Dock=DockStyle.Fill;policySummary.ForeColor=Theme.Muted;policySummary.AutoEllipsis=true;rules.Controls.Add(policySummary,0,4);policy.Controls.Add(rules);right.Controls.Add(policy,0,4);content.Controls.Add(right,1,0);root.Controls.Add(content,0,1);
        var status=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(20,12,20,10)};var statusRows=Rows(31,49,25);statusRows.Controls.Add(Heading("▤  连接与运行状态",18),0,0);
        var metrics=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,BackColor=Color.White};for(int i=0;i<4;i++)metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
        var values=new[]{connectionState,monitorState,quotaFreshState,taskState};var captions=new[]{"客户端连接","额度保护","额度数据","任务与等待原因"};for(int i=0;i<4;i++){var metric=Rows(20,27);metric.Controls.Add(new Label{Text=captions[i],Dock=DockStyle.Fill,ForeColor=Theme.Muted},0,0);values[i].Dock=DockStyle.Fill;values[i].AutoEllipsis=true;values[i].ForeColor=Theme.Ink;values[i].Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold);metric.Controls.Add(values[i],0,1);metrics.Controls.Add(metric,i,0);}statusRows.Controls.Add(metrics,0,1);freshness.Dock=DockStyle.Fill;freshness.ForeColor=Theme.Muted;statusRows.Controls.Add(freshness,0,2);status.Controls.Add(statusRows);root.Controls.Add(status,0,2);Controls.Add(root);
        threshold.ValueChanged+=delegate{if(!loading){if(resumeThreshold.Value<=threshold.Value)resumeThreshold.Value=threshold.Value+1;SaveAndApply();}};
        resumeThreshold.ValueChanged+=delegate{if(!loading){if(resumeThreshold.Value<=threshold.Value)threshold.Value=resumeThreshold.Value-1;SaveAndApply();}};
        autoResume.CheckedChanged+=delegate{if(!loading)SaveAndApply();};autoReset.CheckedChanged+=delegate{if(!loading)SaveAndApply();};
        loginStart.CheckedChanged+=delegate{if(!loading){try{SetStartup(loginStart.Checked);}catch(Exception e){AppendLog("自启动设置失败："+e.Message);}}};
        chats.SelectionChanged+=delegate{UpdateStatusPanel();};
    }
    TableLayoutPanel SwitchRow(string text,CheckBox toggle){var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty,BackColor=Color.White};row.RowStyles.Add(new RowStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,60));row.Controls.Add(new Label{Text=text,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Theme.Ink},0,0);toggle.Text=text;toggle.AccessibleName=text;toggle.Anchor=AnchorStyles.Right;toggle.Margin=Padding.Empty;row.Controls.Add(toggle,1,0);return row;}
    string PolicySummary(){return "五小时："+config.pauseRemainingPercent.ToString("0.#")+"% 暂停 / "+config.resumeRemainingPercent.ToString("0.#")+"% 自动恢复\n周额度保留底线："+config.weeklyPauseRemainingPercent.ToString("0.#")+"%（设置中可修改）";}
    void SaveProtectionValues(decimal pause,decimal resume,decimal week){
        loading=true;threshold.Value=pause;resumeThreshold.Value=resume;weeklyFloor.Value=week;loading=false;SaveAndApply();
    }
    TableLayoutPanel GeneralSwitch(string title,string detail,CheckBox toggle) {
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=Padding.Empty,Padding=new Padding(6,4,6,4),BackColor=Color.White};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,64));
        row.RowStyles.Add(new RowStyle(SizeType.Percent,50));row.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        row.Controls.Add(new Label{Text=title,Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Theme.Ink},0,0);
        row.Controls.Add(new Label{Text=detail,Dock=DockStyle.Fill,Margin=Padding.Empty,ForeColor=Theme.Muted,TextAlign=ContentAlignment.TopLeft,Font=new Font(Font.FontFamily,9)},0,1);
        toggle.Text=title;toggle.AccessibleName=title;toggle.Anchor=AnchorStyles.Right;toggle.Margin=Padding.Empty;row.Controls.Add(toggle,1,0);row.SetRowSpan(toggle,2);return row;
    }
    Form CreateSettingsDialog(){
        var dialog=new Form{Text="设置",ClientSize=new Size(740,750),AutoScaleMode=AutoScaleMode.Dpi,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=Color.FromArgb(245,248,253)};
        var rows=Rows(40,28,114,96,32,60,60,48,28,124,46);rows.Padding=new Padding(24,12,24,12);rows.BackColor=dialog.BackColor;
        rows.Controls.Add(Heading("设置",23),0,0);rows.Controls.Add(Heading("额度保护",12),0,1);
        var pause=new NumericUpDown{Minimum=0,Maximum=99,DecimalPlaces=1,Value=threshold.Value,Width=68,Name="fiveHourPause",AccessibleName="五小时暂停线"};
        var resume=new NumericUpDown{Minimum=1,Maximum=100,DecimalPlaces=1,Value=resumeThreshold.Value,Width=68,Name="fiveHourResume",AccessibleName="五小时自动恢复线"};
        var weekInput=new NumericUpDown{Minimum=0,Maximum=99,DecimalPlaces=1,Value=weeklyFloor.Value,Width=68,Name="weeklyFloor",AccessibleName="周额度保留底线"};
        var fiveCard=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=new Padding(0,0,0,6)};var fiveRows=Rows(23,34,-1);fiveRows.Controls.Add(Heading("五小时额度",13),0,0);
        var inputs=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};inputs.Controls.Add(Inline("暂停线"));inputs.Controls.Add(pause);inputs.Controls.Add(Inline("%      自动恢复线"));inputs.Controls.Add(resume);inputs.Controls.Add(Inline("%"));fiveRows.Controls.Add(inputs,0,1);
        fiveRows.Controls.Add(new Label{Text="剩余 ≤ 暂停线时暂停；自动续跑须达到恢复线。手动继续只需高于暂停线。",Dock=DockStyle.Fill,ForeColor=Theme.Muted,Font=new Font(Font.FontFamily,9)},0,2);fiveCard.Controls.Add(fiveRows);rows.Controls.Add(fiveCard,0,2);
        var weekCard=new SurfacePanel{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=new Padding(0,0,0,6)};var weekRows=Rows(34,-1);var weekInputs=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,WrapContents=false};weekInputs.Controls.Add(Inline("周额度保留底线"));weekInputs.Controls.Add(weekInput);weekInputs.Controls.Add(Inline("%"));weekRows.Controls.Add(weekInputs,0,0);
        weekRows.Controls.Add(new Label{Text="剩余 ≤ 底线时暂停；五小时刷新、手动继续均不能绕过。周额度未知时等待。",Dock=DockStyle.Fill,ForeColor=Theme.Muted,Font=new Font(Font.FontFamily,9)},0,1);weekCard.Controls.Add(weekRows);rows.Controls.Add(weekCard,0,3);
        bool changing=false;Action save=delegate{SaveProtectionValues(pause.Value,resume.Value,weekInput.Value);};
        pause.ValueChanged+=delegate{if(changing)return;changing=true;if(resume.Value<=pause.Value)resume.Value=pause.Value+1;save();changing=false;};
        resume.ValueChanged+=delegate{if(changing)return;changing=true;if(resume.Value<=pause.Value)pause.Value=resume.Value-1;save();changing=false;};weekInput.ValueChanged+=delegate{if(!changing)save();};
        rows.Controls.Add(Heading("通用设置",12),0,4);var startup=new ToggleSwitch{Checked=File.Exists(StartupShortcut())};rows.Controls.Add(GeneralSwitch("随 Windows 启动","登录后自动启动并开始监控",startup),0,5);
        var background=new ToggleSwitch{Checked=config.trayBackgroundEnabled};rows.Controls.Add(GeneralSwitch("托盘后台运行","最小化或关闭窗口后驻留托盘；关闭此项后，关窗退出。",background),0,6);
        var resetOptions=new FlowLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty,Padding=new Padding(0,6,0,0),WrapContents=false};var resetInput=new NumericUpDown{Minimum=0,Maximum=99,DecimalPlaces=1,Value=(decimal)config.resetWeeklyRemainingPercent,Width=58};resetOptions.Controls.Add(Inline("重置卡触发：周剩余 ≤"));resetOptions.Controls.Add(resetInput);resetOptions.Controls.Add(Inline("%（仅在主界面开关开启时）"));rows.Controls.Add(resetOptions,0,7);
        rows.Controls.Add(Heading("版本更新",12),0,8);rows.Controls.Add(CreateUpdatePanel(dialog),0,9);
        rows.Controls.Add(SingleActionRow(MakeButton("查看完整日志",delegate{OpenLogs();})),0,10);
        startup.CheckedChanged+=delegate{loginStart.Checked=startup.Checked;};background.CheckedChanged+=delegate{config.trayBackgroundEnabled=background.Checked;WriteSettings();};resetInput.ValueChanged+=delegate{resetThreshold.Value=resetInput.Value;SaveAndApply();};dialog.Controls.Add(rows);return dialog;
    }
    void OpenSettings(){using(var dialog=CreateSettingsDialog()){dialog.ShowDialog(this);}}
    void UpdateStatusPanel(){
        bool running=WorkerRunning()&&!stopRequested;DateTime sampled;bool fresh=lastState!=null&&DateTime.TryParse(Str(Get(lastState,"lastCheckedAt")),out sampled)&&(DateTime.UtcNow-sampled.ToUniversalTime()).TotalSeconds<Math.Max(90,config.pollSeconds+30);
        connectionState.Text=running?(fresh?"● 已连接 Codex":"○ 等待客户端"):"○ 尚未检查";connectionState.ForeColor=running&&fresh?Theme.Green:Theme.Muted;
        monitorState.Text=running?(config.observeOnly?"只读观察":"● 保护中"):exiting?"正在退出":stopRequested?"保护未开启":"等待自动重连";monitorState.ForeColor=running?Theme.Green:Theme.Muted;
        protectionStatus.Text=running?(config.observeOnly?"● 只读观察":"● 额度保护中"):exiting?"正在退出":stopRequested?"● 保护未开启":"● 自动重连中";protectionStatus.ForeColor=running?Theme.Green:Theme.Muted;
        quotaFreshState.Text=fresh?"数据新鲜":"上次记录 · 待刷新";quotaFreshState.ForeColor=fresh?Theme.Green:Color.FromArgb(210,137,52);
        selectedTask.Text=chats.CurrentRow==null?"选择需要控制的任务":Str(chats.CurrentRow.Cells[2].Value);
        string reason=chats.CurrentRow==null?"":Str(chats.CurrentRow.Cells[3].Value);taskState.Text=reason.Length>0?reason:running?"等待选择任务":"点击开始监控";
        if(!running)freshness.Text=exiting?"正在退出管家；退出后不再执行额度保护。":stopRequested?"额度保护未运行，显示上次记录；刷新或继续任务可恢复后台。":"额度保护正在自动重连，显示上次记录。";
        if(DateTime.UtcNow<actionFeedbackUntil){selectedTask.Text=actionFeedback;taskState.Text=actionFeedback;}
    }
    string StartupShortcut() {return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"Codex 任务托管.lnk");}
    void SetStartup(bool enabled) {
        string shortcut=StartupShortcut();
        if(!enabled) {if(File.Exists(shortcut))File.Delete(shortcut);return;}
        Type type=Type.GetTypeFromProgID("WScript.Shell");object shell=Activator.CreateInstance(type);
        object link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{shortcut});
        Type lt=link.GetType();lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{Application.ExecutablePath});
        lt.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--tray"});
        lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{AppDomain.CurrentDomain.BaseDirectory});
        lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,new object[0]);
        Marshal.ReleaseComObject(link);Marshal.ReleaseComObject(shell);
    }
    void WriteSettings() {
        config.pollSeconds=(int)poll.Value; config.pauseRemainingPercent=(double)threshold.Value;
        config.weeklyPauseRemainingPercent=(double)weeklyFloor.Value;config.quotaBasis="layered";config.resumeRemainingPercent=(double)resumeThreshold.Value;config.autoResumeEnabled=autoResume.Checked;
        config.observeOnly=observe.Checked; config.resumeAll=resumeAll.Checked;
        config.autoResetEnabled=autoReset.Checked;config.resetWeeklyRemainingPercent=(double)resetThreshold.Value;
        config.nearLimitRemainingPercent=(double)nearThreshold.Value;config.nearLimitPollSeconds=(int)nearPoll.Value;
        config.maxConsecutiveFailures=stopOnFailures.Checked ? (int)failureLimit.Value : 0;
        config.showAllProjects=showAll.Checked;
        AtomicJson(settingsPath,config); settingsSeen=ReadShared(settingsPath);
    }
    void SaveAndApply() {
        try { WriteSettings(); Signal("Refresh"); AppendLog("设置已保存，将在下次查询时应用。"); }
        catch(Exception e) { AppendLog("设置保存失败："+e.Message); }
    }
    void SelectChats(bool selected) {
        loading=true;
        var ids=new HashSet<string>(config.resumeThreadIds);var excluded=new HashSet<string>(config.resumeExcludedThreadIds);
        foreach(DataGridViewRow row in chats.Rows){row.Cells[0].Value=selected;string id=Str(row.Tag);if(selected){ids.Add(id);excluded.Remove(id);}else{ids.Remove(id);excluded.Add(id);}}
        config.resumeThreadIds=ids.ToArray();config.resumeExcludedThreadIds=excluded.ToArray();loading=false;SaveAndApply();
    }
    string FindRuntime() {
        if(cachedRuntime!=null && File.Exists(cachedRuntime))return cachedRuntime;
        var candidates=new List<string>();
        if(!String.IsNullOrWhiteSpace(config.pwshPath))candidates.Add(Environment.ExpandEnvironmentVariables(config.pwshPath.Trim().Trim('"')));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".cache","codex-runtimes","codex-primary-runtime","dependencies","native","powershell","pwsh.exe"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","pwsh.exe"));
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) {
            if(!String.IsNullOrWhiteSpace(folder))try{candidates.Add(Path.Combine(folder.Trim().Trim('"'),"pwsh.exe"));}catch(ArgumentException){}
        }
        foreach(string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase)) {
            if(!File.Exists(candidate))continue;
            try {
                var info=System.Diagnostics.FileVersionInfo.GetVersionInfo(candidate);
                if(info.FileMajorPart>=7){cachedRuntime=candidate;return candidate;}
            }catch(System.ComponentModel.Win32Exception){}
        }
        throw new Exception("缺少 PowerShell 7 运行组件，请安装 PowerShell 7 后重新打开任务管家。");
    }
    void DisposeWorker() {
        if(worker==null)return;
        worker.OutputDataReceived-=WorkerOutput;worker.ErrorDataReceived-=WorkerOutput;
        worker.Dispose();worker=null;
    }
    static string Quote(string value) { if(value.Contains("\"")) throw new ArgumentException("路径含不支持的引号"); return "\""+value+"\""; }
    bool WorkerRunning() { return worker!=null && !worker.HasExited; }
    void ProcessWakeRequest() {
        if(wakeEvent.WaitOne(0))wakePending=true;
        if(!wakePending || exiting || stopRequested && WorkerRunning())return;
        wakePending=false;
        if(WorkerRunning())Signal("Refresh");else StartWorker(true);
    }
    void KeepProtectionRunning() {
        if(exiting || stopRequested || smokeTest || WorkerRunning() || DateTime.UtcNow<nextProtectionRestart)return;
        StartWorker();
    }
    void StartWorker(bool manual=false) {
        if(manual){restartFailures=0;nextProtectionRestart=DateTime.MinValue;}
        if(WorkerRunning() || exiting) return;
        if(!manual && lastState!=null && Str(Get(lastState,"phase"))=="needs_attention"){runtimeFailure="任务保护已停止，请在设置中查看原因后手动重试。";return;}
        nextProtectionRestart=DateTime.UtcNow.AddSeconds(Math.Min(300,30*Math.Pow(2,Math.Min(4,restartFailures++))));
        try {
            WriteSettings(); stopRequested=false;
            DisposeWorker();
            worker=new Process();
            worker.StartInfo=new ProcessStartInfo(FindRuntime(),"-NoLogo -NoProfile -File "+Quote(backendPath)+" -All -Mode "+(smokeTest ? "Monitor" : "Auto")+" -StateDirectory "+Quote(stateDir)+" -SettingsPath "+Quote(settingsPath));
            worker.StartInfo.UseShellExecute=false; worker.StartInfo.CreateNoWindow=true;
            worker.StartInfo.RedirectStandardOutput=true; worker.StartInfo.RedirectStandardError=true;
            worker.StartInfo.StandardOutputEncoding=Encoding.UTF8; worker.StartInfo.StandardErrorEncoding=Encoding.UTF8;
            worker.OutputDataReceived+=WorkerOutput; worker.ErrorDataReceived+=WorkerOutput;
            worker.Start(); worker.BeginOutputReadLine(); worker.BeginErrorReadLine();
            startedAt=DateTime.Now; runtimeFailure=null; mode.Text="正在查询额度…";
            AppendLog("后台监控已启动，不会打开终端窗口。");
        } catch(Exception e) { if(worker!=null)try{if(!worker.HasExited)worker.Kill();}catch(InvalidOperationException){} DisposeWorker(); runtimeFailure=e.Message; AppendLog("启动失败："+e.Message); mode.Text="额度保护连接失败，等待自动重连"; }
    }
    void WorkerOutput(object sender,DataReceivedEventArgs e) {
        if(String.IsNullOrWhiteSpace(e.Data) || IsDisposed)return;
        lock(outputLock){pendingOutput.Enqueue(e.Data.Length>4096 ? e.Data.Substring(0,4096) : e.Data);while(pendingOutput.Count>120)pendingOutput.Dequeue();}
    }
    void FlushOutput() {
        lock(outputLock){while(pendingOutput.Count>0)AddLogLine(pendingOutput.Dequeue());}
        if(!logDirty)return;logDirty=false;
        string text=String.Join(Environment.NewLine,logLines.ToArray());
        if(log.Text!=text){log.Text=text;log.SelectionStart=log.TextLength;log.ScrollToCaret();}
    }
    void AddLogLine(string message){logDirty=true;logLines.Enqueue(message.Length>4096 ? message.Substring(0,4096) : message);while(logLines.Count>60)logLines.Dequeue();}
    void AppendLog(string message) {if(!IsDisposed){AddLogLine(message);FlushOutput();}}
    void Signal(string action) {
        if(!WorkerRunning()) return;
        if(stopRequested && action!="Stop") return;
        AtomicJson(Path.Combine(stateDir,"control.json"),new {id=Guid.NewGuid().ToString(),action=action,time=DateTime.UtcNow.ToString("o")});
    }
    void ShowActionFeedback(string message) {
        actionFeedback=message;actionFeedbackUntil=DateTime.UtcNow.AddSeconds(15);
        selectedTask.Text=message;taskState.Text=message;AppendLog(message);
    }
    void ProjectAction(string action) {
        if(chats.CurrentRow==null){ShowActionFeedback("请先选择一个任务。");return;}
        if(observe.Checked){ShowActionFeedback("只读观察模式不操作任务，请先关闭只读观察。");return;}
        if(stopRequested && WorkerRunning()){ShowActionFeedback("后台正在停止，请等待退出后再继续任务。");return;}
        string id=Str(chats.CurrentRow.Tag),title=Str(chats.CurrentRow.Cells[1].Value);
        string actionKey=id+"|"+action;DateTime submitted;
        if(recentActions.TryGetValue(actionKey,out submitted) && DateTime.UtcNow-submitted<TimeSpan.FromSeconds(3)){ShowActionFeedback("操作已登记，请稍候。");return;}
        restartFailures=0;nextProtectionRestart=DateTime.MinValue;
        try {
            if(action=="AutoContinue" || action=="CancelAuto"){
                // Update selection without recursively submitting another project command.
                loading=true;
                try {
                    bool enabled=action=="AutoContinue";chats.CurrentRow.Cells[0].Value=enabled;
                    var ids=new HashSet<string>(config.resumeThreadIds);var excluded=new HashSet<string>(config.resumeExcludedThreadIds);
                    if(enabled){ids.Add(id);excluded.Remove(id);}else{ids.Remove(id);excluded.Add(id);}
                    config.resumeThreadIds=ids.ToArray();config.resumeExcludedThreadIds=excluded.ToArray();
                } finally {loading=false;}
                WriteSettings();
            }
            string directory=Path.Combine(stateDir,"commands");Directory.CreateDirectory(directory);
            AtomicJson(Path.Combine(directory,DateTime.UtcNow.Ticks.ToString("D20")+"-"+Guid.NewGuid().ToString()+".json"),new {id=Guid.NewGuid().ToString(),threadId=id,title=title,action=action,time=DateTime.UtcNow.ToString("o")});
            foreach(string key in recentActions.Keys.Where(x=>x.StartsWith(id+"|",StringComparison.Ordinal)).ToArray())recentActions.Remove(key);
            recentActions[actionKey]=DateTime.UtcNow;
            if(recentActions.Count>100)foreach(string key in recentActions.Where(x=>DateTime.UtcNow-x.Value>TimeSpan.FromMinutes(1)).Select(x=>x.Key).ToArray())recentActions.Remove(key);
            if(!WorkerRunning())StartWorker(true);else Signal("Refresh");
            if((action=="Start" || action=="AutoContinue") && Str(chats.CurrentRow.Cells[2].Value)=="未加载"){
                try {Process.Start(new ProcessStartInfo("codex://threads/"+id){UseShellExecute=true});AppendLog("正在 Codex 客户端打开未加载项目「"+title+"」，加载后执行已登记操作。");}
                catch(Exception e){AppendLog("操作已登记，请在 Codex 客户端打开项目「"+title+"」；自动打开失败："+e.Message);}
            }
            ShowActionFeedback(WorkerRunning() ? "操作已登记，正在核实额度与任务。" : "操作已保存，但后台未启动；请在设置中查看完整日志。");
        }catch(Exception e){ShowActionFeedback("操作登记失败："+e.Message);}
    }
    void StopWorker() {
        if(!WorkerRunning()) return;
        stopRequested=true; Signal("Stop"); mode.Text="正在关闭额度保护…";
        AppendLog("已请求停止监控；当前聊天照常执行，暂停记录保留。");
    }
    void RequestExit() {
        exiting=true; StopWorker();
        if(!WorkerRunning()) FinishExit(); else mode.Text="正在关闭后台监控…";
    }
    void FinishExit() { allowClose=true; Close(); }
    void RestoreWindow() { Show(); WindowState=FormWindowState.Normal; UpdateScreen(); Activate(); }
    void OpenLogs() {
        if(viewer==null || viewer.IsDisposed) { viewer=new LogWindow(logDir); viewer.Show(this); }
        else { viewer.Show(); viewer.Activate(); }
    }
    string Phase(string phase) {
        switch(phase) {
            case "paused": return "已暂停"; case "pause_pending": return "确认暂停中";
            case "resume_submitting": return "确认继续中"; case "resumed": return "已继续";
            case "needs_attention": return "需要排查";case "completed":return "本轮已结束";case "failed":return "本轮失败，需排查";
            case "cancelled": return "已解除登记"; case "stopped": return "已停止登记";
            case "observing": return "正常监控"; default: return "尚未登记";
            case "queued":return "已登记，等待继续";case "held":return "保持手动暂停";
        }
    }
    void RenderChats(bool force) {
        if(lastState==null) return;
        string revision=stateRevision+"|"+resumeAll.Checked+"|"+showAll.Checked+"|"+search.Text+"|"+String.Join(",",config.resumeThreadIds)+"|"+String.Join(",",config.resumeExcludedThreadIds);
        if(!force && revision==lastRendered)return;
        var data=Items(Get(lastState,"chats")).Select(Obj).Where(x=>showAll.Checked || search.Text.Length>0 || IsRelevantChat(x) || config.resumeThreadIds.Contains(Str(Get(x,"id")))).Where(x=>search.Text.Length==0 || (Str(Get(x,"title"))+" "+Str(Get(x,"workspace"))).IndexOf(search.Text,StringComparison.OrdinalIgnoreCase)>=0).OrderByDescending(x=>config.resumeThreadIds.Contains(Str(Get(x,"id")))).ToList();
        string selected=chats.CurrentRow==null ? "" : Str(chats.CurrentRow.Tag);
        int scroll=chats.FirstDisplayedScrollingRowIndex;
        loading=true; var ids=new HashSet<string>(config.resumeThreadIds);var excluded=new HashSet<string>(config.resumeExcludedThreadIds);
        var wantedIds=new HashSet<string>(data.Select(x=>Str(Get(x,"id"))));
        for(int row=chats.Rows.Count-1;row>=0;row--)if(!wantedIds.Contains(Str(chats.Rows[row].Tag)))chats.Rows.RemoveAt(row);
        var rows=chats.Rows.Cast<DataGridViewRow>().ToDictionary(x=>Str(x.Tag));
        int desiredIndex=0;
        foreach(var chat in data) {
            string id=Str(Get(chat,"id")), why=Str(Get(chat,"waitReason"));
            string runtime=Str(Get(chat,"runtime"));if(runtime=="执行中" && Str(Get(chat,"goalStatus"))=="active")runtime="目标执行";
            DataGridViewRow row;
            if(!rows.TryGetValue(id,out row)){int added=chats.Rows.Add();row=chats.Rows[added];row.Tag=id;}
            if(row.Index!=desiredIndex){chats.Rows.Remove(row);chats.Rows.Insert(desiredIndex,row);}
            desiredIndex++;int index=row.Index;
            object[] values={resumeAll.Checked ? !excluded.Contains(id) : ids.Contains(id),Str(Get(chat,"title")),runtime,!ids.Contains(id) && !resumeAll.Checked ? "未托管" : why.Length>0 ? why : Phase(Str(Get(chat,"monitorPhase")))};
            for(int cell=0;cell<values.Length;cell++)if(!Object.Equals(row.Cells[cell].Value,values[cell]))row.Cells[cell].Value=values[cell];
            chats.Rows[index].Tag=id; chats.Rows[index].Cells[1].ToolTipText=Str(Get(chat,"title"))+"\n"+Str(Get(chat,"workspace"));
            chats.Rows[index].Cells[3].ToolTipText=why;
            chats.Rows[index].Cells[2].Style.ForeColor=runtime.Contains("执行") ? Color.FromArgb(16,114,107) : Color.FromArgb(109,118,137);
            if(id==selected) chats.CurrentCell=chats.Rows[index].Cells[1];
        }
        chats.Columns[0].ReadOnly=false;listTitle.Text=(showAll.Checked ? "全部 " : "相关 ")+data.Count+" 个";
        if(scroll>=0 && scroll<chats.Rows.Count) chats.FirstDisplayedScrollingRowIndex=scroll;
        loading=false; lastRendered=revision;
    }
    static bool IsRelevantChat(Dictionary<string,object> chat){
        string runtime=Str(Get(chat,"runtime")),phase=Str(Get(chat,"monitorPhase"));
        return runtime=="执行中" || runtime=="目标待续" || runtime=="额度中断" || runtime=="等待输入或审批" || new[]{"observing","paused","queued","held","pause_pending","resume_submitting","needs_attention"}.Contains(phase) || Str(Get(chat,"waitReason")).Length>0;
    }
    void UpdateScreen() {
        RefreshUpdateResult();policySummary.Text=PolicySummary();
        try {
            var settingsInfo=new FileInfo(settingsPath);
            if(settingsInfo.Exists && (settingsInfo.LastWriteTimeUtc!=settingsStamp || settingsInfo.Length!=settingsLength || DateTime.UtcNow-lastSettingsRead>TimeSpan.FromSeconds(30))) {
                string currentSettings=ReadShared(settingsPath);
                if(settingsSeen!=currentSettings) {
                    var next=json.Deserialize<MonitorSettings>(currentSettings);
                    if(next!=null) {
                        loading=true; config=next;
                        if(config.resumeThreadIds==null)config.resumeThreadIds=new string[0];
                        if(config.resumeExcludedThreadIds==null)config.resumeExcludedThreadIds=new string[0];
                        resumeAll.Checked=config.resumeAll;observe.Checked=config.observeOnly;showAll.Checked=config.showAllProjects;
                        poll.Value=config.pollSeconds;threshold.Value=(decimal)config.pauseRemainingPercent;weeklyFloor.Value=(decimal)config.weeklyPauseRemainingPercent;
                        nearThreshold.Value=(decimal)config.nearLimitRemainingPercent;nearPoll.Value=config.nearLimitPollSeconds;resumeThreshold.Value=(decimal)config.resumeRemainingPercent;autoResume.Checked=config.autoResumeEnabled;
                        autoReset.Checked=config.autoResetEnabled;loading=false;settingsSeen=currentSettings;lastRendered="";cachedRuntime=null;
                    }
                }
                lastSettingsRead=DateTime.UtcNow;settingsStamp=settingsInfo.LastWriteTimeUtc;settingsLength=settingsInfo.Length;
            }

            string path=Path.Combine(stateDir,"state.json");
            var stateInfo=new FileInfo(path);
            if(stateInfo.Exists && (lastState==null || stateInfo.LastWriteTimeUtc!=stateStamp || stateInfo.Length!=stateLength || DateTime.UtcNow-lastStateRead>TimeSpan.FromSeconds(30))) {
                var fresh=Obj(json.DeserializeObject(ReadShared(path)));
                lastState=fresh;lastStateRead=DateTime.UtcNow;stateRevision++;stateStamp=stateInfo.LastWriteTimeUtc;stateLength=stateInfo.Length;
                if(Convert.ToInt32(Get(fresh,"readFailureCount"))==0)restartFailures=0;
            }
            if(lastState!=null) {
                var data=lastState;
                lastState=data; lastChecked=Str(Get(data,"lastCheckedAt"));
                var quota=Obj(Get(data,"lastQuota"));
                if(quota.Count>0) { five.PauseAt=config.pauseRemainingPercent;week.PauseAt=config.weeklyPauseRemainingPercent;five.UpdateQuota(Get(quota,"remaining"),Get(quota,"resetsAt")); week.UpdateQuota(Get(quota,"weeklyRemaining"),Get(quota,"weeklyResetsAt")); }
                var c=Obj(Get(data,"counts"));
                int managedActive=Convert.ToInt32(Get(c,"managedActive")),managedPaused=Convert.ToInt32(Get(c,"managedPaused")),managedPending=Convert.ToInt32(Get(c,"managedPending"));
                if(Get(c,"managedActive")==null){
                    var selected=Items(Get(data,"chats")).Select(Obj).Where(x=>config.resumeThreadIds.Contains(Str(Get(x,"id")))).ToList();
                    managedActive=selected.Count(x=>Str(Get(x,"runtime")).Contains("执行") || Str(Get(x,"goalStatus"))=="active");
                    managedPaused=selected.Count(x=>Str(Get(x,"goalStatus"))=="paused" || new[]{"paused","held"}.Contains(Str(Get(x,"monitorPhase"))));
                    managedPending=config.autoResumeEnabled?selected.Count(x=>new[]{"paused","queued"}.Contains(Str(Get(x,"monitorPhase")))):0;
                }

                counts.Text="已托管 "+config.resumeThreadIds.Length+"    ·    托管运行 "+managedActive+"    ·    托管暂停 "+managedPaused+"    ·    等待续跑 "+managedPending;
                var credits=Obj(Get(quota,"resetCredits"));var available=Get(credits,"availableCount");
                var expires=Items(Get(credits,"credits")).Select(Obj).Where(x=>Str(Get(x,"status"))=="available" && Get(x,"expiresAt")!=null).Select(x=>Convert.ToInt64(Get(x,"expiresAt"))).OrderBy(x=>x).ToList();
                cardInfo.Text="可用卡："+(available==null ? "未提供" : Str(available)+" 张")+(expires.Count>0 ? "   ·   最早到期 "+QuotaCard.FromUnix(expires[0]).ToString("MM-dd HH:mm") : "");
                resetStatus.Text="重置卡："+Str(Get(data,"resetStatus"))+"；每次只用一张，成功后复查额度。";
                RenderChats(false);
                DateTime checkedTime;
                if(DateTime.TryParse(lastChecked,out checkedTime)) {
                    double age=(DateTime.Now-checkedTime.ToLocalTime()).TotalSeconds;
                    int delay=Get(data,"nextPollSeconds")==null ? config.pollSeconds : Convert.ToInt32(Get(data,"nextPollSeconds"));
                    freshness.Text="上次查询 "+checkedTime.ToLocalTime().ToString("HH:mm:ss")+"   ·   "+(age>delay+20 ? "数据已过期，正在等待客户端" : "距下次查询约 "+Math.Max(0,delay-(int)age)+" 秒")+"   ·   客户端 "+Str(Get(data,"appVersion"));
                    freshness.ForeColor=age>delay+20 ? Color.FromArgb(185,61,41) : Color.FromArgb(77,94,112);
                }
                if(WorkerRunning() && !stopRequested) mode.Text=Str(Get(data,"phase"))=="needs_attention" ? "需要排查，请看下方日志" : (Convert.ToInt32(Get(data,"readFailureCount"))>0 ? "查询连续失败 "+Str(Get(data,"readFailureCount"))+" 次，正在重试" : (config.observeOnly ? "只读观察 · 不操作聊天" : "自动监控 · 阈值 "+config.pauseRemainingPercent.ToString("0.#")+"%"));
            }
        } catch(IOException) { } catch(Exception e) { mode.Text="状态读取失败："+e.Message; }
        five.Tick(); week.Tick();UpdateStatusPanel();
        if(!WorkerRunning()) {
            mode.Text=stopRequested ? "额度保护未开启；刷新或继续任务可恢复" : "额度保护等待自动重连";
            if(!stopRequested && lastState!=null && Str(Get(lastState,"phase"))=="needs_attention")runtimeFailure="任务保护已停止，请在设置中查看原因后重试。";
            if(!stopRequested && runtimeFailure!=null){mode.Text="额度保护未启动";freshness.Text=runtimeFailure;freshness.ForeColor=Color.FromArgb(185,61,41);}
            if(exiting) {FinishExit(); return;}
        }
        if(smokeTest && !checkFinished) SmokeCheck();
    }
    void SmokeCheck() {
        if(DateTime.Now-startedAt>TimeSpan.FromSeconds(75)) { SaveCheck(false,"等待实时数据超时"); RequestExit(); return; }
        if(lastState==null || chats.Rows.Count==0) return;
        DateTime checkedTime;
        if(!DateTime.TryParse(lastChecked,out checkedTime) || checkedTime.ToLocalTime()<startedAt) return;
        if(!checkConfigured) {
            loading=true; poll.Value=initialConfig.pollSeconds==3 ? 4 : 3; threshold.Value=initialConfig.pauseRemainingPercent==12 ? 13 : 12; observe.Checked=true; resumeAll.Checked=false;
            foreach(DataGridViewRow row in chats.Rows) row.Cells[0].Value=false;
            chats.Rows[0].Cells[0].Value=true; config.resumeThreadIds=new[]{Str(chats.Rows[0].Tag)};
            loading=false; SaveAndApply(); RenderChats(true); checkConfigured=true; return;
        }
        var effective=Obj(Get(lastState,"settings")); var quota=Obj(Get(lastState,"lastQuota"));
        if(Convert.ToInt32(Get(effective,"pollSeconds"))!=config.pollSeconds || Convert.ToDouble(Get(effective,"pauseRemainingPercent"))!=config.pauseRemainingPercent || !Convert.ToBoolean(Get(effective,"observeOnly"))) return;
        bool passed=config.resumeThreadIds.Length==1 && !config.resumeAll && quota.Count>0 && Get(quota,"weeklyResetsAt")!=null && WorkerRunning() && !log.Text.Contains("\uFFFD");
        if(viewer==null) { OpenLogs(); return; }
        if(!viewer.Ready) return;
        passed=passed && viewer.RowCount>0 && viewer.HasQuotaRows;
        Directory.CreateDirectory(checkDir);
        viewer.SaveScreenshot(Path.Combine(checkDir,"logs-window.png"));
        viewer.ExportCsv(Path.Combine(checkDir,"logs.csv"));
        if(logDir!=stateDir) {
            passed=passed && viewer.HasControlHistory;
            viewer.SetFilter("暂停");
            passed=passed && viewer.RowCount>0;
            viewer.SaveScreenshot(Path.Combine(checkDir,"logs-pauses-window.png"));
        }
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(Path.Combine(checkDir,"window.png")); }
        ClientSize=new Size(960,900); PerformLayout(); Application.DoEvents();
        using(var bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size)); bitmap.Save(Path.Combine(checkDir,"window-small.png")); }
        try {CheckChoicesAndPreview();}catch(Exception e){SaveCheck(false,"界面交互检查失败："+e.Message);RequestExit();return;}
        passed=passed && choicesPassed && quotaDisplayPassed;
        SaveCheck(passed,"实时额度、重置时间、失败次数设置、项目筛选及全部列表、右键菜单、单项目选择和只读设置"); RequestExit();
    }
    void CheckChoicesAndPreview(){
        var savedState=lastState;var savedConfig=json.Serialize(config);bool savedDefault=resumeAll.Checked;
        Func<string,string,string,string,Dictionary<string,object>> chat=delegate(string id,string title,string runtime,string phase){return new Dictionary<string,object>{{"id",id},{"title",title},{"runtime",runtime},{"monitorPhase",phase},{"goalStatus",id=="demo-goal" ? "active" : ""}};};
        try {
            lastState=new Dictionary<string,object>{{"chats",new object[]{chat("demo-goal","整理项目文档","执行中","observing"),chat("demo-ordinary","核对数据记录","执行中","observing"),chat("demo-paused","生成研究报告","额度中断","paused"),chat("demo-history","历史任务（不应显示）","空闲","completed"),chat("demo-unloaded","未加载历史（不应显示）","未加载","")}}};
            loading=true;resumeAll.Checked=true;config.resumeAll=true;config.resumeExcludedThreadIds=new string[0];loading=false;RenderChats(true);
            if(chats.Rows.Count!=3 || chats.Columns[0].ReadOnly)throw new Exception("相关聊天筛选或勾选被锁住");
            chats.Rows[0].Cells[0].Value=false;
            if(!config.resumeExcludedThreadIds.Contains("demo-goal"))throw new Exception("默认允许时无法取消单行");
            RenderChats(true);if(Convert.ToBoolean(chats.Rows[0].Cells[0].Value))throw new Exception("刷新丢失取消选择");
            chats.Rows[0].Cells[0].Value=true;
            if(config.resumeExcludedThreadIds.Contains("demo-goal"))throw new Exception("默认允许时无法重新勾选");
            resumeAll.Checked=false;
            if(!Convert.ToBoolean(chats.Rows[0].Cells[0].Value))throw new Exception("切换新聊天默认值覆盖已有选择");
            chats.Rows[0].Cells[0].Value=false;RenderChats(true);
            if(Convert.ToBoolean(chats.Rows[0].Cells[0].Value))throw new Exception("逐项模式无法取消");
            chats.Rows[0].Cells[0].Value=true;RenderChats(true);
            if(!Convert.ToBoolean(chats.Rows[0].Cells[0].Value))throw new Exception("逐项模式无法开启");
            var persisted=json.Deserialize<MonitorSettings>(ReadShared(settingsPath));
            if(!persisted.resumeThreadIds.Contains("demo-goal") || persisted.resumeExcludedThreadIds.Contains("demo-goal"))throw new Exception("逐项选择没有保存");
            choicesPassed=true;
            showAll.Checked=true;RenderChats(true);if(chats.Rows.Count!=5)throw new Exception("显示所有项目遗漏空闲或未加载项目");
            showAll.Checked=false;RenderChats(true);if(chats.Rows.Count!=3 || projectMenu.Items.Count!=4)throw new Exception("项目筛选或右键菜单错误");
            if(json.Deserialize<MonitorSettings>(ReadShared(settingsPath)).showAllProjects)throw new Exception("全部列表开关没有保存");
            failureLimit.Value=7;stopOnFailures.Checked=true;SaveAndApply();
            if(json.Deserialize<MonitorSettings>(ReadShared(settingsPath)).maxConsecutiveFailures!=7 || !failureLimit.Enabled)throw new Exception("失败次数设置没有保存");
            stopOnFailures.Checked=false;SaveAndApply();
            if(json.Deserialize<MonitorSettings>(ReadShared(settingsPath)).maxConsecutiveFailures!=0 || failureLimit.Enabled)throw new Exception("无法关闭连续失败自动停止");
            long resetAt=(long)(DateTime.UtcNow.AddHours(2)-new DateTime(1970,1,1)).TotalSeconds;
            five.UpdateQuota(65,resetAt);if(five.Value.Text!="剩余 65%")throw new Exception("额度 65% 未显示");
            five.UpdateQuota(7,resetAt);if(five.Value.Text!="剩余 7%" || five.Bar.Value!=7 || five.Detail.Text!="已用 93%")throw new Exception("额度变化未刷新");
            five.UpdateQuota(0,resetAt);if(five.Value.Text!="剩余 0%" || five.Bar.Value!=0)throw new Exception("额度归零未刷新");
            quotaDisplayPassed=true;
            five.UpdateQuota(65,resetAt);week.UpdateQuota(80,resetAt+6*86400);
            ClientSize=new Size(1120,970);PerformLayout();
            counts.Text="执行中 2（普通 1，目标 1）    ·    已暂停 1    ·    恢复后待续 1";mode.Text="自动监控 · 暂停阈值 5%";
            loading=true;observe.Checked=false;autoReset.Checked=false;poll.Value=180;threshold.Value=5;nearThreshold.Value=10;nearPoll.Value=10;resetThreshold.Value=1;failureLimit.Value=5;loading=false;
            cardInfo.Text="可用卡：2 张   ·   最早到期 01-31 12:00";resetStatus.Text="重置卡：自动使用已关闭；每次只用一张，成功后复查额度。";
            freshness.Text="上次查询 12:00:00   ·   常规 180 秒，低额度 10 秒   ·   示例界面";
            log.Text="[12:00:00] 五小时余 65% | 周余 80% | 执行中 2（普通 1，目标 1） | 已暂停 1\r\n[11:59:30] 聊天「生成研究报告」因额度不足中断，等待额度恢复。";
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(checkDir,"preview.png"));}
            ClientSize=new Size(960,900);PerformLayout();
            using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(Path.Combine(checkDir,"preview-small.png"));}
            projectMenu.Show(chats,new Point(260,30));projectMenu.Refresh();
            using(var bitmap=new Bitmap(projectMenu.Width,projectMenu.Height)){projectMenu.DrawToBitmap(bitmap,new Rectangle(Point.Empty,projectMenu.Size));bitmap.Save(Path.Combine(checkDir,"project-menu.png"));}projectMenu.Close();
        }finally {
            loading=true;lastState=savedState;config=json.Deserialize<MonitorSettings>(savedConfig);resumeAll.Checked=savedDefault;
            observe.Checked=config.observeOnly;autoReset.Checked=config.autoResetEnabled;poll.Value=config.pollSeconds;threshold.Value=(decimal)config.pauseRemainingPercent;nearThreshold.Value=(decimal)config.nearLimitRemainingPercent;nearPoll.Value=config.nearLimitPollSeconds;resumeThreshold.Value=(decimal)config.resumeRemainingPercent;autoResume.Checked=config.autoResumeEnabled;resetThreshold.Value=(decimal)config.resetWeeklyRemainingPercent;
            stopOnFailures.Checked=config.maxConsecutiveFailures>0;failureLimit.Value=config.maxConsecutiveFailures>0 ? config.maxConsecutiveFailures : 5;failureLimit.Enabled=stopOnFailures.Checked;
            showAll.Checked=config.showAllProjects;
            loading=false;WriteSettings();
        }
    }
    void SaveCheck(bool passed,string detail) {
        checkFinished=true; Directory.CreateDirectory(checkDir);
        AtomicJson(Path.Combine(checkDir,"ui-check-result.json"),new {passed=passed,detail=detail,checkedAt=DateTime.Now.ToString("o"),initialSettings=initialConfig,settings=config,chats=chats.Rows.Count,choicesPassed=choicesPassed,quotaDisplayPassed=quotaDisplayPassed,logRows=viewer==null ? 0 : viewer.RowCount,quota=lastState==null ? null : Get(lastState,"lastQuota"),stateDirectory=stateDir});
    }
}
