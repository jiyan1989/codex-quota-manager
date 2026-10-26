using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class Theme {
    public static readonly Color Ink=Color.FromArgb(24,43,75),Muted=Color.FromArgb(112,130,154),Blue=Color.FromArgb(39,128,250),Green=Color.FromArgb(38,182,135),Border=Color.FromArgb(225,233,243);
    public static GraphicsPath Round(RectangleF r,float radius) {
        float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));var p=new GraphicsPath();
        p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;
    }
}

class SurfacePanel : Panel {
    public SurfacePanel(){DoubleBuffered=true;BackColor=Color.FromArgb(245,248,253);Padding=new Padding(18);Margin=new Padding(0);}
    protected override void OnPaintBackground(PaintEventArgs e){
        e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var shape=Theme.Round(new RectangleF(1,1,Width-3,Height-3),17)){
            using(var brush=new LinearGradientBrush(ClientRectangle,Color.White,Color.FromArgb(248,251,255),90))e.Graphics.FillPath(brush,shape);
            using(var pen=new Pen(Theme.Border))e.Graphics.DrawPath(pen,shape);
        }
    }
}

class ActionButton : Button {
    public bool Primary;
    public ActionButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Size=new Size(128,40);Margin=new Padding(0,0,10,0);UseVisualStyleBackColor=false;}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
        using(var shape=Theme.Round(new RectangleF(1,1,Width-3,Height-3),10)){
            using(var fill=new SolidBrush(Primary?Theme.Blue:Color.FromArgb(242,246,252)))e.Graphics.FillPath(fill,shape);
            if(!Primary)using(var pen=new Pen(Theme.Border))e.Graphics.DrawPath(pen,shape);
        }
        TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,!Enabled?Theme.Muted:Primary?Color.White:Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(5,5,Width-10,Height-10));
    }
}

class ToggleSwitch : CheckBox {
    public ToggleSwitch(){AutoSize=false;Size=new Size(54,28);Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(Parent==null?Color.White:Parent.BackColor);
        using(var track=Theme.Round(new RectangleF(1,2,50,24),12))using(var fill=new SolidBrush(Checked?Theme.Blue:Color.FromArgb(203,215,231)))e.Graphics.FillPath(fill,track);
        using(var knob=new SolidBrush(Color.White))e.Graphics.FillEllipse(knob,Checked?29:4,5,18,18);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(0,0,53,27));
    }
}

class SearchBox : TextBox {
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr value,string text);
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);SendMessage(Handle,0x1501,IntPtr.Zero,"搜索任务名称或本地工作目录…");}
}

class QuotaRing : Control {
    public double? Remaining;
    public Color Accent=Theme.Blue;
    public QuotaRing(){DoubleBuffered=true;Size=new Size(128,128);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.Clear(BackColor);
        int side=Math.Min(Width,Height)-20;var r=new Rectangle((Width-side)/2,(Height-side)/2,side,side);
        using(var pen=new Pen(Color.FromArgb(230,237,247),10))e.Graphics.DrawArc(pen,r,0,360);
        if(Remaining.HasValue)using(var pen=new Pen(Accent,10)){pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;e.Graphics.DrawArc(pen,r,-90,(float)Math.Max(.1,Math.Min(100,Remaining.Value)*3.6));}
        using(var font=new Font("Microsoft YaHei UI",22,FontStyle.Bold,GraphicsUnit.Pixel))TextRenderer.DrawText(e.Graphics,Remaining.HasValue?Remaining.Value.ToString("0.#")+"%":"—",font,ClientRectangle,Theme.Ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
    }
}

class QuotaCard : SurfacePanel {
    public Label Value=new Label(),Detail=new Label(),Reset=new Label(),Countdown=new Label();
    public QuotaMeter Bar=new QuotaMeter();
    public long? ResetTimestamp;
    public double PauseAt=10;
    readonly QuotaRing ring=new QuotaRing();
    readonly Color accent;
    public QuotaCard(string title){
        Dock=DockStyle.Fill;Padding=new Padding(16,12,16,12);accent=title.Contains("周")?Theme.Green:Theme.Blue;ring.Accent=accent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=2,Margin=new Padding(0),BackColor=Color.White};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,126));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        var heading=new Label{Text="◷  "+title,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold),ForeColor=Theme.Ink};layout.Controls.Add(heading,0,0);layout.SetColumnSpan(heading,2);
        ring.Dock=DockStyle.Fill;ring.BackColor=Color.White;layout.Controls.Add(ring,0,1);
        var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Margin=new Padding(8,6,0,0),BackColor=Color.White};
        foreach(int h in new[]{34,23,23,24,12})body.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        Value.Text="正在查询…";Value.Font=new Font("Microsoft YaHei UI",18,FontStyle.Bold,GraphicsUnit.Pixel);Value.ForeColor=accent;
        Detail.Text="等待实时额度";Detail.ForeColor=Theme.Muted;Reset.ForeColor=Theme.Muted;Countdown.ForeColor=Theme.Blue;Countdown.Font=new Font("Microsoft YaHei UI",10,FontStyle.Bold);
        int row=0;foreach(Control control in new Control[]{Value,Detail,Reset,Countdown,Bar}){control.Dock=DockStyle.Fill;control.Margin=new Padding(0);body.Controls.Add(control,0,row++);}layout.Controls.Add(body,1,1);Controls.Add(layout);
    }
    public void UpdateQuota(object remaining,object reset){
        if(remaining==null){Value.Text="额度未知";Detail.Text="客户端未提供";ring.Remaining=null;Bar.Value=0;}
        else{double left=Math.Max(0,Math.Min(100,Convert.ToDouble(remaining)));Value.Text="剩余 "+left.ToString("0.#")+"%";Detail.Text="已用 "+(100-left).ToString("0.#")+"%";Value.ForeColor=left<=PauseAt?Color.FromArgb(221,113,39):accent;ring.Remaining=left;ring.Accent=Value.ForeColor;Bar.Value=(int)Math.Round(left);Bar.Accent=Value.ForeColor;}
        ring.Invalidate();Bar.Invalidate();ResetTimestamp=reset==null?(long?)null:Convert.ToInt64(reset);Reset.Text=ResetTimestamp.HasValue?"重置于 "+FromUnix(ResetTimestamp.Value).ToString("MM-dd HH:mm:ss"):"重置时间未提供";Tick();
    }
    public static DateTime FromUnix(long seconds){return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime();}
    public void Tick(){if(!ResetTimestamp.HasValue){Countdown.Text="等待查询确认";return;}var wait=FromUnix(ResetTimestamp.Value)-DateTime.Now;Countdown.Text=wait.TotalSeconds<=0?"时间已到，等待额度确认":"距重置 "+(wait.Days>0?wait.Days+"天 ":"")+wait.ToString(@"hh\:mm\:ss");}
}
