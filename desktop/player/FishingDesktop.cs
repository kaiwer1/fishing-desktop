using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FishingDesktop.Player
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Launcher());
        }
    }

    internal sealed class Launcher : Form
    {
        private readonly FlowLayoutPanel choices;

        internal Launcher()
        {
            Text = "钓鱼桌面";
            Font = new Font("Microsoft YaHei UI", 10f);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(246, 245, 238);
            ForeColor = Color.FromArgb(40, 51, 43);
            ClientSize = new Size(464, 255);

            var layout = new TableLayoutPanel {
                Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 16),
                ColumnCount = 1, RowCount = 4
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(layout);
            var heading = new Label {
                Text = "钓鱼桌面", AutoSize = true, Font = new Font(Font.FontFamily, 22f),
                Margin = new Padding(0, 0, 0, 8)
            };
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(new Label {
                Dock = DockStyle.Fill, AutoSize = true, Text =
                "选择「窗口试玩」后点「开启交互」，按 Esc 去营地买抄网。\n\n" +
                "选择「设为桌面」可在图标后方观景。想钓鱼时，双击托盘图标打开岸边窗口。"
            }, 0, 1);
            choices = new FlowLayoutPanel {
                AutoSize = true, Dock = DockStyle.Fill, WrapContents = false,
                Margin = new Padding(0, 0, 0, 9)
            };
            layout.Controls.Add(choices, 0, 2);
            var window = AddChoice("窗口试玩", "--player-window", Color.FromArgb(44, 82, 65), Color.White);
            AddChoice("设为桌面", "--player-wallpaper", Color.FromArgb(220, 226, 211), ForeColor);
            AcceptButton = window;
            layout.Controls.Add(new Label {
                AutoSize = true, Text = "两种模式共用存档。首次操作请看随包的试玩指南。", Margin = Padding.Empty
            }, 0, 3);
        }

        private Button AddChoice(string title, string argument, Color background, Color text)
        {
            var button = new Button {
                Text = title, Size = new Size(151, 43), BackColor = background,
                ForeColor = text, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 0, 10, 0)
            };
            button.FlatAppearance.BorderSize = 0;
            button.Click += delegate { Launch(argument); };
            choices.Controls.Add(button);
            return button;
        }

        private void Launch(string argument)
        {
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app");
            var host = Path.Combine(directory, "DualWindowHost.exe");
            if (!File.Exists(host) || !File.Exists(Path.Combine(directory, "page", "game.html")))
            {
                MessageBox.Show(this, "找不到游戏文件。请完整解压试玩 ZIP，让 app 文件夹与 FishingDesktop.exe 放在一起，再打开启动器。", Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try
            {
                choices.Enabled = false;
                using (Process.Start(new ProcessStartInfo(host, argument) {
                    WorkingDirectory = directory, UseShellExecute = false
                })) { }
                Close();
            }
            catch (Exception error)
            {
                choices.Enabled = true;
                MessageBox.Show(this, "游戏未能启动。\n\n" + error.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
