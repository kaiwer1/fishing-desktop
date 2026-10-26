using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Changes only its own visible window; no foreign activation or synthetic input.
sealed class NativeEnvironmentFixture : Form
{
    readonly string root;
    readonly Timer timer=new Timer{Interval=100};
    readonly Stopwatch clock=Stopwatch.StartNew();
    int phase=-1;
    readonly Rectangle normal=new Rectangle(80,80,720,460);
    NativeEnvironmentFixture(string root)
    {
        this.root=root;Text="Fishing host policy - owned fullscreen fixture";
        StartPosition=FormStartPosition.Manual;Bounds=normal;
        // Keep one HWND across phases. Changing FormBorderStyle can recreate
        // it and hand foreground to another application before the next sample.
        FormBorderStyle=FormBorderStyle.None;
        BackColor=Color.FromArgb(24,48,53);ForeColor=Color.White;
        Controls.Add(new Label{Text="P0 host pause test · closes automatically\nNormal → fullscreen → normal",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",20)});
        Shown+=delegate{clock.Restart();timer.Start();};
        timer.Tick+=delegate{
            if(File.Exists(Path.Combine(root,"exit.request")) || clock.ElapsedMilliseconds>=50000){Close();return;}
            int next=clock.ElapsedMilliseconds<2000?0:clock.ElapsedMilliseconds<40000?1:2;
            if(next==phase)return;phase=next;
            Bounds=phase==1?Screen.PrimaryScreen.Bounds:normal;
            // The launcher intentionally starts hidden. Toggle our own managed
            // visibility so its startup show hint cannot suppress the fixture.
            if(phase==0)Hide();Show();Activate();
            File.WriteAllText(Path.Combine(root,"fixture-phase.txt"),phase==0?"normal":phase==1?"fullscreen":"restored");
            File.AppendAllText(Path.Combine(root,"fixture-sizes.txt"),(phase==0?"normal":phase==1?"fullscreen":"restored")+" width="+Width+" height="+Height+Environment.NewLine);
        };
    }
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    [STAThread]static int Main(string[] args)
    {
        if(args.Length!=1 || !Directory.Exists(args[0]))return 2;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var fixture=new NativeEnvironmentFixture(args[0]))Application.Run(fixture);return 0;
    }
}
