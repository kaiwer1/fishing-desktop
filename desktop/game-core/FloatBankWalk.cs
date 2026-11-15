using System;

namespace FishingDesktop.GameCore
{
    // Position is an ephemeral station preference. The water, aim sector and
    // bed remain in world coordinates when the angler moves along the bank.
    internal sealed class FloatBankWalk
    {
        internal const int HalfWidthMm=8000,WalkSpeedMmPerSecond=1600,RunSpeedMmPerSecond=3400;
        internal readonly FloatStillWaterGeometry BaseGeometry;
        internal readonly int MinimumXmm,MaximumXmm;
        internal FloatStillWaterGeometry Geometry {get;private set;}
        internal int Direction {get;private set;}
        internal bool Fast {get;private set;}
        internal int PoseRevision {get;private set;}
        int subMillimetres;

        internal FloatBankWalk(FloatStillWaterGeometry geometry,int initialPoseRevision=0)
        {
            if(geometry==null)throw new ArgumentNullException("geometry");
            if(initialPoseRevision<0)throw new ArgumentOutOfRangeException("initialPoseRevision");
            BaseGeometry=Geometry=geometry;
            PoseRevision=initialPoseRevision;
            MinimumXmm=Math.Max(geometry.MinimumXmm,geometry.AnchorXmm-HalfWidthMm);
            MaximumXmm=Math.Min(geometry.MaximumXmm,geometry.AnchorXmm+HalfWidthMm);
        }
        internal int Xmm {get{return Geometry.AnchorXmm;}}
        internal int NearestAimU {
            get{
                long width=BaseGeometry.AimMaximumXmm-BaseGeometry.AimMinimumXmm;
                long offset=Math.Max(0L,Math.Min(width,(long)Xmm-BaseGeometry.AimMinimumXmm));
                return (int)((offset*10000+width/2)/width);
            }
        }
        internal void SetMotion(int direction,bool fast)
        {
            if(direction< -1 || direction>1 || (direction==0&&fast))throw new ArgumentException("Invalid bank direction or speed.");
            if(direction!=Direction)subMillimetres=0;
            Direction=direction;Fast=fast;
        }
        internal bool Advance(int activeMilliseconds)
        {
            if(activeMilliseconds<0 || activeMilliseconds>250)throw new ArgumentOutOfRangeException("activeMilliseconds");
            if(Direction==0 || activeMilliseconds==0)return false;
            int units=(Fast?RunSpeedMmPerSecond:WalkSpeedMmPerSecond)*activeMilliseconds+subMillimetres;
            int next=Math.Max(MinimumXmm,Math.Min(MaximumXmm,Xmm+Direction*(units/1000)));
            subMillimetres=next==MinimumXmm || next==MaximumXmm?0:units%1000;
            if(next==Xmm)return false;
            var g=BaseGeometry;
            Geometry=next==g.AnchorXmm?g:new FloatStillWaterGeometry(g.WaterId,g.SpotId,g.Version,next,g.AnchorYmm,g.AnchorZmm,g.SurfaceYmm,
                g.MinimumXmm,g.MaximumXmm,g.MinimumZmm,g.MaximumZmm,g.AimMinimumXmm,g.AimMaximumXmm,g.AimMinimumZmm,g.AimMaximumZmm,g.BedKnots,g.ShelfDepthMm);
            PoseRevision=checked(PoseRevision+1);return true;
        }
    }
}
