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
    readonly bool operatorStart;
    bool started,completed,waitExpired;
    internal int Result;
    readonly Label instructions=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,Font=new Font("Segoe UI",20)};
    NativeEnvironmentFixture(string root,bool operatorStart)
    {
        this.root=root;this.operatorStart=operatorStart;started=!operatorStart;Text="Fishing host policy - owned fullscreen fixture";
        StartPosition=FormStartPosition.Manual;Bounds=normal;
        // Keep one HWND across phases. Changing FormBorderStyle can recreate
        // it and hand foreground to another application before the next sample.
        FormBorderStyle=FormBorderStyle.None;
        BackColor=Color.FromArgb(24,48,53);ForeColor=Color.White;
        instructions.Text=operatorStart?"全屏暂停验证\n点击开始后约50秒自动退出\n期间请保持本窗口在前台":"P0 host pause test · closes automatically\nNormal → fullscreen → normal";
        Controls.Add(instructions);
        if(operatorStart){
            var start=new Button{Text="开始全屏验证（约50秒）",Dock=DockStyle.Bottom,Height=70};
            Controls.Add(start);
            start.Click+=delegate{
                try{
                if(started)return;started=true;clock.Restart();start.Visible=false;
                instructions.Text="正在验证全屏暂停\n约50秒后自动恢复并退出";
                WriteOperatorState();
                }catch(Exception){Result=4;Close();}
            };
        }
        Shown+=delegate{try{clock.Restart();timer.Start();if(operatorStart){Hide();Show();File.WriteAllText(Path.Combine(root,"fixture-phase.txt"),"awaiting-operator");WriteOperatorState();}}catch(Exception){Result=4;Close();}};
        FormClosing+=delegate{if(operatorStart){if(!completed && Result==0)Result=6;try{WriteOperatorState();}catch(Exception){Result=4;}}};
        timer.Tick+=delegate{
            try{
            if(File.Exists(Path.Combine(root,"exit.request"))){Close();return;}
            if(!started){if(clock.ElapsedMilliseconds>=180000){waitExpired=true;Result=6;Close();}return;}
            if(clock.ElapsedMilliseconds>=50000){
                completed=true;
                Close();return;
            }
            int next=clock.ElapsedMilliseconds<2000?0:clock.ElapsedMilliseconds<40000?1:2;
            if(next==phase)return;phase=next;
            Bounds=phase==1?Screen.PrimaryScreen.Bounds:normal;
            // The launcher intentionally starts hidden. Toggle our own managed
            // visibility so its startup show hint cannot suppress the fixture.
            if(phase==0)Hide();Show();Activate();
            File.WriteAllText(Path.Combine(root,"fixture-phase.txt"),phase==0?"normal":phase==1?"fullscreen":"restored");
            File.AppendAllText(Path.Combine(root,"fixture-sizes.txt"),(phase==0?"normal":phase==1?"fullscreen":"restored")+" width="+Width+" height="+Height+Environment.NewLine);
            }catch(Exception){Result=4;Close();}
        };
    }
    void WriteOperatorState(){File.WriteAllText(Path.Combine(root,"fixture-operator.json"),"{\"operatorStarted\":"+(started?"true":"false")+",\"normalExit\":"+(completed && Result==0?"true":"false")+",\"waitExpired\":"+(waitExpired?"true":"false")+"}");}
    protected override bool ShowWithoutActivation{get{return operatorStart && !started;}}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    [STAThread]static int Main(string[] args)
    {
        bool operatorStart=args.Length==2 && args[1]=="--operator-start";
        if((args.Length!=1 && !operatorStart) || !Directory.Exists(args[0]))return 2;
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        using(var fixture=new NativeEnvironmentFixture(args[0],operatorStart)){Application.Run(fixture);return fixture.Result;}
    }
}
