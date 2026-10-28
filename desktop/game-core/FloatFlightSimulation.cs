using System;
using System.Collections.Generic;
using System.Threading;

namespace FishingDesktop.GameCore
{
    // Immutable host-owned values in millimetres and seconds. This independent
    // flat-water model grants no storage authority and cannot confirm a cast.
    internal sealed class FloatFlightPoint
    {
        internal readonly double Xmm,Ymm,Zmm;
        internal FloatFlightPoint(double xMm,double yMm,double zMm)
        {
            FloatFlightMath.Range(xMm,-1e9,1e9,"xMm");FloatFlightMath.Range(yMm,-1e9,1e9,"yMm");FloatFlightMath.Range(zMm,-1e9,1e9,"zMm");
            Xmm=xMm;Ymm=yMm;Zmm=zMm;
        }
    }
    internal sealed class FloatFlightWater
    {
        internal readonly double Ymm,MinXmm,MaxXmm,MinZmm,MaxZmm;
        internal FloatFlightWater(double yMm,double minXmm,double maxXmm,double minZmm,double maxZmm)
        {
            FloatFlightMath.Range(yMm,-1e9,1e9,"waterYmm");FloatFlightMath.Range(minXmm,-1e9,1e9,"minXmm");FloatFlightMath.Range(maxXmm,-1e9,1e9,"maxXmm");
            FloatFlightMath.Range(minZmm,-1e9,1e9,"minZmm");FloatFlightMath.Range(maxZmm,-1e9,1e9,"maxZmm");
            if(minXmm>=maxXmm || minZmm>=maxZmm)throw new ArgumentException("Water rectangle must have positive area.");
            Ymm=yMm;MinXmm=minXmm;MaxXmm=maxXmm;MinZmm=minZmm;MaxZmm=maxZmm;
        }
        internal bool Contains(double xMm,double zMm){return xMm>=MinXmm && xMm<=MaxXmm && zMm>=MinZmm && zMm<=MaxZmm;}
    }
    internal sealed class FloatFlightRules
    {
        internal readonly double MinimumSpeedMmPerSecond,MaximumSpeedMmPerSecond,LaunchAngleDegrees,GravityMmPerSecondSquared;
        internal readonly int MaximumFlightMilliseconds;
        internal FloatFlightRules(double minimumSpeedMmPerSecond,double maximumSpeedMmPerSecond,double launchAngleDegrees,double gravityMmPerSecondSquared,int maximumFlightMilliseconds)
        {
            FloatFlightMath.Range(minimumSpeedMmPerSecond,1,1e6,"minimumSpeedMmPerSecond");FloatFlightMath.Range(maximumSpeedMmPerSecond,minimumSpeedMmPerSecond,1e6,"maximumSpeedMmPerSecond");
            FloatFlightMath.Range(launchAngleDegrees,0,90,"launchAngleDegrees");if(launchAngleDegrees==90)throw new ArgumentOutOfRangeException("launchAngleDegrees");
            FloatFlightMath.Range(gravityMmPerSecondSquared,1,1e6,"gravityMmPerSecondSquared");
            if(maximumFlightMilliseconds<1 || maximumFlightMilliseconds>600000)throw new ArgumentOutOfRangeException("maximumFlightMilliseconds");
            MinimumSpeedMmPerSecond=minimumSpeedMmPerSecond;MaximumSpeedMmPerSecond=maximumSpeedMmPerSecond;LaunchAngleDegrees=launchAngleDegrees;
            GravityMmPerSecondSquared=gravityMmPerSecondSquared;MaximumFlightMilliseconds=maximumFlightMilliseconds;
        }
    }
    internal sealed class FloatFlightLaunch
    {
        internal readonly FloatFlightPoint Origin,Aim;
        internal readonly FloatFlightWater Water;
        internal readonly FloatFlightRules Rules;
        internal readonly int PowerPermille;
        internal readonly double LineBudgetMm;
        internal FloatFlightLaunch(FloatFlightPoint origin,FloatFlightPoint aim,FloatFlightWater water,FloatFlightRules rules,int powerPermille,double lineBudgetMm)
        {
            if(origin==null || aim==null || water==null || rules==null)throw new ArgumentNullException("launch dependency");
            if(origin.Ymm<=water.Ymm)throw new ArgumentException("Launch origin must be above the water plane.");
            if(origin.Xmm==aim.Xmm && origin.Zmm==aim.Zmm)throw new ArgumentException("Aim must define a horizontal heading.");
            if(powerPermille<0 || powerPermille>1000)throw new ArgumentOutOfRangeException("powerPermille");
            FloatFlightMath.Range(lineBudgetMm,1,1e9,"lineBudgetMm");
            Origin=origin;Aim=aim;Water=water;Rules=rules;PowerPermille=powerPermille;LineBudgetMm=lineBudgetMm;
        }
    }
    internal enum FloatFlightPhase { Flying,Waiting,Invalid,Ended }
    internal sealed class FloatFlightSnapshot
    {
        internal readonly Guid SessionId;
        internal readonly FloatFlightPhase Phase;
        internal readonly string Reason;
        internal readonly int ElapsedMilliseconds,DurationMilliseconds,PowerPermille;
        internal readonly double Xmm,Ymm,Zmm,LineUsedMm;
        internal FloatFlightSnapshot(Guid session,FloatFlightPhase phase,string reason,int elapsed,int duration,int power,double x,double y,double z,double line)
        {SessionId=session;Phase=phase;Reason=reason;ElapsedMilliseconds=elapsed;DurationMilliseconds=duration;PowerPermille=power;Xmm=x;Ymm=y;Zmm=z;LineUsedMm=line;}
    }
    internal sealed class FloatFlightResult
    {
        internal readonly bool Accepted,TerminalEvent;
        internal readonly string Code;
        internal readonly FloatFlightSnapshot Snapshot;
        internal FloatFlightResult(bool accepted,bool terminal,string code,FloatFlightSnapshot snapshot)
        {Accepted=accepted;TerminalEvent=terminal;Code=code;Snapshot=snapshot;}
    }
    internal static class FloatFlightMath
    {
        internal static void Range(double value,double minimum,double maximum,string name)
        {if(Double.IsNaN(value)||Double.IsInfinity(value)||value<minimum||value>maximum)throw new ArgumentOutOfRangeException(name);}
    }
    internal sealed class FloatFlightSimulation
    {
        readonly int thread=Thread.CurrentThread.ManagedThreadId;
        readonly Guid session;
        readonly FloatFlightLaunch launch;
        readonly double vx,vy,vz,horizontalSpeed,terminalSeconds;
        readonly int terminalMilliseconds;
        readonly FloatFlightPhase contactPhase;
        readonly string contactReason;
        FloatFlightPhase phase=FloatFlightPhase.Flying;
        string reason="flying";
        int elapsed;
        bool terminalEmitted;

        // The caller must construct exactly once, only after a newly confirmed
        // cast commit. A GUID here is identity, never proof of that commit.
        internal FloatFlightSimulation(Guid confirmedSessionId,FloatFlightLaunch launch)
        {
            if(confirmedSessionId==Guid.Empty)throw new ArgumentException("A confirmed session identity is required.","confirmedSessionId");
            if(launch==null)throw new ArgumentNullException("launch");
            session=confirmedSessionId;this.launch=launch;
            double speed=launch.Rules.MinimumSpeedMmPerSecond+(launch.Rules.MaximumSpeedMmPerSecond-launch.Rules.MinimumSpeedMmPerSecond)*launch.PowerPermille/1000;
            double angle=launch.Rules.LaunchAngleDegrees*Math.PI/180;
            horizontalSpeed=speed*Math.Cos(angle);vy=speed*Math.Sin(angle);
            double dx=launch.Aim.Xmm-launch.Origin.Xmm,dz=launch.Aim.Zmm-launch.Origin.Zmm;
            double scale=Math.Max(Math.Abs(dx),Math.Abs(dz)),nx=dx/scale,nz=dz/scale,heading=Math.Sqrt(nx*nx+nz*nz);
            vx=horizontalSpeed*nx/heading;vz=horizontalSpeed*nz/heading;
            double gravity=launch.Rules.GravityMmPerSecondSquared,height=launch.Origin.Ymm-launch.Water.Ymm;
            double waterSeconds=(vy+Math.Sqrt(vy*vy+2*gravity*height))/gravity;
            double timeoutSeconds=launch.Rules.MaximumFlightMilliseconds/1000.0;
            terminalSeconds=Math.Min(waterSeconds,timeoutSeconds);
            if(waterSeconds<=timeoutSeconds){
                bool inside=launch.Water.Contains(launch.Origin.Xmm+vx*waterSeconds,launch.Origin.Zmm+vz*waterSeconds);
                contactPhase=inside?FloatFlightPhase.Waiting:FloatFlightPhase.Invalid;
                contactReason=inside?"water-contact":"outside-water";
            }else{contactPhase=FloatFlightPhase.Invalid;contactReason="flight-timeout";}
            double? lineSeconds=FindFirstLineContact(terminalSeconds);
            // Reaching the available span counts as line exhaustion, including
            // an exact tie with water contact or a tangent interior maximum.
            if(lineSeconds.HasValue && lineSeconds.Value<=terminalSeconds){terminalSeconds=lineSeconds.Value;contactPhase=FloatFlightPhase.Invalid;contactReason="line-limit";}
            terminalMilliseconds=Math.Max(1,(int)Math.Ceiling(terminalSeconds*1000));
        }
        void Check(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Use the flight owner's host thread.");}
        double RadiusSquared(double time)
        {
            double horizontal=horizontalSpeed*time,vertical=vy*time-.5*launch.Rules.GravityMmPerSecondSquared*time*time;
            return horizontal*horizontal+vertical*vertical;
        }
        double? FindFirstLineContact(double before)
        {
            var boundaries=new List<double>{0};
            double gravity=launch.Rules.GravityMmPerSecondSquared;
            double discriminant=vy*vy-8*horizontalSpeed*horizontalSpeed;
            if(discriminant>=0){
                double root=Math.Sqrt(discriminant),first=(3*vy-root)/(2*gravity),second=(3*vy+root)/(2*gravity);
                if(first>0 && first<before)boundaries.Add(first);
                if(second>0 && second<before && second>first)boundaries.Add(second);
            }
            boundaries.Add(before);
            double limit=launch.LineBudgetMm*launch.LineBudgetMm;
            for(int index=1;index<boundaries.Count;index++){
                double low=boundaries[index-1],high=boundaries[index];
                if(RadiusSquared(low)>=limit)return low;
                // A small relative comparison tolerance retains an exact
                // tangent despite floating-point squaring roundoff. The path
                // and output line span are still evaluated and bounded below.
                if(RadiusSquared(high)<limit && limit-RadiusSquared(high)>limit*1e-12)continue;
                // Extrema split the radial quartic into monotonic intervals.
                // Fixed iterations precompute contact independent of frames.
                for(int iteration=0;iteration<64;iteration++){
                    double middle=(low+high)/2;
                    if(RadiusSquared(middle)>=limit)high=middle;else low=middle;
                }
                return high;
            }
            return null;
        }
        FloatFlightSnapshot Describe()
        {
            double time=Math.Min(elapsed/1000.0,terminalSeconds);
            double x=launch.Origin.Xmm+vx*time,z=launch.Origin.Zmm+vz*time;
            double y=launch.Origin.Ymm+vy*time-.5*launch.Rules.GravityMmPerSecondSquared*time*time;
            if(phase!=FloatFlightPhase.Flying && elapsed>=terminalMilliseconds && (contactReason=="water-contact"||contactReason=="outside-water"))y=launch.Water.Ymm;
            double line=Math.Min(launch.LineBudgetMm,Math.Sqrt(RadiusSquared(time)));
            return new FloatFlightSnapshot(session,phase,reason,elapsed,terminalMilliseconds,launch.PowerPermille,x,y,z,line);
        }
        internal FloatFlightSnapshot Snapshot{get{Check();return Describe();}}
        FloatFlightResult Result(bool accepted,bool terminal,string code){return new FloatFlightResult(accepted,terminal,code,Describe());}
        internal FloatFlightResult Advance(Guid sessionId,int activeMilliseconds)
        {
            Check();
            if(activeMilliseconds<0 || activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(sessionId!=session)return Result(false,false,"session-not-current");
            if(phase!=FloatFlightPhase.Flying)return Result(false,false,"flight-not-active");
            if(activeMilliseconds==0)return Result(true,false,"flying");
            elapsed=Math.Min(terminalMilliseconds,checked(elapsed+activeMilliseconds));
            if(elapsed<terminalMilliseconds)return Result(true,false,"flying");
            phase=contactPhase;reason=contactReason;
            bool emit=!terminalEmitted;terminalEmitted=true;return Result(true,emit,reason);
        }
        internal FloatFlightResult End(Guid sessionId)
        {
            Check();if(sessionId!=session)return Result(false,false,"session-not-current");
            if(phase==FloatFlightPhase.Ended)return Result(false,false,"flight-not-active");
            phase=FloatFlightPhase.Ended;reason="ended";
            bool emit=!terminalEmitted;terminalEmitted=true;return Result(true,emit,reason);
        }
    }
}
