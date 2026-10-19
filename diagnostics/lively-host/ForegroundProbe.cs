// A visible test fixture, not a desktop automation helper.
// Changes only its own form. No hooks, synthetic input, global hotkeys or user file access.
using System;
using System.Drawing;
using System.Net;
using System.Text;
using System.Windows.Forms;

sealed class ForegroundProbe : Form
{
    readonly Label state = new Label();
    readonly string run = Guid.NewGuid().ToString();
    Rectangle normalBounds;
    bool fullscreen;

    ForegroundProbe()
    {
        Text = "Fishing Desktop Foreground Probe";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(960, 600);
        BackColor = Color.FromArgb(19, 46, 50);
        ForeColor = Color.FromArgb(236, 244, 225);
        Font = new Font("Microsoft YaHei UI", 16);
        KeyPreview = true;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(36), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        var title = new Label { Text = "桌面壁纸 · 前台遮挡测试", AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
        var note = new Label { Text = "只改变本窗口；不会操作其他应用或游戏。", AutoSize = true };
        var enter = new Button { Text = "进入全屏测试", AutoSize = true, Padding = new Padding(12), ForeColor = Color.Black };
        var leave = new Button { Text = "恢复普通窗口", AutoSize = true, Padding = new Padding(12), ForeColor = Color.Black };
        var close = new Button { Text = "关闭测试程序", AutoSize = true, Padding = new Padding(12), ForeColor = Color.Black };
        state.AutoSize = true;
        enter.Click += delegate { SetFullscreen(true); };
        leave.Click += delegate { SetFullscreen(false); };
        close.Click += delegate { Close(); };
        KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) SetFullscreen(false); };
        panel.Controls.AddRange(new Control[] { title, note, enter, leave, close, state });
        Controls.Add(panel);
        Shown += delegate { Report(); };
    }

    void SetFullscreen(bool value)
    {
        if (value == fullscreen) return;
        fullscreen = value;
        if (value)
        {
            normalBounds = Bounds;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = normalBounds;
        }
        Report();
    }

    async void Report()
    {
        state.Text = (fullscreen ? "全屏测试中" : "普通窗口") + "  " + Width + " × " + Height;
        var screen = Screen.FromControl(this).Bounds;
        var json = "{\"kind\":\"property\",\"fixture\":\"native-foreground\",\"run\":\"" + run
            + "\",\"name\":\"fullscreen\",\"value\":" + (fullscreen ? "true" : "false")
            + ",\"width\":" + Width + ",\"height\":" + Height
            + ",\"screenWidth\":" + screen.Width + ",\"screenHeight\":" + screen.Height + "}";
        try
        {
            using (var client = new WebClient())
            {
                client.Proxy = null;
                client.Encoding = Encoding.UTF8;
                client.Headers[HttpRequestHeader.ContentType] = "text/plain; charset=utf-8";
                await client.UploadStringTaskAsync("http://127.0.0.1:43817/event", "POST", json);
            }
        }
        catch (WebException) { state.Text += "（收集器不可用）"; }
    }

    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ForegroundProbe());
    }
}
