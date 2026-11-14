using System;

namespace FishingDesktop.GameCore
{
    internal sealed class FloatCastLaunchPlan
    {
        internal readonly FloatStillWaterGeometry Geometry;
        internal readonly string WaterId,SpotId;
        internal readonly long GeometryVersion,RequiredLineMm;
        internal readonly int U,V,PowerPermille,AnchorXmm,AnchorYmm,AnchorZmm,MaximumFlightMilliseconds;
        internal readonly FloatWaterSample Target,PredictedSample;
        internal readonly double PredictedXmm,PredictedYmm,PredictedZmm;
        internal readonly double MinimumSpeedMmPerSecond,FullSpeedMmPerSecond,LaunchSpeedMmPerSecond,LaunchAngleDegrees,GravityMmPerSecondSquared;
        internal readonly double FlightSeconds,FullPowerFlightSeconds,MaximumFlightLineMm,MaximumHookLineMm,MaximumLineEnvelopeMm;
        internal FloatCastLaunchPlan(FloatStillWaterGeometry geometry,int u,int v,int power,FloatWaterSample target,FloatWaterSample predicted,
            double x,double z,int minimumSpeed,double fullSpeed,double launchSpeed,double seconds,double fullSeconds,double flightLine,double hookLine,long requiredLine)
        {
            Geometry=geometry;WaterId=geometry.WaterId;SpotId=geometry.SpotId;GeometryVersion=geometry.Version;
            U=u;V=v;PowerPermille=power;Target=target;PredictedSample=predicted;
            AnchorXmm=geometry.AnchorXmm;AnchorYmm=geometry.AnchorYmm;AnchorZmm=geometry.AnchorZmm;
            PredictedXmm=x;PredictedYmm=geometry.SurfaceYmm;PredictedZmm=z;
            MinimumSpeedMmPerSecond=minimumSpeed;FullSpeedMmPerSecond=fullSpeed;LaunchSpeedMmPerSecond=launchSpeed;
            LaunchAngleDegrees=FloatCastLaunchPlanner.LaunchAngleDegrees;GravityMmPerSecondSquared=FloatCastLaunchPlanner.GravityMmPerSecondSquared;
            FlightSeconds=seconds;FullPowerFlightSeconds=fullSeconds;MaximumFlightMilliseconds=Math.Max(1,checked((int)Math.Ceiling(fullSeconds*1000)));
            MaximumFlightLineMm=flightLine;MaximumHookLineMm=hookLine;MaximumLineEnvelopeMm=Math.Max(flightLine,hookLine);RequiredLineMm=requiredLine;
        }
    }
    internal sealed class FloatCastLaunchPlanResult
    {
        internal readonly bool Accepted;
        internal readonly string Code;
        internal readonly FloatCastLaunchPlan Plan;
        internal FloatCastLaunchPlanResult(string code,FloatCastLaunchPlan plan=null){Accepted=plan!=null;Code=code;Plan=plan;}
    }

    // Closed-form, no-drag preparation for the existing flight model. A plan
    // neither commits a cast nor owns line. The host still checks inventory and
    // confirms the cast before constructing its one flight runtime.
    internal static class FloatCastLaunchPlanner
    {
        internal const double LaunchAngleDegrees=40,GravityMmPerSecondSquared=9810;
        internal const int LineSafetyMarginMm=2;
        static bool Finite(double value){return !Double.IsNaN(value)&&!Double.IsInfinity(value);}
        static FloatCastLaunchPlanResult Reject(string code){return new FloatCastLaunchPlanResult(code);}
        internal static FloatCastLaunchPlanResult Plan(FloatStillWaterGeometry geometry,int u,int v,long expectedVersion,int powerPermille,int minimumSpeedMmPerSecond=6500)
        {
            if(geometry==null)return Reject("geometry-required");
            if(powerPermille<0||powerPermille>1000)return Reject("power-out-of-range");
            if(minimumSpeedMmPerSecond<1||minimumSpeedMmPerSecond>1000000)return Reject("launch-speed-out-of-range");
            var aim=geometry.ResolveAim(u,v,expectedVersion);if(!aim.Accepted)return Reject(aim.Code);
            var target=aim.Sample;
            double dx=(double)target.Xmm-geometry.AnchorXmm,dz=(double)target.Zmm-geometry.AnchorZmm;
            double distance=Math.Sqrt(dx*dx+dz*dz),height=(double)geometry.AnchorYmm-geometry.SurfaceYmm;
            double radians=LaunchAngleDegrees*Math.PI/180,cos=Math.Cos(radians),sin=Math.Sin(radians),tan=Math.Tan(radians);
            // 0 = h + R*tan(theta) - g*R^2/(2*v^2*cos(theta)^2).
            double fullSpeed=Math.Sqrt(GravityMmPerSecondSquared*distance*distance/(2*cos*cos*(height+distance*tan)));
            if(!Finite(fullSpeed)||fullSpeed<1||fullSpeed>1000000)return Reject("launch-speed-out-of-range");
            if(fullSpeed<minimumSpeedMmPerSecond)return Reject("target-too-near");
            double launchSpeed=minimumSpeedMmPerSecond+(fullSpeed-minimumSpeedMmPerSecond)*powerPermille/1000;
            double vy=launchSpeed*sin,fullVy=fullSpeed*sin;
            double seconds=(vy+Math.Sqrt(vy*vy+2*GravityMmPerSecondSquared*height))/GravityMmPerSecondSquared;
            double fullSeconds=(fullVy+Math.Sqrt(fullVy*fullVy+2*GravityMmPerSecondSquared*height))/GravityMmPerSecondSquared;
            if(!Finite(seconds)||!Finite(fullSeconds)||seconds<=0||fullSeconds*1000>600000)return Reject("flight-timeout");
            // Match the flight model's scale-normalized heading calculation.
            double scale=Math.Max(Math.Abs(dx),Math.Abs(dz)),nx=dx/scale,nz=dz/scale,norm=Math.Sqrt(nx*nx+nz*nz);
            double horizontalSpeed=launchSpeed*cos,vx=horizontalSpeed*nx/norm,vz=horizontalSpeed*nz/norm;
            double x=geometry.AnchorXmm+vx*seconds,z=geometry.AnchorZmm+vz*seconds;
            if(!Finite(x)||!Finite(z))return Reject("launch-nonfinite");
            if(x<geometry.MinimumXmm||x>geometry.MaximumXmm||z<geometry.MinimumZmm||z>geometry.MaximumZmm)return Reject("predicted-outside-water");
            if(x<geometry.SafeWorkMinimumXmm||x>geometry.SafeWorkMaximumXmm||z<geometry.SafeWorkMinimumZmm||z>geometry.SafeWorkMaximumZmm)
                return Reject("predicted-outside-safe-work");
            var predicted=geometry.Sample(checked((int)Math.Round(x,MidpointRounding.AwayFromZero)),checked((int)Math.Round(z,MidpointRounding.AwayFromZero)),expectedVersion);
            if(!predicted.Accepted)return Reject(predicted.Code);

            // For theta=40 degrees, vy^2-8*vh^2 < 0: the derivative of
            // radius^2=t^2*(vh^2+(vy-g*t/2)^2) stays positive for t>0.
            // Thus each trajectory's largest span is its water contact. The
            // contact distance increases with speed, so the full-power contact
            // bounds EVERY power in [0,1000], not just the requested release.
            // During the subsequent vertical 200 mm sink, span grows again;
            // use that hook depth too. This reservation covers launch/sink, not
            // all later fish motion, whose paid-out line remains separately bound.
            double fullDistance=fullSpeed*cos*fullSeconds;
            double flightLine=Math.Sqrt(fullDistance*fullDistance+height*height);
            double hookHeight=height+FloatStillWaterGeometry.FloatDepthMm;
            double hookLine=Math.Sqrt(fullDistance*fullDistance+hookHeight*hookHeight);
            double envelope=Math.Max(flightLine,hookLine);
            if(!Finite(envelope)||envelope<=0||envelope>100000000-LineSafetyMarginMm)return Reject("line-envelope-out-of-range");
            long required=checked((long)Math.Ceiling(envelope)+LineSafetyMarginMm);
            var plan=new FloatCastLaunchPlan(geometry,u,v,powerPermille,target,predicted.Sample,x,z,minimumSpeedMmPerSecond,fullSpeed,launchSpeed,
                seconds,fullSeconds,flightLine,hookLine,required);
            return new FloatCastLaunchPlanResult("launch-planned",plan);
        }
    }
}
