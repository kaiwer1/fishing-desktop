using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FishingDesktop.GameCore
{
    internal sealed class FloatFishingSpecies
    {
        internal readonly string Id,Name,AssetId;
        internal readonly FloatSpeciesRule Rule;
        internal readonly long EncounterMinimumMg,EncounterMaximumMg;

        internal FloatFishingSpecies(string id,string name,string assetId,long minimumMg,long maximumMg,long minorPerKg,long encounterMinimumMg,long encounterMaximumMg)
        {
            if(encounterMinimumMg<minimumMg || encounterMaximumMg>maximumMg || encounterMaximumMg<encounterMinimumMg)
                throw new ArgumentOutOfRangeException("encounterMinimumMg");
            Id=id;Name=name;AssetId=assetId;
            Rule=new FloatSpeciesRule(id,minimumMg,maximumMg,minorPerKg);
            EncounterMinimumMg=encounterMinimumMg;EncounterMaximumMg=encounterMaximumMg;
        }
    }

    internal sealed class FloatFishingPool
    {
        internal readonly string Id,Name;
        internal readonly FloatBiteEnvironment Environment;
        internal FloatFishingPool(string id,string name,FloatBiteEnvironment environment)
        {Id=id;Name=name;Environment=environment;}
    }

    // Relative weights and depth bands are original game settings for one
    // shoreline station, not real population surveys or sale prices.
    internal sealed class FloatFishingWater
    {
        internal sealed class FishWeight
        {
            internal readonly FloatFishingSpecies Species;
            internal readonly int Shallow,Deep;
            internal FishWeight(FloatFishingSpecies species,int shallow,int deep)
            {Species=species;Shallow=shallow;Deep=deep;}
        }

        internal readonly string Id,Name,Kind,Description,ShallowPoolName,DeepPoolName;
        internal readonly FloatStillWaterGeometry Geometry;
        internal readonly int DeepThresholdMm;
        readonly ReadOnlyCollection<FishWeight> weights;

        internal FloatFishingWater(string name,string kind,string description,FloatStillWaterGeometry geometry,
            int thresholdMm,string shallowName,string deepName,params FishWeight[] species)
        {
            Id=geometry.WaterId;Name=name;Kind=kind;Description=description;Geometry=geometry;
            DeepThresholdMm=thresholdMm;ShallowPoolName=shallowName;DeepPoolName=deepName;
            weights=Array.AsReadOnly((FishWeight[])species.Clone());
        }

        static bool HasEncounterRange(FloatGameRules rules,FloatFishingSpecies species)
        {
            if(rules==null || (rules.Version!=1 && rules.Version!=FloatFishingAtlas.NewRulesVersion))return false;
            var rule=rules.Find(species.Id);
            return rule!=null && rule.MaximumMassMg>=species.EncounterMinimumMg && rule.MinimumMassMg<=species.EncounterMaximumMg;
        }

        internal ReadOnlyCollection<FloatFishingSpecies> SpeciesFor(FloatGameRules rules)
        {
            var result=new List<FloatFishingSpecies>();
            foreach(var row in weights)if(HasEncounterRange(rules,row.Species))result.Add(row.Species);
            return result.AsReadOnly();
        }

        // World identity, coordinates, bed and casting depth must agree with
        // the selected immutable station before generating any encounter.
        internal FloatFishingPool CreatePool(FloatWaterSample landing,FloatGameRules rules)
        {
            if(landing==null || landing.WaterId!=Id || landing.SpotId!=Geometry.SpotId ||
                landing.GeometryVersion!=Geometry.Version)return null;
            var sample=Geometry.Sample(landing.Xmm,landing.Zmm,landing.GeometryVersion);
            if(!sample.Accepted || sample.Sample.DepthMm!=landing.DepthMm || sample.Sample.SurfaceYmm!=landing.SurfaceYmm ||
                sample.Sample.HookYmm!=landing.HookYmm || sample.Sample.BottomClearanceMm!=landing.BottomClearanceMm)return null;
            bool deep=landing.DepthMm>=DeepThresholdMm;
            var pool=new List<FloatBiteCandidate>();
            foreach(var row in weights){
                var species=row.Species;
                if(!HasEncounterRange(rules,species))continue;
                var saved=rules.Find(species.Id);
                pool.Add(new FloatBiteCandidate(species.Id,deep?row.Deep:row.Shallow,
                    Math.Max(saved.MinimumMassMg,species.EncounterMinimumMg),Math.Min(saved.MaximumMassMg,species.EncounterMaximumMg)));
            }
            if(pool.Count==0)return null;
            return new FloatFishingPool(deep?"deep":"shallow",deep?DeepPoolName:ShallowPoolName,
                new FloatBiteEnvironment(.18,pool.ToArray()));
        }
    }

    internal static class FloatFishingAtlas
    {
        internal const long NewRulesVersion=2;
        static readonly FloatFishingSpecies Carp=new FloatFishingSpecies("carp","鲤鱼","fw_carp",100000,10000000,120,300000,1800000);
        static readonly FloatFishingSpecies Crucian=new FloatFishingSpecies("crucian","鲫鱼","fw_crucian",50000,3000000,80,100000,650000);
        static readonly FloatFishingSpecies Grass=new FloatFishingSpecies("grass-carp","草鱼","fw_grass_carp",200000,12000000,135,550000,2400000);
        static readonly FloatFishingSpecies Bream=new FloatFishingSpecies("blunt-snout-bream","鳊鱼","fw_blunt_snout_bream",100000,3500000,105,220000,1050000);
        static readonly FloatFishingSpecies Black=new FloatFishingSpecies("black-seabream","黑鲷","sw_black_seabream",100000,6000000,175,330000,1900000);
        static readonly FloatFishingSpecies Bass=new FloatFishingSpecies("spotted-seabass","花鲈","sw_spotted_seabass",200000,10000000,165,480000,2600000);
        static readonly FloatFishingSpecies Yellow=new FloatFishingSpecies("yellowfin-seabream","黄鳍鲷","sw_yellowfin_seabream",80000,4300000,155,180000,1300000);
        internal static readonly ReadOnlyCollection<FloatSpeciesRule> SpeciesRules=Array.AsReadOnly(new[]{
            Carp.Rule,Crucian.Rule,Grass.Rule,Bream.Rule,Black.Rule,Bass.Rule,Yellow.Rule});
        internal static readonly ReadOnlyCollection<FloatFishingWater> Waters=Array.AsReadOnly(new[]{
            new FloatFishingWater("静水湾","freshwater","近岸芦苇间可寻鲫鱼，塘心深水更常遇到鲤鱼。",
                FloatStillWaterGeometry.CreateDevelopment(),1800,"芦苇浅滩","塘心深窝",
                new FloatFishingWater.FishWeight(Carp,3,8),new FloatFishingWater.FishWeight(Crucian,7,2)),
            new FloatFishingWater("江湾","freshwater","浅水草滩聚着草鱼和鳊鱼，回湾深槽里鲤鱼较多。",
                new FloatStillWaterGeometry("river-bend","reed-bank",2,0,1500,0,0,-46000,46000,3000,80000,
                    -13000,13000,12000,41000,new[]{new FloatWaterBedKnot(3000,520),new FloatWaterBedKnot(12000,950),
                        new FloatWaterBedKnot(28000,2750),new FloatWaterBedKnot(80000,4650)}),2100,"湾口草滩","回湾深槽",
                new FloatFishingWater.FishWeight(Grass,5,2),new FloatFishingWater.FishWeight(Bream,4,3),new FloatFishingWater.FishWeight(Carp,1,5)),
            new FloatFishingWater("海湾","marine","黄鳍鲷常在近岸沙槽活动，外湾礁坡更容易遇到黑鲷和花鲈。",
                new FloatStillWaterGeometry("coastal-bay","rock-bank",3,0,1600,0,0,-48000,48000,3000,85000,
                    -14000,14000,13000,42000,new[]{new FloatWaterBedKnot(3000,750),new FloatWaterBedKnot(13000,1500),
                        new FloatWaterBedKnot(30000,3500),new FloatWaterBedKnot(85000,6400)}),2700,"近岸沙槽","外湾礁坡",
                new FloatFishingWater.FishWeight(Black,3,5),new FloatFishingWater.FishWeight(Bass,1,4),new FloatFishingWater.FishWeight(Yellow,6,1))
        });

        internal static FloatFishingWater Find(string waterId)
        {
            foreach(var water in Waters)if(water.Id==waterId)return water;
            return null;
        }

        internal static ReadOnlyCollection<FloatFishingWater> Available(FloatGameRules rules)
        {
            var result=new List<FloatFishingWater>();
            foreach(var water in Waters)if(water.SpeciesFor(rules).Count!=0)result.Add(water);
            return result.AsReadOnly();
        }
    }
}
