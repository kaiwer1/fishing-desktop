using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Threading;
using FishingDesktop.Diagnostics.HostPolicy;

namespace FishingDesktop.Diagnostics.OutputEnvironment
{
    [Flags]
    public enum OutputEnvironmentGlobalBlock { None=0,EnvironmentUnknown=1,SessionUnavailable=2 }
    public enum OutputEnvironmentReason
    {
        None,OwnOrShellForeground,WrongThread,InvalidTopology,AwarenessUnconfirmed,
        SessionUnavailable,InputDesktopUnavailable,ForegroundUnavailable,ShellUnavailable,
        GeometryUnavailable,InvalidGeometry,ForegroundChanged,ShellChanged,GeometryChanged,
        SessionChanged,DesktopChanged,QueryFailed
    }
    // Caller-declared geometry from one already verified topology. Invalid
    // declarations are retained here so Capture can reject the entire batch.
    public sealed class OutputEnvironmentTarget
    {
        public readonly string Key;
        public readonly int X,Y,Width,Height;
        public OutputEnvironmentTarget(string key,int x,int y,int width,int height)
        {Key=key;X=x;Y=y;Width=width;Height=height;}
    }
    public sealed class OutputEnvironmentRow
    {
        public readonly string Key;
        public readonly bool Fullscreen;
        public OutputEnvironmentRow(string key,bool fullscreen)
        {Key=EnvironmentAliases.Canonical(key);Fullscreen=fullscreen;}
    }
    public sealed class OutputEnvironmentSample
    {
        public readonly long TopologyGeneration;
        public readonly bool Known;
        public readonly OutputEnvironmentReason Reason;
        public readonly OutputEnvironmentGlobalBlock GlobalBlocks;
        public readonly ReadOnlyCollection<OutputEnvironmentRow> Outputs;
        OutputEnvironmentSample(long generation,bool known,OutputEnvironmentReason reason,OutputEnvironmentGlobalBlock blocks,List<OutputEnvironmentRow> outputs)
        {
            if(generation<=0)throw new ArgumentOutOfRangeException("generation");
            TopologyGeneration=generation;Known=known;Reason=reason;GlobalBlocks=blocks;Outputs=outputs.AsReadOnly();
        }
        // Declaration factories support downstream pure boundary tests. They do
        // not turn a synthetic row into physical fullscreen acceptance.
        public static OutputEnvironmentSample CreateKnown(long generation,IEnumerable<OutputEnvironmentRow> outputs,bool ownOrShellForeground=false)
        {
            if(outputs==null)throw new ArgumentNullException("outputs");
            var rows=new List<OutputEnvironmentRow>();var keys=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in outputs){
                if(row==null||rows.Count>=64||!keys.Add(row.Key)||(ownOrShellForeground&&row.Fullscreen))throw new ArgumentException("Invalid environment output collection.","outputs");
                rows.Add(row);
            }
            if(rows.Count==0)throw new ArgumentException("An environment output is required.","outputs");
            return new OutputEnvironmentSample(generation,true,ownOrShellForeground?OutputEnvironmentReason.OwnOrShellForeground:OutputEnvironmentReason.None,OutputEnvironmentGlobalBlock.None,rows);
        }
        public static OutputEnvironmentSample CreateUnavailable(long generation,OutputEnvironmentReason reason)
        {
            if(reason==OutputEnvironmentReason.None||reason==OutputEnvironmentReason.OwnOrShellForeground||!Enum.IsDefined(typeof(OutputEnvironmentReason),reason))throw new ArgumentOutOfRangeException("reason");
            var blocks=OutputEnvironmentGlobalBlock.EnvironmentUnknown;
            if(reason==OutputEnvironmentReason.SessionUnavailable||reason==OutputEnvironmentReason.InputDesktopUnavailable||reason==OutputEnvironmentReason.SessionChanged||reason==OutputEnvironmentReason.DesktopChanged)
                blocks|=OutputEnvironmentGlobalBlock.SessionUnavailable;
            return new OutputEnvironmentSample(generation,false,reason,blocks,new List<OutputEnvironmentRow>());
        }
    }
    static class EnvironmentAliases
    {
        internal static string Canonical(string key)
        {
            if(String.IsNullOrEmpty(key)||key.Length>64)throw new ArgumentException("Invalid logical output alias.","key");
            for(int i=0;i<key.Length;i++){
                char c=key[i];bool word=(c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9');
                if(!word&&(i==0||(c!='-'&&c!='_'&&c!='.')))throw new ArgumentException("Invalid logical output alias.","key");
            }
            return key.ToLowerInvariant();
        }
    }
    // All outputs derive from the SAME bracketed environment observation, never
    // separate NativeEnvironment.Capture calls that overwrite a global Fullscreen.
    public sealed partial class OutputEnvironmentCapture
    {
        readonly INativeEnvironmentQueries queries;
        readonly Func<bool> perMonitorV2;
        readonly int ownPid,sessionId,maximumOutputs,ownerThread=Thread.CurrentThread.ManagedThreadId;
        public OutputEnvironmentCapture(INativeEnvironmentQueries queries,int ownProcessId,int currentSessionId,Func<bool> perMonitorV2,int maximumOutputs=16)
        {
            if(queries==null)throw new ArgumentNullException("queries");
            if(perMonitorV2==null)throw new ArgumentNullException("perMonitorV2");
            if(ownProcessId<=0)throw new ArgumentOutOfRangeException("ownProcessId");
            if(currentSessionId<0)throw new ArgumentOutOfRangeException("currentSessionId");
            if(maximumOutputs<=0||maximumOutputs>64)throw new ArgumentOutOfRangeException("maximumOutputs");
            this.queries=queries;this.perMonitorV2=perMonitorV2;ownPid=ownProcessId;sessionId=currentSessionId;this.maximumOutputs=maximumOutputs;
        }
        static bool Valid(OutputEnvironmentTarget row)
        {return row!=null&&row.Width>0&&row.Height>0&&(long)row.X+row.Width<=Int32.MaxValue&&(long)row.Y+row.Height<=Int32.MaxValue;}
        bool Targets(IEnumerable<OutputEnvironmentTarget> declared,out List<OutputEnvironmentTarget> rows)
        {
            rows=new List<OutputEnvironmentTarget>();if(declared==null)return false;
            var aliases=new HashSet<string>(StringComparer.Ordinal);
            try{
                foreach(var row in declared){
                    if(rows.Count>=maximumOutputs||!Valid(row))return false;
                    string key=EnvironmentAliases.Canonical(row.Key);if(!aliases.Add(key))return false;
                    foreach(var prior in rows)if(row.X<(long)prior.X+prior.Width&&prior.X<(long)row.X+row.Width&&row.Y<(long)prior.Y+prior.Height&&prior.Y<(long)row.Y+row.Height)return false;
                    rows.Add(new OutputEnvironmentTarget(key,row.X,row.Y,row.Width,row.Height));
                }
            }catch(Exception){return false;}
            return rows.Count>0;
        }
        bool ForegroundValid(IntPtr window,out uint pid)
        {
            pid=0;return window!=IntPtr.Zero&&queries.WindowExists(window)&&queries.WindowVisible(window)&&!queries.WindowIconic(window)&&queries.TryProcessId(window,out pid)&&pid>0;
        }
        bool ShellValid(IntPtr window,out uint pid)
        {pid=0;return window!=IntPtr.Zero&&queries.WindowExists(window)&&queries.TryProcessId(window,out pid)&&pid>0;}
        static bool RectangleOf(EnvironmentWindowRect raw,out Rectangle rectangle)
        {
            rectangle=Rectangle.Empty;long width=(long)raw.Right-raw.Left,height=(long)raw.Bottom-raw.Top;
            if(width<=0||width>Int32.MaxValue||height<=0||height>Int32.MaxValue)return false;
            rectangle=new Rectangle(raw.Left,raw.Top,(int)width,(int)height);return true;
        }
        static bool Same(EnvironmentWindowRect a,EnvironmentWindowRect b)
        {return a.Left==b.Left&&a.Top==b.Top&&a.Right==b.Right&&a.Bottom==b.Bottom;}
        public OutputEnvironmentSample Capture(long topologyGeneration,IEnumerable<OutputEnvironmentTarget> declared)
        {
            if(topologyGeneration<=0)throw new ArgumentOutOfRangeException("topologyGeneration");
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread)return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.WrongThread);
            List<OutputEnvironmentTarget> targets;
            if(!Targets(declared,out targets))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.InvalidTopology);
            try{
                if(!perMonitorV2())return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.AwarenessUnconfirmed);
                if(!queries.SessionActive(sessionId))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.SessionUnavailable);
                if(!queries.DefaultInputDesktop())return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.InputDesktopUnavailable);
                IntPtr foreground=queries.ForegroundWindow();uint pid;
                if(!ForegroundValid(foreground,out pid))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.ForegroundUnavailable);
                IntPtr shell=queries.ShellWindow();uint shellPid;
                if(!ShellValid(shell,out shellPid))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.ShellUnavailable);
                bool ignored=pid==(uint)ownPid||pid==shellPid;
                var first=new EnvironmentWindowRect();Rectangle window=Rectangle.Empty;
                if(!ignored){
                    if(!queries.TryWindowRect(foreground,out first))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.GeometryUnavailable);
                    if(!RectangleOf(first,out window))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.InvalidGeometry);
                }
                if(!ignored){
                    EnvironmentWindowRect after;Rectangle checkedRectangle;
                    if(!queries.TryWindowRect(foreground,out after)||!RectangleOf(after,out checkedRectangle)||!Same(first,after))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.GeometryChanged);
                }
                // Bracket BOTH geometry reads with identity verification. A
                // foreground switch inside the second query cannot qualify the
                // old HWND merely because its rectangle happened to stay equal.
                uint afterPid,afterShellPid;
                if(queries.ForegroundWindow()!=foreground||!ForegroundValid(foreground,out afterPid)||afterPid!=pid)return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.ForegroundChanged);
                if(queries.ShellWindow()!=shell||!ShellValid(shell,out afterShellPid)||afterShellPid!=shellPid)return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.ShellChanged);
                if(!queries.SessionActive(sessionId))return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.SessionChanged);
                if(!queries.DefaultInputDesktop())return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.DesktopChanged);
                if(!perMonitorV2())return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.AwarenessUnconfirmed);
                var rows=new List<OutputEnvironmentRow>();
                foreach(var target in targets)rows.Add(new OutputEnvironmentRow(target.Key,!ignored&&NativeEnvironment.Covers(window,new Rectangle(target.X,target.Y,target.Width,target.Height))));
                return OutputEnvironmentSample.CreateKnown(topologyGeneration,rows,ignored);
            }catch(Exception){return OutputEnvironmentSample.CreateUnavailable(topologyGeneration,OutputEnvironmentReason.QueryFailed);}
        }
    }
}
