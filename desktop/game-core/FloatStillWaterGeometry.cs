using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    internal sealed class FloatWaterBedKnot
    {
        internal readonly int Zmm,DepthMm;
        internal FloatWaterBedKnot(int zMm,int depthMm)
        {
            if(zMm < -1000000||zMm > 1000000)throw new ArgumentOutOfRangeException("zMm");
            if(depthMm<1||depthMm>100000)throw new ArgumentOutOfRangeException("depthMm");
            Zmm=zMm;DepthMm=depthMm;
        }
    }
    internal sealed class FloatWaterSample
    {
        internal readonly string WaterId,SpotId;
        internal readonly long GeometryVersion;
        internal readonly int Xmm,Zmm,SurfaceYmm,BottomYmm,DepthMm,HookYmm,BottomClearanceMm;
        internal FloatWaterSample(FloatStillWaterGeometry geometry,int xMm,int zMm,int depthMm)
        {
            WaterId=geometry.WaterId;SpotId=geometry.SpotId;GeometryVersion=geometry.Version;
            Xmm=xMm;Zmm=zMm;SurfaceYmm=geometry.SurfaceYmm;DepthMm=depthMm;BottomYmm=SurfaceYmm-depthMm;
            HookYmm=SurfaceYmm-FloatStillWaterGeometry.FloatDepthMm;BottomClearanceMm=depthMm-FloatStillWaterGeometry.FloatDepthMm;
        }
    }
    internal sealed class FloatWaterSampleResult
    {
        internal readonly bool Accepted;
        internal readonly string Code;
        internal readonly FloatWaterSample Sample;
        internal FloatWaterSampleResult(string code,FloatWaterSample sample=null)
        {Accepted=sample!=null;Code=code;Sample=sample;}
    }

    // Add the cross-bank shelf to the longitudinal bed. Every patch between
    // consecutive stations is a plane, so the rendered triangles and the
    // physical soundings agree across their entire area.
    internal sealed class FloatStillWaterGeometry
    {
        internal const int FloatDepthMm=200,MinimumBottomClearanceMm=100;
        internal readonly string WaterId,SpotId;
        internal readonly long Version;
        internal readonly int AnchorXmm,AnchorYmm,AnchorZmm,SurfaceYmm;
        internal readonly int MinimumXmm,MaximumXmm,MinimumZmm,MaximumZmm;
        internal readonly int AimMinimumXmm,AimMaximumXmm,AimMinimumZmm,AimMaximumZmm;
        internal readonly int SafeWorkMinimumXmm,SafeWorkMaximumXmm,SafeWorkMinimumZmm,SafeWorkMaximumZmm;
        internal readonly ReadOnlyCollection<FloatWaterBedKnot> BedKnots;
        // The sign selects which bank leads to deep water. Both sides add a
        // nonnegative drop, preserving the validated safe-work rectangle.
        internal readonly int ShelfDepthMm,ShelfStartXmm,ShelfEndXmm;
        static string Key(string value)
        {
            if(String.IsNullOrEmpty(value)||value.Length>96)throw new ArgumentException("A bounded water or spot identifier is required.");
            foreach(char c in value)if(!((c>='a'&&c<='z')||(c>='0'&&c<='9')||c=='-'||c=='_'||c=='.'))
                throw new ArgumentException("Water and spot identifiers use lowercase ASCII.");
            return value;
        }
        internal FloatStillWaterGeometry(string waterId,string spotId,long version,
            int anchorXmm,int anchorYmm,int anchorZmm,int surfaceYmm,
            int minimumXmm,int maximumXmm,int minimumZmm,int maximumZmm,
            int aimMinimumXmm,int aimMaximumXmm,int aimMinimumZmm,int aimMaximumZmm,IEnumerable<FloatWaterBedKnot> bedKnots,int shelfDepthMm=0)
        {
            WaterId=Key(waterId);SpotId=Key(spotId);
            if(version<1)throw new ArgumentOutOfRangeException("version");
            foreach(int coordinate in new[]{anchorXmm,anchorYmm,anchorZmm,surfaceYmm,minimumXmm,maximumXmm,minimumZmm,maximumZmm,
                aimMinimumXmm,aimMaximumXmm,aimMinimumZmm,aimMaximumZmm})
                if(coordinate < -1000000||coordinate > 1000000)throw new ArgumentOutOfRangeException("coordinate");
            if(minimumXmm>=maximumXmm||minimumZmm>=maximumZmm)throw new ArgumentException("Water must have positive rectangular area.");
            if(anchorYmm<=surfaceYmm||anchorZmm>=minimumZmm||anchorXmm<minimumXmm||anchorXmm>maximumXmm)
                throw new ArgumentException("The anchor must be above the plane on the near bank.");
            if(aimMinimumXmm>=aimMaximumXmm||aimMinimumZmm>=aimMaximumZmm||aimMinimumXmm<minimumXmm||aimMaximumXmm>maximumXmm||
                aimMinimumZmm<minimumZmm||aimMaximumZmm>maximumZmm)throw new ArgumentException("Aim bounds must be a positive rectangle within the water.");
            if(bedKnots==null)throw new ArgumentNullException("bedKnots");
            var copy=new List<FloatWaterBedKnot>();
            foreach(var knot in bedKnots){
                if(knot==null||copy.Count>=128||(copy.Count>0&&copy[copy.Count-1].Zmm>=knot.Zmm))
                    throw new ArgumentException("Bed knots must be nonnull, bounded and strictly ordered.","bedKnots");
                copy.Add(knot);
            }
            if(copy.Count<2||copy[0].Zmm!=minimumZmm||copy[copy.Count-1].Zmm!=maximumZmm)
                throw new ArgumentException("The complete near-to-far water bed must be explicitly covered.","bedKnots");
            int safeMinimum,safeMaximum;FindSafeInterval(copy,out safeMinimum,out safeMaximum);
            if(aimMinimumZmm<safeMinimum||aimMaximumZmm>safeMaximum)throw new ArgumentException("Aim bounds must stay in the continuous safe working water.");
            Version=version;AnchorXmm=anchorXmm;AnchorYmm=anchorYmm;AnchorZmm=anchorZmm;SurfaceYmm=surfaceYmm;
            MinimumXmm=minimumXmm;MaximumXmm=maximumXmm;MinimumZmm=minimumZmm;MaximumZmm=maximumZmm;
            AimMinimumXmm=aimMinimumXmm;AimMaximumXmm=aimMaximumXmm;AimMinimumZmm=aimMinimumZmm;AimMaximumZmm=aimMaximumZmm;
            SafeWorkMinimumXmm=minimumXmm;SafeWorkMaximumXmm=maximumXmm;SafeWorkMinimumZmm=safeMinimum;SafeWorkMaximumZmm=safeMaximum;
            BedKnots=copy.AsReadOnly();
            if(Math.Abs((long)shelfDepthMm)>5000)throw new ArgumentOutOfRangeException("shelfDepthMm");
            if(shelfDepthMm!=0){
                int middle=(minimumXmm+maximumXmm)/2,side=Math.Sign(shelfDepthMm);
                ShelfStartXmm=middle+side*1000;ShelfEndXmm=middle+side*6000;
                if(Math.Min(ShelfStartXmm,ShelfEndXmm)<=minimumXmm||Math.Max(ShelfStartXmm,ShelfEndXmm)>=maximumXmm)
                    throw new ArgumentException("The shelf must lie inside the surveyed rectangle.");
                foreach(var knot in copy)if(knot.DepthMm+Math.Abs(shelfDepthMm)>100000)throw new ArgumentException("Shelf exceeds the survey depth range.");
            }
            ShelfDepthMm=shelfDepthMm;
        }
        int ShelfRun(int xMm)
        {return Math.Min(5000,Math.Max(0,(xMm-ShelfStartXmm)*Math.Sign(ShelfDepthMm)));}
        internal int ExtraDepthMm(int xMm){return (int)((long)ShelfRun(xMm)*Math.Abs(ShelfDepthMm)/5000);}
        internal int SoundingMm(FloatWaterBedKnot a,FloatWaterBedKnot b,int xMm,int zMm)
        {
            long span=b.Zmm-a.Zmm;
            long along=(long)a.DepthMm*(b.Zmm-zMm)+(long)b.DepthMm*(zMm-a.Zmm);
            return (int)((along*5000+(long)ShelfRun(xMm)*Math.Abs(ShelfDepthMm)*span)/(span*5000));
        }
        static void FindSafeInterval(List<FloatWaterBedKnot> knots,out int minimum,out int maximum)
        {
            const int requiredDepth=FloatDepthMm+MinimumBottomClearanceMm;
            bool found=false;minimum=0;maximum=0;
            for(int index=1;index<knots.Count;index++){
                var a=knots[index-1];var b=knots[index];
                if(a.DepthMm<requiredDepth&&b.DepthMm<requiredDepth)continue;
                int start=a.Zmm,end=b.Zmm;long width=(long)b.Zmm-a.Zmm;
                if(a.DepthMm<requiredDepth){
                    long numerator=(long)(requiredDepth-a.DepthMm)*width,denominator=b.DepthMm-a.DepthMm;
                    start=checked(a.Zmm+(int)((numerator+denominator-1)/denominator));
                }else if(b.DepthMm<requiredDepth){
                    long numerator=(long)(a.DepthMm-requiredDepth)*width,denominator=a.DepthMm-b.DepthMm;
                    end=checked(a.Zmm+(int)(numerator/denominator));
                }
                if(!found){minimum=start;maximum=end;found=true;}
                else{
                    // Do not join intervals merely one integer millimetre
                    // apart: the physical path between them may be unsafe.
                    if(start>maximum)throw new ArgumentException("This slice requires one continuous safe working rectangle.");
                    maximum=Math.Max(maximum,end);
                }
            }
            if(!found||minimum>=maximum)throw new ArgumentException("No positive safe working rectangle is configured.");
        }
        internal static FloatStillWaterGeometry CreateDevelopment(bool surveyedBank=false)
        {
            return new FloatStillWaterGeometry("development-still-water","bank-01",surveyedBank?4:1,0,1500,0,0,-40000,40000,3000,80000,
                -12000,12000,12000,40000,new[]{new FloatWaterBedKnot(3000,600),new FloatWaterBedKnot(12000,1200),
                    new FloatWaterBedKnot(30000,2400),new FloatWaterBedKnot(80000,4000)},surveyedBank?450:0);
        }
        internal FloatWaterSampleResult ResolveAim(int u,int v,long expectedVersion)
        {
            if(expectedVersion!=Version)return new FloatWaterSampleResult("geometry-version-mismatch");
            if(u<0||u>10000||v<0||v>10000)return new FloatWaterSampleResult("aim-out-of-range");
            // Integer grid interpolation is explicit. U increases to the
            // right; V increases from the far bank to the near bank.
            int x=checked(AimMinimumXmm+(int)(((long)AimMaximumXmm-AimMinimumXmm)*u/10000));
            int z=checked(AimMaximumZmm-(int)(((long)AimMaximumZmm-AimMinimumZmm)*v/10000));
            var result=Sample(x,z,expectedVersion);
            return result.Accepted?new FloatWaterSampleResult("aim-resolved",result.Sample):result;
        }
        internal FloatWaterSampleResult Sample(int xMm,int zMm,long expectedVersion)
        {
            if(expectedVersion!=Version)return new FloatWaterSampleResult("geometry-version-mismatch");
            if(xMm<MinimumXmm||xMm>MaximumXmm||zMm<MinimumZmm||zMm>MaximumZmm)return new FloatWaterSampleResult("outside-water");
            for(int index=1;index<BedKnots.Count;index++){
                var a=BedKnots[index-1];var b=BedKnots[index];if(zMm>b.Zmm)continue;
                // Weighted nonnegative integers floor the true depth even on
                // descending segments, never overestimating bottom clearance.
                int depth=SoundingMm(a,b,xMm,zMm);
                if(depth<FloatDepthMm+MinimumBottomClearanceMm)return new FloatWaterSampleResult("float-clearance-insufficient");
                return new FloatWaterSampleResult("water-sampled",new FloatWaterSample(this,xMm,zMm,depth));
            }
            return new FloatWaterSampleResult("bed-unavailable");
        }
    }
}
