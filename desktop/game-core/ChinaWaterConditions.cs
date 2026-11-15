using System;
using System.Collections.Generic;

namespace FishingDesktop.GameCore
{
    internal sealed class ChinaWaterConditionSnapshot
    {
        internal readonly string WaterId,WeatherKey;
        internal readonly long ActiveMilliseconds,DayIndex;
        internal readonly int MinuteOfDay,LightPermille,CloudPermille,RainPermille,WindTenthsMps,TemperatureTenthsC;
        readonly int sun,twilight;

        internal ChinaWaterConditionSnapshot(string waterId,long elapsed,long day,int minute,int sunlight,int twilight,
            int light,int cloud,int rain,int wind,int temperature)
        {
            WaterId=waterId;ActiveMilliseconds=elapsed;DayIndex=day;MinuteOfDay=minute;
            sun=sunlight;this.twilight=twilight;LightPermille=light;CloudPermille=cloud;RainPermille=rain;
            WindTenthsMps=wind;TemperatureTenthsC=temperature;
            WeatherKey=rain>=150?"rain":cloud>=730?"overcast":cloud>=370?"cloudy":"clear";
        }

        // Original relative activity, 1000 = the atlas baseline. The signed
        // daylight term changes habitat preferences gradually across the day.
        int Rate(bool deep,int daylightBias,int dawnDuskBias,int rainBias,int deepDayBias)
        {
            int signedSun=2*sun-1000;
            return 1000+(daylightBias*signedSun+dawnDuskBias*twilight+rainBias*RainPermille+
                (deep?deepDayBias:-deepDayBias)*signedSun)/1000;
        }
        internal int ActivityPermille(string speciesId,bool deep)
        {
            switch(speciesId){
                case "carp": return WaterId=="development-still-water"||WaterId=="river-bend"?Rate(deep,-90,300,120,100):0;
                case "crucian": return WaterId=="development-still-water"?Rate(deep,140,140,190,35):0;
                case "grass-carp": return WaterId=="river-bend"?Rate(deep,300,45,-190,50):0;
                case "blunt-snout-bream": return WaterId=="river-bend"?Rate(deep,85,235,145,80):0;
                case "black-seabream": return WaterId=="coastal-bay"?Rate(deep,-60,125,95,165):0;
                case "spotted-seabass": return WaterId=="coastal-bay"?Rate(deep,-165,425,180,50):0;
                case "yellowfin-seabream": return WaterId=="coastal-bay"?Rate(deep,200,75,-150,45):0;
                case "yellow-catfish": return WaterId=="river-bend"?Rate(deep,-140,230,90,50):0;
                case "black-rockfish": return WaterId=="coastal-bay"?Rate(deep,-65,160,70,105):0;
                default:return 0;
            }
        }
    }

    // All reads are pure functions of a single owner's cumulative active
    // time. Opening a map, querying a second rod, or pausing does not spend
    // time, advance a random stream, or reset an existing encounter.
    internal sealed class ChinaWaterConditions
    {
        internal const int RulesVersion=1,DefaultDayLengthMilliseconds=1440000,StartMinute=450;
        internal readonly int DayLengthMilliseconds;
        readonly uint seed;
        // Weather repeats after two local days. Each span occupies six game
        // hours; both ends of each transition have zero first derivative.
        static readonly int[] cloudFront={140,425,825,965,710,320,540,235};
        static readonly int[] rainFront={0,0,95,790,170,0,65,0};
        static readonly int[] windFront={90,260,570,830,700,380,310,150};

        internal ChinaWaterConditions(uint worldSeed,int dayLengthMilliseconds=DefaultDayLengthMilliseconds)
        {
            if(dayLengthMilliseconds<1000||dayLengthMilliseconds>86400000)throw new ArgumentOutOfRangeException("dayLengthMilliseconds");
            seed=worldSeed;DayLengthMilliseconds=dayLengthMilliseconds;
        }
        static uint Mix(uint n)
        {
            unchecked{n^=n>>16;n*=0x7feb352dU;n^=n>>15;n*=0x846ca68bU;return n^(n>>16);}
        }
        static double Smooth(double x)
        {x=Math.Max(0,Math.Min(1,x));return x*x*(3-2*x);}
        static int Rounded(double x){return (int)Math.Round(x,MidpointRounding.AwayFromZero);}
        static int Front(int[] series,int index,double fraction)
        {return Rounded(series[index]+(series[(index+1)%series.Length]-series[index])*fraction);}

        internal ChinaWaterConditionSnapshot Sample(FloatFishingWater water,long activeMilliseconds)
        {
            if(water==null||!Object.ReferenceEquals(FloatFishingAtlas.Find(water.Id),water))throw new ArgumentException("Unknown fishing water.","water");
            if(activeMilliseconds<0)throw new ArgumentOutOfRangeException("activeMilliseconds");
            int site=water.Id=="development-still-water"?0:water.Id=="river-bend"?1:2;
            long remainder=activeMilliseconds%DayLengthMilliseconds;
            // Keep exact fractions even for custom day lengths and Long.MaxValue.
            long scaledMinute=remainder*1440+(long)StartMinute*DayLengthMilliseconds;
            long day=activeMilliseconds/DayLengthMilliseconds+scaledMinute/(1440L*DayLengthMilliseconds);
            double localMinute=(scaledMinute%(1440L*DayLengthMilliseconds))/(double)DayLengthMilliseconds;
            double angle=(localMinute/1440-.5)*2*Math.PI, altitude=Math.Cos(angle);
            int sun=Rounded(1000*Smooth((altitude+.13)/1.13));
            int twilight=Rounded(1000*Smooth(1-Math.Abs(altitude)/.46));

            long weatherPeriod=DayLengthMilliseconds*2L;
            uint regionalPhase=Mix(seed^(0x17063001U+(uint)site));
            double frame=((activeMilliseconds%weatherPeriod)*8.0/weatherPeriod+regionalPhase*(8.0/4294967296.0))%8;
            int index=(int)frame;double fraction=Smooth(frame-index);
            int cloud=Front(cloudFront,index,fraction);
            int rain=Rounded(Front(rainFront,index,fraction)*(site==0?.83:site==1?1:.91));
            int wind=(site==0?8:site==1?13:23)+Rounded(Front(windFront,index,fraction)*(site==2?.068:.047));
            int light=85+Rounded(sun*(.915-.00034*cloud));
            int temperature=(site==0?178:site==1?199:218)+Rounded((site==2?22:40)*Math.Cos(angle-Math.PI/6)-.016*rain);
            return new ChinaWaterConditionSnapshot(water.Id,activeMilliseconds,day,(int)localMinute,sun,twilight,
                light,cloud,rain,wind,temperature);
        }

        internal FloatFishingPool CreatePool(FloatFishingWater water,FloatWaterSample landing,FloatGameRules rules,long activeMilliseconds)
        {
            var conditions=Sample(water,activeMilliseconds);
            var basis=water.CreatePool(landing,rules);if(basis==null)return null;
            var result=new List<FloatBiteCandidate>();int total=0;
            foreach(var candidate in basis.Environment.Candidates){
                int activity=conditions.ActivityPermille(candidate.SpeciesId,basis.Id=="deep");
                if(activity==0)return null;
                int weight=checked(candidate.Weight*activity);total=checked(total+weight);
                result.Add(new FloatBiteCandidate(candidate.SpeciesId,weight,candidate.MinimumMassMg,candidate.MaximumMassMg));
            }
            // Hazard and species mix are weighted by the same response. This
            // keeps each species' marginal rate proportional to its activity.
            double rate=basis.Environment.TotalWeight==0?0:basis.Environment.HazardPerSecond*total/(1000.0*basis.Environment.TotalWeight);
            return new FloatFishingPool(basis.Id,basis.Name,new FloatBiteEnvironment(rate,result.ToArray(),basis.Environment.Available));
        }
    }
}
