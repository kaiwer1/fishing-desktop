using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    internal sealed class FloatAnglerMethodLog
    {
        internal readonly string Id;
        internal readonly int Casts,Caught;
        internal readonly long Xp;
        internal FloatAnglerMethodLog(string id,int casts,int caught,long xp)
        {Id=id;Casts=casts;Caught=caught;Xp=xp;}
    }
    internal sealed class FloatAnglerSpeciesLog
    {
        internal readonly string Id;
        internal readonly int Caught;
        internal readonly long MaxMassMg;
        internal FloatAnglerSpeciesLog(string id,int caught,long maximum)
        {Id=id;Caught=caught;MaxMassMg=maximum;}
    }
    internal sealed class FloatAnglerLog
    {
        internal const int RulesVersion=1;
        internal readonly long Sequence,SaleMinor,TotalXp;
        internal readonly int TotalCasts,TotalCaught,TotalSold;
        internal readonly ReadOnlyCollection<FloatAnglerMethodLog> Methods;
        internal readonly ReadOnlyCollection<FloatAnglerSpeciesLog> Species;
        static readonly string[] methodIds={"float","bottom","lure"};
        FloatAnglerLog(long sequence,long sale,int sold,int[] casts,int[] caught,long[] xp,
            SortedDictionary<string,FloatAnglerSpeciesLog> species)
        {
            Sequence=sequence;SaleMinor=sale;TotalSold=sold;
            var methods=new FloatAnglerMethodLog[methodIds.Length];
            checked{
                for(int i=0;i<methods.Length;i++){
                    methods[i]=new FloatAnglerMethodLog(methodIds[i],casts[i],caught[i],xp[i]);
                    TotalCasts+=casts[i];TotalCaught+=caught[i];TotalXp+=xp[i];
                }
            }
            Methods=Array.AsReadOnly(methods);Species=new List<FloatAnglerSpeciesLog>(species.Values).AsReadOnly();
        }
        // The original browser game's 10 + round(6 * kilograms), evaluated
        // with nonnegative integer arithmetic so milligram ties round up.
        internal static long CatchXp(long massMg)
        {
            if(massMg<=0)throw new ArgumentOutOfRangeException("massMg");
            return checked(10+(massMg/1000000)*6+((massMg%1000000)*6+500000)/1000000);
        }
        internal static FloatAnglerLog Build(FloatGameState state)
        {
            if(state==null)throw new ArgumentNullException("state");
            var casts=new int[3];var caught=new int[3];var xp=new long[3];
            var active=new Dictionary<Guid,int>();
            var species=new SortedDictionary<string,FloatAnglerSpeciesLog>(StringComparer.Ordinal);
            long sale=0;int sold=0;
            checked{
                foreach(var receipt in state.Receipts){
                    if(receipt.Outcome==FloatOutcome.CastCommitted){
                        int method=Array.IndexOf(methodIds,receipt.CastMethod);
                        if(method<0)throw new InvalidOperationException("Unknown confirmed fishing method.");
                        active.Add(receipt.SessionId,method);casts[method]++;
                    }else if(receipt.Outcome==FloatOutcome.Landed){
                        int method;
                        if(!active.TryGetValue(receipt.SessionId,out method))
                            throw new InvalidOperationException("Confirmed landing has no matching cast.");
                        caught[method]++;xp[method]+=CatchXp(receipt.LandedMassMg);
                        FloatAnglerSpeciesLog prior;species.TryGetValue(receipt.LandedSpecies,out prior);
                        species[receipt.LandedSpecies]=new FloatAnglerSpeciesLog(receipt.LandedSpecies,prior==null?1:prior.Caught+1,
                            Math.Max(receipt.LandedMassMg,prior==null?0:prior.MaxMassMg));
                    }else if(receipt.Outcome==FloatOutcome.Sold){
                        sold+=receipt.FishIds.Count;sale+=receipt.WalletAfter-receipt.WalletBefore;
                    }
                    if(receipt.Terminal)active.Remove(receipt.SessionId);
                }
            }
            return new FloatAnglerLog(state.Sequence,sale,sold,casts,caught,xp,species);
        }
    }
}
