using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace QuickPPPoE {
    static class Program {
        [STAThread] static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length==2 && args[0]=="--preview") {
                using(var form=new MainForm(true)) {form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)) {form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(args[1]);} form.Dispose();}
                return;
            }
            bool first;
            using(var mutex=new Mutex(true,"Local\\QuickPPPoE-"+WindowsIdentity.GetCurrent().User.Value,out first)) {
                if(!first) {MessageBox.Show("程序已在运行，请从系统托盘打开。","轻拨 PPPoE");return;}
                Application.Run(new MainForm(false));
            }
        }
    }
    public sealed class MainForm : Form {
        readonly TextBox username=new TextBox(), password=new TextBox(), service=new TextBox(), log=new TextBox();
        readonly CheckBox remember=new CheckBox(), reconnect=new CheckBox(), showPassword=new CheckBox();
        readonly NumericUpDown retry=new NumericUpDown();
        readonly Button connect=new Button(), disconnect=new Button(), save=new Button();
        readonly Label status=new Label(), detail=new Label();
        readonly NotifyIcon tray=new NotifyIcon();
        readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
        readonly Stopwatch clock=Stopwatch.StartNew();
        ConnectionController controller;
        bool exitRequested, preview, balloonShown;
        readonly Color ink=Color.FromArgb(29,42,64), muted=Color.FromArgb(100,116,139), blue=Color.FromArgb(37,99,235);
        public MainForm(bool isPreview) {
            preview=isPreview;
            Text="轻拨 PPPoE v"+Application.ProductVersion;ClientSize=new Size(580,700);MinimumSize=new Size(596,739);
            AutoScaleMode=AutoScaleMode.Dpi;Font=new Font("Microsoft YaHei UI",10);
            BackColor=Color.FromArgb(244,247,252);ForeColor=ink;StartPosition=FormStartPosition.CenterScreen;
            FormBorderStyle=FormBorderStyle.FixedSingle;MaximizeBox=false;Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            AddLabel("轻拨",28,22,220,42,24,true);
            AddLabel("v"+Application.ProductVersion,440,38,112,24,10,false).ForeColor=muted;
            AddLabel("PPPoE  /  宽带连接助手",30,69,510,25,10,false).ForeColor=muted;
            var banner=new Panel {Location=new Point(28,112),Size=new Size(524,78),BackColor=Color.FromArgb(231,239,255)};
            Controls.Add(banner);
            status.Text="●  尚未连接";status.Font=new Font(Font.FontFamily,14,FontStyle.Bold);status.ForeColor=blue;status.SetBounds(16,12,490,28);banner.Controls.Add(status);
            detail.Text="填好宽带信息，点击连接即可。";detail.ForeColor=muted;detail.SetBounds(17,45,490,25);banner.Controls.Add(detail);
            AddLabel("宽带账号",28,209,130,25,10,false);Input(username,28,238,524,256);
            AddLabel("宽带密码",28,281,130,25,10,false);Input(password,28,310,420,256);password.UseSystemPasswordChar=true;
            showPassword.Text="显示";showPassword.SetBounds(462,311,90,30);Controls.Add(showPassword);showPassword.CheckedChanged+=delegate {password.UseSystemPasswordChar=!showPassword.Checked;};
            AddLabel("服务名",28,353,180,25,10,false);
            AddLabel("可选 · 运营商未指定则留空",219,353,333,25,9,false).ForeColor=muted;
            Input(service,28,382,524,128);
            remember.Text="在本机加密记住密码";remember.SetBounds(28,426,240,28);Controls.Add(remember);
            reconnect.Text="掉线自动重连";reconnect.Checked=true;reconnect.SetBounds(28,461,205,28);Controls.Add(reconnect);
            AddLabel("失败后重试间隔",263,463,145,27,9,false);
            retry.SetBounds(408,460,75,28);retry.Minimum=1;retry.Maximum=60;retry.Value=1;Controls.Add(retry);
            AddLabel("秒",495,463,50,25,9,false);
            ButtonStyle(connect,"连接",28,510,202,true);ButtonStyle(disconnect,"断开 / 停止",244,510,158,false);ButtonStyle(save,"保存",416,510,136,false);
            disconnect.Enabled=false;
            AddLabel("运行日志",28,569,220,25,9,true);
            log.SetBounds(28,600,524,63);log.Multiline=true;log.ReadOnly=true;log.ScrollBars=ScrollBars.Vertical;log.BorderStyle=BorderStyle.None;log.BackColor=BackColor;log.ForeColor=muted;log.Font=new Font("Microsoft YaHei UI",9);Controls.Add(log);
            AddLabel("关闭窗口后留在托盘；退出请使用托盘菜单。",28,673,524,22,8,false).ForeColor=muted;
            connect.Click+=delegate {StartConnection();};disconnect.Click+=delegate {if(controller!=null) controller.Stop(clock.ElapsedMilliseconds);RefreshState();};
            save.Click+=delegate {try {SaveSettings();WriteLog("配置已保存。");} catch(Exception ex) {ShowError(ex);}};
            remember.CheckedChanged+=delegate {
                if(!remember.Checked && !preview) {
                    try {if(File.Exists(Settings.FilePath)) {var old=Settings.Load();old.Remember=false;old.Secret="";old.Save();}}
                    catch(Exception ex) {ShowError(ex);}
                }
            };
            var menu=new ContextMenuStrip();menu.Items.Add("打开轻拨",null,delegate {Restore();});
            menu.Items.Add("断开并停止重连",null,delegate {if(controller!=null) controller.Stop(clock.ElapsedMilliseconds);});
            menu.Items.Add("退出并断开",null,delegate {ExitApp();});
            tray.Icon=Icon;tray.Text="轻拨 PPPoE · 尚未连接";tray.ContextMenuStrip=menu;tray.Visible=!preview;tray.DoubleClick+=delegate {Restore();};
            FormClosing+=OnClosing;
            Resize+=delegate {if(WindowState==FormWindowState.Minimized && !preview) Hide();};
            timer.Interval=250;timer.Tick+=delegate {
                try {
                    if(controller!=null) controller.Tick(clock.ElapsedMilliseconds);
                    RefreshState();
                    if(exitRequested && (controller==null || controller.Phase==Phase.Idle)) {timer.Stop();tray.Visible=false;Close();}
                } catch(Exception ex) {WriteLog("运行异常："+ex.Message);if(controller!=null) controller.Stop(clock.ElapsedMilliseconds);}
            };
            if(!preview) LoadSettings(); else {username.Text="your-account@isp";WriteLog("就绪 · 250 ms 连接状态监测 / 本机密码加密");}
            timer.Start();
        }
        Label AddLabel(string text,int x,int y,int w,int h,float size,bool bold) {
            var label=new Label {Text=text,Font=new Font(Font.FontFamily,size,bold?FontStyle.Bold:FontStyle.Regular)};
            label.SetBounds(x,y,w,h);Controls.Add(label);return label;
        }
        void Input(TextBox input,int x,int y,int width,int max) {input.SetBounds(x,y,width,32);input.MaxLength=max;input.Font=new Font(Font.FontFamily,11);Controls.Add(input);}
        void ButtonStyle(Button b,string text,int x,int y,int width,bool primary) {
            b.Text=text;b.SetBounds(x,y,width,42);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderSize=primary?0:1;b.FlatAppearance.BorderColor=Color.FromArgb(205,215,230);b.BackColor=primary?blue:Color.White;b.ForeColor=primary?Color.White:ink;b.Cursor=Cursors.Hand;Controls.Add(b);
        }
        void LoadSettings() {
            try {
                var s=Settings.Load();username.Text=s.User??"";service.Text=s.Service??"";remember.Checked=s.Remember;reconnect.Checked=s.AutoReconnect;retry.Value=Math.Max(1,Math.Min(60,s.RetrySeconds));
                try {password.Text=s.Password();} catch {WriteLog("保存的密码无法解密，请重新输入并保存。");}
            } catch {WriteLog("配置读取失败，请重新输入信息并保存。");}
        }
        void SaveSettings() {
            var s=new Settings {User=username.Text.Trim(),Service=service.Text,Remember=remember.Checked,AutoReconnect=reconnect.Checked,RetrySeconds=(int)retry.Value};
            try {s.SetPassword(password.Text);} catch(System.Security.Cryptography.CryptographicException) {throw new InvalidOperationException("Windows 当前用户的密码加密不可用。请取消“在本机加密记住密码”后连接；密码不会明文保存。");}
            s.Save();
        }
        void StartConnection() {
            if(preview) return;
            try {
                if(string.IsNullOrWhiteSpace(username.Text)) {username.Focus();throw new InvalidOperationException("请填写宽带账号。");}
                if(password.Text.Length==0) {password.Focus();throw new InvalidOperationException("请填写宽带密码。");}
                if(username.Text.Trim().Length>256 || password.Text.Length>256) throw new InvalidOperationException("宽带账号与密码各最多 256 个字符。");
                SaveSettings();NativeRas.SaveEntry(Settings.Phonebook,service.Text);
                controller=new ConnectionController(new RasLink(Settings.Phonebook,username.Text.Trim(),password.Text));
                controller.AutoReconnect=reconnect.Checked;controller.RetryMilliseconds=(int)retry.Value*1000;controller.Log+=WriteLog;
                controller.Start(clock.ElapsedMilliseconds);RefreshState();
            } catch(Exception ex) {ShowError(ex);}
        }
        void RefreshState() {
            var phase=controller==null ? Phase.Idle : controller.Phase;
            bool idle=phase==Phase.Idle;
            username.Enabled=password.Enabled=service.Enabled=save.Enabled=idle && !exitRequested;
            connect.Enabled=idle && !exitRequested;disconnect.Enabled=!idle && !exitRequested;
            if(controller!=null) {controller.AutoReconnect=reconnect.Checked;controller.RetryMilliseconds=(int)retry.Value*1000;}
            string label="尚未连接",sub="填好宽带信息，点击连接即可。";
            if(phase==Phase.Connected) {label="已连接";sub="在线 "+TimeSpan.FromMilliseconds(clock.ElapsedMilliseconds-controller.ConnectedAt).ToString(@"dd\.hh\:mm\:ss")+"  ·  "+(reconnect.Checked?"掉线自动重连":"自动重连已关闭");}
            if(phase==Phase.Dialing) {label="正在拨号…";sub="正在发现服务并认证，请稍候。";}
            if(phase==Phase.Waiting) {label="等待重拨…";sub="自动重连运行中，可随时点击停止。";}
            if(phase==Phase.Releasing) {label="正在释放连接…";sub="等待 Windows 完成断开，避免端口冲突。";}
            if(exitRequested) sub="正在断开连接，完成后退出。";
            status.Text="●  "+label;status.ForeColor=phase==Phase.Connected?Color.FromArgb(15,128,92):blue;detail.Text=sub;tray.Text="轻拨 PPPoE · "+label;
        }
        void WriteLog(string message) {
            if(log.TextLength>16000) log.Text=log.Text.Substring(log.TextLength-8000);
            log.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+message+Environment.NewLine);
        }
        void ShowError(Exception ex) {WriteLog(ex.Message);MessageBox.Show(this,ex.Message,"轻拨 PPPoE",MessageBoxButtons.OK,MessageBoxIcon.Information);}
        void Restore() {Show();WindowState=FormWindowState.Normal;Activate();}
        void ExitApp() {exitRequested=true;Restore();if(controller!=null) controller.Stop(clock.ElapsedMilliseconds);RefreshState();}
        void OnClosing(object sender,FormClosingEventArgs e) {
            if(preview) return;
            if(e.CloseReason==CloseReason.WindowsShutDown || e.CloseReason==CloseReason.TaskManagerClosing) {if(controller!=null) controller.Stop(clock.ElapsedMilliseconds);tray.Visible=false;return;}
            if(exitRequested && (controller==null || controller.Phase==Phase.Idle)) return;
            e.Cancel=true;
            if(exitRequested) return;
            Hide();
            if(!balloonShown) {balloonShown=true;tray.ShowBalloonTip(2500,"轻拨仍在运行","自动重连会继续运行。右键托盘图标可退出。",ToolTipIcon.Info);}
        }
        protected override void Dispose(bool disposing) {if(disposing) {timer.Dispose();tray.Dispose();}base.Dispose(disposing);}
    }
}
