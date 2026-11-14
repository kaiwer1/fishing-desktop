using System;
using System.Collections.Generic;

namespace FishingDesktop.GameCore
{
    internal enum BottomPartKind { Rod, Reel, SpoolLine, Leader, Hook, Sinker, HookBait, Feeder }

    internal sealed class BottomRigPart
    {
        internal readonly Guid Id;
        internal readonly BottomPartKind Kind;
        internal readonly string DefinitionId;
        internal BottomRigPart(Guid id,BottomPartKind kind,string definition)
        {
            if(id==Guid.Empty || String.IsNullOrEmpty(definition))throw new ArgumentException("A part needs an instance and definition.");
            Id=id;Kind=kind;DefinitionId=definition;
        }
    }

    // This is a read-only assembly projection. An owner proves ownership and
    // inventory locations before admission. No parts are issued by this class.
    internal sealed class BottomSimpleAssembly
    {
        internal const long RulesVersion=1;
        internal const int MaximumLineMm=100000,LeaderLengthMm=400,LineDiameterUm=220,LeaderDiameterUm=160;
        internal const int SinkerMassMg=18000,HookMassMg=200,BaitMassMg=700;
        internal const int CastMassMg=SinkerMassMg+HookMassMg+BaitMassMg;
        internal const int RodMinimumCastMg=10000,RodMaximumCastMg=35000;
        internal readonly Guid RigId;
        internal readonly long RigRevision;
        internal readonly int LineRemainingMm;
        readonly BottomRigPart[] parts;
        readonly string mode;
        // Slots are deliberately explicit. Only these seven component
        // definitions and their one v1 mounting combination are configured.
        static readonly string[] definitions={"bottom-bank-rod","bottom-bank-reel","bottom-022-line","bottom-016-leader",
            "bottom-single-hook","bottom-18g-sinker","worm"};
        internal BottomSimpleAssembly(Guid rigId,long rigRevision,string mode,int lineRemainingMm,
            BottomRigPart rod,BottomRigPart reel,BottomRigPart line,BottomRigPart leader,
            BottomRigPart hook,BottomRigPart sinker,BottomRigPart bait)
        {
            if(rigId==Guid.Empty||rigRevision<1||lineRemainingMm<0||lineRemainingMm>MaximumLineMm)throw new ArgumentException("Invalid rig identity or line length.");
            RigId=rigId;RigRevision=rigRevision;this.mode=mode;LineRemainingMm=lineRemainingMm;
            parts=new[]{rod,reel,line,leader,hook,sinker,bait};
        }
        internal string ValidateAssembly()
        {
            if(mode!="bottom_simple")return "bottom-unsupported";
            var seen=new HashSet<Guid>{RigId};
            for(int i=0;i<parts.Length;i++){
                var p=parts[i];if(p==null)continue;
                if(!seen.Add(p.Id))return "part-identity-conflict";
                if(p.Kind!=(BottomPartKind)i || p.DefinitionId!=definitions[i])return "rig-incompatible";
            }
            return null;
        }
        internal string ValidateComplete()
        {
            string invalid=ValidateAssembly();if(invalid!=null)return invalid;
            if(Array.Exists(parts,p=>p==null))return "rig-incomplete";
            return null;
        }
        internal string EvaluateCastReadiness(int baitCount,int stamina,bool held)
        {
            string invalid=ValidateComplete();if(invalid!=null)return invalid;
            if(!held)return "not-held";
            if(baitCount<1)return "hookbait-empty";
            if(stamina<2)return "stamina-insufficient";
            if(LineRemainingMm<1)return "line-insufficient";
            return null;
        }
        internal Guid ItemId(BottomPartKind kind){int i=(int)kind;return i>=0&&i<parts.Length&&parts[i]!=null?parts[i].Id:Guid.Empty;}
    }

    internal enum BottomSubstrate { Unknown, FirmGravel, SoftMud, Weeds }
    internal sealed class BottomContact
    {
        internal const int SlackMm=180;
        internal readonly int Xmm,Zmm,SinkerYmm,BaitZmm,BaitYmm,RequiredLineMm;
        internal BottomContact(int x,int z,int sinkerY,int baitZ,int baitY,int line)
        {Xmm=x;Zmm=z;SinkerYmm=sinkerY;BaitZmm=baitZ;BaitYmm=baitY;RequiredLineMm=line;}
        internal static int LineTo(FloatStillWaterGeometry geometry,int x,int y,int z)
        {
            double dx=(long)x-geometry.AnchorXmm,dy=(long)y-geometry.AnchorYmm,dz=(long)z-geometry.AnchorZmm;
            return checked((int)Math.Ceiling(Math.Sqrt(dx*dx+dy*dy+dz*dz))+SlackMm);
        }
    }
    internal sealed class BottomSurveyPatch
    {
        internal readonly FloatStillWaterGeometry Geometry;
        readonly int minX,maxX,minZ,maxZ;
        internal int MinimumXmm{get{return minX;}}
        internal int MaximumXmm{get{return maxX;}}
        internal int MinimumZmm{get{return minZ;}}
        internal int MaximumZmm{get{return maxZ;}}
        readonly BottomSubstrate substrate;
        readonly bool unobstructed;
        // The optional area metadata stays separate from the water atlas.
        // Naming a lake or supplying a depth does not prove a firm bed.
        internal BottomSurveyPatch(FloatStillWaterGeometry geometry,int minX,int maxX,int minZ,int maxZ,
            BottomSubstrate substrate,bool unobstructed)
        {
            if(geometry==null)throw new ArgumentNullException("geometry");
            if(minX>=maxX||minZ>=maxZ||minX<geometry.MinimumXmm||maxX>geometry.MaximumXmm||
                minZ<geometry.MinimumZmm||maxZ>geometry.MaximumZmm)throw new ArgumentException("Invalid surveyed patch.");
            Geometry=geometry;this.minX=minX;this.maxX=maxX;this.minZ=minZ;this.maxZ=maxZ;
            this.substrate=substrate;this.unobstructed=unobstructed;
        }
        internal string Resolve(long version,int x,int z,int availableLineMm,out BottomContact contact)
        {
            contact=null;
            if(version!=Geometry.Version)return "geometry-version-mismatch";
            if(substrate!=BottomSubstrate.FirmGravel||!unobstructed)return "bottom-unsupported";
            // The 400 mm leader trails 240 mm towards the bank. The residual
            // length can rest on the bed after both endpoints have settled.
            const int trailMm=240;
            if(x<minX||x>maxX||z>maxZ||z<(long)minZ+trailMm)return "outside-bottom-patch";
            int baitZ=z-trailMm;
            int? sinkerY=null,baitY=null;
            var knots=Geometry.BedKnots;
            for(int i=1;i<knots.Count;i++){
                var a=knots[i-1];var b=knots[i];
                if(a.Zmm<=z&&z<=b.Zmm)sinkerY=BedY(a,b,z);
                if(a.Zmm<=baitZ&&baitZ<=b.Zmm)baitY=BedY(a,b,baitZ);
                if(a.Zmm<z&&b.Zmm>baitZ&&Math.Abs((long)b.DepthMm-a.DepthMm)*100>(long)(b.Zmm-a.Zmm)*12)
                    return "bed-too-steep";
            }
            if(!sinkerY.HasValue||!baitY.HasValue)return "bed-unavailable";
            int line=BottomContact.LineTo(Geometry,x,sinkerY.Value,z);
            if(availableLineMm<line)return "line-insufficient";
            contact=new BottomContact(x,z,sinkerY.Value,baitZ,baitY.Value,line);
            return null;
        }
        // The same survey knots and nonnegative depth rounding used by the
        // water atlas, without its float-specific hook clearance requirement.
        int BedY(FloatWaterBedKnot a,FloatWaterBedKnot b,int z)
        {
            long depth=((long)a.DepthMm*(b.Zmm-z)+(long)b.DepthMm*(z-a.Zmm))/(b.Zmm-a.Zmm);
            return checked(Geometry.SurfaceYmm-(int)depth);
        }
    }
}
