using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace FishingDesktop.Diagnostics.OutputTopology
{
    public enum TopologyUnavailableReason
    {
        None, WrongThread, AwarenessUnconfirmed, EnumerationFailed, EmptyTopology,
        InvalidTopology, TopologyChanged, DpiUnavailable, DpiChanged,
        WindowSafetyUnconfirmed, CleanupUnconfirmed, QueryFailed
    }

    public sealed class TopologyOutput
    {
        public readonly string Key;
        public readonly int X,Y,Width,Height,DpiX,DpiY;
        public readonly bool Primary;
        public TopologyOutput(string key,int x,int y,int width,int height,int dpiX,int dpiY,bool primary)
        {
            if(String.IsNullOrEmpty(key)||key.Length>64)throw new ArgumentException("Invalid logical alias.","key");
            for(int i=0;i<key.Length;i++){
                char c=key[i];bool word=(c>='a'&&c<='z')||(c>='A'&&c<='Z')||(c>='0'&&c<='9');
                if(!word&&(i==0||(c!='-'&&c!='_'&&c!='.')))throw new ArgumentException("Invalid logical alias.","key");
            }
            if(!TopologyValidation.RectValid(x,y,width,height))throw new ArgumentOutOfRangeException("width");
            if(dpiX<=0||dpiY<=0)throw new ArgumentOutOfRangeException("dpiX");
            Key=key.ToLowerInvariant();X=x;Y=y;Width=width;Height=height;DpiX=dpiX;DpiY=dpiY;Primary=primary;
        }
    }

    public sealed class TopologySample
    {
        // A capture serial orders observations. It is NOT a topology generation.
        public readonly long CaptureSerial;
        public readonly bool Known;
        public readonly TopologyUnavailableReason Reason;
        public readonly ReadOnlyCollection<TopologyOutput> Outputs;
        TopologySample(long serial,bool known,TopologyUnavailableReason reason,List<TopologyOutput> outputs)
        {
            if(serial<=0)throw new ArgumentOutOfRangeException("serial");
            CaptureSerial=serial;Known=known;Reason=reason;Outputs=outputs.AsReadOnly();
        }
        // Also available for a downstream pure boundary test. This factory does
        // not turn caller-declared data into evidence of physical OS testing.
        public static TopologySample CreateKnown(long serial,IEnumerable<TopologyOutput> outputs)
        {
            if(outputs==null)throw new ArgumentNullException("outputs");
            var copy=new List<TopologyOutput>();var keys=new HashSet<string>(StringComparer.Ordinal);int primary=0;
            foreach(var row in outputs){
                if(row==null||copy.Count>=64||!keys.Add(row.Key))throw new ArgumentException("Invalid output collection.","outputs");
                foreach(var prior in copy)if(TopologyValidation.Overlap(prior.X,prior.Y,prior.Width,prior.Height,row.X,row.Y,row.Width,row.Height))
                    throw new ArgumentException("Ambiguous overlapping outputs.","outputs");
                copy.Add(row);if(row.Primary)primary++;
            }
            if(copy.Count==0||primary!=1)throw new ArgumentException("Exactly one primary output is required.","outputs");
            return new TopologySample(serial,true,TopologyUnavailableReason.None,copy);
        }
        public static TopologySample CreateUnavailable(long serial,TopologyUnavailableReason reason)
        {
            if(reason==TopologyUnavailableReason.None||!Enum.IsDefined(typeof(TopologyUnavailableReason),reason))throw new ArgumentOutOfRangeException("reason");
            return new TopologySample(serial,false,reason,new List<TopologyOutput>());
        }
    }

    // Opaque native identity is deliberately kept out of TopologySample. It has
    // meaning only for comparing observations during one bounded capture.
    public sealed class RawMonitor
    {
        public readonly long Handle;
        public readonly int X,Y,Width,Height;
        public readonly bool Primary;
        public RawMonitor(long handle,int x,int y,int width,int height,bool primary)
        {Handle=handle;X=x;Y=y;Width=width;Height=height;Primary=primary;}
    }
    public sealed class DpiProbeResult
    {
        public readonly uint Dpi;
        public readonly bool OwnWindow,Hidden,NoActivate,PerMonitorV2,MonitorMatches,Released;
        public DpiProbeResult(uint dpi,bool ownWindow,bool hidden,bool noActivate,bool perMonitorV2,bool monitorMatches,bool released)
        {Dpi=dpi;OwnWindow=ownWindow;Hidden=hidden;NoActivate=noActivate;PerMonitorV2=perMonitorV2;MonitorMatches=monitorMatches;Released=released;}
    }
    public interface ITopologyQuery
    {
        bool IsPerMonitorV2 {get;}
        IEnumerable<RawMonitor> Enumerate();
        // Must create/query/release only its own hidden NoActivate HWND. A
        // failed release must be reported even when all DPI queries succeeded.
        DpiProbeResult Measure(RawMonitor monitor);
    }
    static class TopologyValidation
    {
        internal static bool RectValid(int x,int y,int width,int height)
        {return width>0&&height>0&&(long)x+width<=Int32.MaxValue&&(long)y+height<=Int32.MaxValue;}
        internal static bool Overlap(int x,int y,int width,int height,int otherX,int otherY,int otherWidth,int otherHeight)
        {return x<(long)otherX+otherWidth&&otherX<(long)x+width&&y<(long)otherY+otherHeight&&otherY<(long)y+height;}
        internal static bool Same(RawMonitor a,RawMonitor b)
        {return a!=null&&b!=null&&a.Handle==b.Handle&&a.X==b.X&&a.Y==b.Y&&a.Width==b.Width&&a.Height==b.Height&&a.Primary==b.Primary;}
    }

    // Independent owner-thread, read-only observation. No last-known-good cache,
    // output policy, event loop, view lifecycle or topology generation lives here.
    public sealed class TopologyCapture
    {
        readonly ITopologyQuery query;
        readonly int ownerThread=Thread.CurrentThread.ManagedThreadId,maximumOutputs;
        long serial;
        public TopologyCapture(ITopologyQuery query,int maximumOutputs=16)
        {
            if(query==null)throw new ArgumentNullException("query");
            if(maximumOutputs<=0||maximumOutputs>64)throw new ArgumentOutOfRangeException("maximumOutputs");
            this.query=query;this.maximumOutputs=maximumOutputs;
        }
        static int Compare(RawMonitor a,RawMonitor b)
        {
            int value=a.X.CompareTo(b.X);if(value!=0)return value;value=a.Y.CompareTo(b.Y);if(value!=0)return value;
            value=a.Width.CompareTo(b.Width);if(value!=0)return value;value=a.Height.CompareTo(b.Height);if(value!=0)return value;
            value=a.Primary.CompareTo(b.Primary);return value!=0?value:a.Handle.CompareTo(b.Handle);
        }
        TopologyUnavailableReason Read(out List<RawMonitor> rows)
        {
            rows=new List<RawMonitor>();var handles=new HashSet<long>();int primary=0;
            try{
                var monitors=query.Enumerate();if(monitors==null)return TopologyUnavailableReason.EnumerationFailed;
                foreach(var row in monitors){
                    if(row==null||rows.Count>=maximumOutputs||row.Handle==0||!handles.Add(row.Handle)||!TopologyValidation.RectValid(row.X,row.Y,row.Width,row.Height))return TopologyUnavailableReason.InvalidTopology;
                    foreach(var prior in rows)if(TopologyValidation.Overlap(prior.X,prior.Y,prior.Width,prior.Height,row.X,row.Y,row.Width,row.Height))return TopologyUnavailableReason.InvalidTopology;
                    rows.Add(row);if(row.Primary)primary++;
                }
            }catch(Exception){return TopologyUnavailableReason.EnumerationFailed;}
            if(rows.Count==0)return TopologyUnavailableReason.EmptyTopology;
            if(primary!=1)return TopologyUnavailableReason.InvalidTopology;
            rows.Sort(Compare);return TopologyUnavailableReason.None;
        }
        static bool Same(List<RawMonitor> a,List<RawMonitor> b)
        {
            if(a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++)if(!TopologyValidation.Same(a[i],b[i]))return false;return true;
        }
        TopologyUnavailableReason Measure(List<RawMonitor> rows,out List<int> dpi)
        {
            dpi=new List<int>();
            foreach(var row in rows){
                DpiProbeResult result=query.Measure(row);
                if(result==null)return TopologyUnavailableReason.DpiUnavailable;
                if(!result.Released)return TopologyUnavailableReason.CleanupUnconfirmed;
                if(!result.OwnWindow||!result.Hidden||!result.NoActivate||!result.MonitorMatches)return TopologyUnavailableReason.WindowSafetyUnconfirmed;
                if(!result.PerMonitorV2)return TopologyUnavailableReason.AwarenessUnconfirmed;
                if(result.Dpi==0||result.Dpi>Int32.MaxValue)return TopologyUnavailableReason.DpiUnavailable;
                dpi.Add((int)result.Dpi);
            }
            return TopologyUnavailableReason.None;
        }
        public TopologySample Capture()
        {
            long current=Interlocked.Increment(ref serial);
            if(current<=0)throw new InvalidOperationException("Capture serial exhausted.");
            if(Thread.CurrentThread.ManagedThreadId!=ownerThread)return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.WrongThread);
            try{
                if(!query.IsPerMonitorV2)return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.AwarenessUnconfirmed);
                List<RawMonitor> before,middle,after;List<int> first,second;
                var reason=Read(out before);if(reason!=TopologyUnavailableReason.None)return TopologySample.CreateUnavailable(current,reason);
                reason=Measure(before,out first);if(reason!=TopologyUnavailableReason.None)return TopologySample.CreateUnavailable(current,reason);
                reason=Read(out middle);if(reason!=TopologyUnavailableReason.None)return TopologySample.CreateUnavailable(current,reason);
                if(!Same(before,middle))return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.TopologyChanged);
                reason=Measure(middle,out second);if(reason!=TopologyUnavailableReason.None)return TopologySample.CreateUnavailable(current,reason);
                reason=Read(out after);if(reason!=TopologyUnavailableReason.None)return TopologySample.CreateUnavailable(current,reason);
                if(!Same(before,after))return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.TopologyChanged);
                if(!query.IsPerMonitorV2)return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.AwarenessUnconfirmed);
                var outputs=new List<TopologyOutput>();
                for(int i=0;i<before.Count;i++){
                    if(first[i]!=second[i])return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.DpiChanged);
                    var row=before[i];outputs.Add(new TopologyOutput("output-"+(i+1),row.X,row.Y,row.Width,row.Height,first[i],first[i],row.Primary));
                }
                return TopologySample.CreateKnown(current,outputs);
            }catch(Exception){return TopologySample.CreateUnavailable(current,TopologyUnavailableReason.QueryFailed);}
        }
    }
}
