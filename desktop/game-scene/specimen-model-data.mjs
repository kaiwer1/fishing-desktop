// Authored contours and colours. Anatomical references only, no source image is used.
// FAO carp and grass carp: https://www.fao.org/fishery/docs/CDrom/aquaculture/I1129m/file/en/en_commoncarp.htm
// https://www.fao.org/fi/static-media/FI/CDrom/aquaculture/I1129m/file/en/en_grasscarp.htm
// Taiwan Fish Database: https://fishdb.sinica.edu.tw/eng/species.php?id=381001 (Carassius)
// https://fishdb.sinica.edu.tw/eng/species.php?id=381015 (Megalobrama)
// https://fishdb.sinica.edu.tw/taxon/382635-fishdb and https://fishdb.sinica.edu.tw/taxon/382634-fishdb
// Spotted sea bass: https://pmc.ncbi.nlm.nih.gov/articles/PMC6616080/
//
// Sections run from caudal peduncle to nose. Each ordinate is relative to
// maximum body depth. Dorsal/anal records are [front, back, height, rays, spiny].
// Distances outside the contour ordinates are in model metres.
export const specimens = {
  'topmouth-culter': {
    depth:.213,width:.111,head:.753,eye:.872,eyeSize:.020,noseY:.052,hump:.008,
    profile:[.27,.29,.42,.60,.78,.93,1,.95,.77,.55,.30,.115],
    colors:[0x354e53,0xb5c0c2,0xe5e8df],fin:0x50636b,pectoralColor:0x898f81,ventral:0x849397,iris:0xbcc4a5,
    scales:66,dorsal:[[.487,.350,.100,9,false]],anal:[.444,.182,.070,23,false],tail:.144,fork:.64,
    chest:.745,pelvis:.514,pectoral:.152,upturned:true
  },
  'large-yellow-croaker': {
    depth:.255,width:.139,head:.722,eye:.865,eyeSize:.019,noseY:-.014,hump:.014,
    profile:[.26,.28,.40,.59,.79,.94,1,.99,.87,.71,.45,.12],
    colors:[0x58646a,0xb9b198,0xe6b74e],fin:0x8b8460,pectoralColor:0xba9c5e,ventral:0xc9a23c,iris:0xae9877,
    scales:55,dorsal:[[.710,.463,.097,9,true],[.463,.106,.071,32,false]],anal:[.323,.195,.070,9,false],tail:.103,fork:0,
    chest:.706,pelvis:.655,pectoral:.139,croaker:true
  },
  'yellow-catfish': {
    depth:.239,width:.194,head:.694,eye:.891,eyeSize:.018,noseY:-.021,hump:.014,
    profile:[.34,.35,.47,.62,.76,.91,1,.99,.85,.66,.42,.11],
    colors:[0x48472a,0xab9740,0xe1d28c],fin:0x736849,ventral:0x91845e,iris:0xbfb998,
    scales:0,dorsal:[[.671,.519,.125,7,false]],anal:[.402,.131,.077,21,false],tail:.144,fork:.88,
    chest:.692,pelvis:.468,pectoral:.158,catfish:true,adipose:true
  },
  'black-rockfish': {
    depth:.304,width:.170,head:.645,eye:.869,eyeSize:.024,noseY:.013,hump:.019,
    profile:[.37,.39,.49,.66,.85,.96,1,.98,.85,.70,.42,.120],
    colors:[0x30363a,0x535856,0x878774],fin:0x484c4a,ventral:0x656558,iris:0x918768,
    scales:63,dorsal:[[.694,.377,.133,13,true],[.377,.122,.085,13,false]],anal:[.348,.136,.079,9,false],tail:.112,fork:.06,
    chest:.652,pelvis:.618,pectoral:.188,rockfish:true
  },
  carp: {
    depth:.318, width:.155, head:.738, eye:.880, eyeSize:.0165, noseY:-.038, hump:.029,
    profile:[.28,.28,.40,.61,.88,1,.96,.83,.64,.47,.26,.065],
    colors:[0x4f5340,0xa08c60,0xddc496], fin:0x916f53, ventral:0xa67653, iris:0xb18e53,
    scales:35, dorsal:[[.66,.21,.081,21,false]], anal:[.32,.22,.063,7,false], tail:.135, fork:.70,
    chest:.72, pelvis:.47, pectoral:.140, whiskers:true
  },
  crucian: {
    depth:.351,width:.137,head:.765,eye:.889,eyeSize:.0175,noseY:-.024,hump:.033,
    profile:[.24,.25,.40,.65,.92,1,.99,.91,.72,.46,.25,.055],
    colors:[0x535a48,0xa19f84,0xdaceaf],fin:0x787665,ventral:0x897c63,iris:0xc1ab71,
    scales:32,dorsal:[[.66,.25,.079,18,false]],anal:[.32,.22,.057,6,false],tail:.126,fork:.47,
    chest:.74,pelvis:.48,pectoral:.129
  },
  'grass-carp': {
    depth:.242,width:.151,head:.741,eye:.882,eyeSize:.014,noseY:-.008,hump:.008,
    profile:[.32,.31,.46,.68,.84,.94,.99,1,.88,.70,.44,.088],
    colors:[0x46503e,0x89916c,0xd1cbb1],fin:0x707667,ventral:0x82836a,iris:0xbab87b,
    scales:43,dorsal:[[.544,.375,.082,9,false]],anal:[.31,.21,.060,9,false],tail:.129,fork:.79,
    chest:.725,pelvis:.468,pectoral:.132
  },
  'blunt-snout-bream': {
    depth:.386,width:.105,head:.777,eye:.896,eyeSize:.0175,noseY:-.040,hump:.033,
    profile:[.21,.22,.37,.60,.86,.99,1,.89,.71,.47,.27,.081],
    colors:[0x506467,0x9fa9a2,0xdcdaca],fin:0x6d7772,ventral:0x84908a,iris:0xb7b599,
    scales:54,dorsal:[[.59,.455,.133,9,false]],anal:[.51,.19,.078,29,false],tail:.154,fork:.86,
    chest:.753,pelvis:.523,pectoral:.167
  },
  'black-seabream': {
    depth:.344,width:.143,head:.707,eye:.852,eyeSize:.0215,noseY:-.033,hump:.029,
    profile:[.23,.24,.39,.70,.92,1,1,.94,.79,.57,.34,.105],
    colors:[0x3a4550,0xa2a6a8,0xe4e3d8],fin:0x56606b,ventral:0x737a80,iris:0xada387,
    scales:48,dorsal:[[.745,.407,.138,11,true],[.407,.15,.069,12,false]],anal:[.39,.17,.072,11,false],tail:.133,fork:.51,
    chest:.695,pelvis:.637,pectoral:.200,bars:true
  },
  'spotted-seabass': {
    depth:.230,width:.134,head:.678,eye:.834,eyeSize:.0185,noseY:.004,hump:.009,
    profile:[.32,.33,.48,.66,.84,.95,1,.95,.83,.66,.43,.13],
    colors:[0x4c615e,0xafb6ac,0xe5e4da],fin:0x758079,ventral:0x92998e,iris:0xb3ab81,
    scales:67,dorsal:[[.67,.445,.115,12,true],[.392,.142,.089,14,false]],anal:[.39,.18,.068,10,false],tail:.118,fork:.24,
    chest:.681,pelvis:.615,pectoral:.126,spots:true
  },
  'yellowfin-seabream': {
    depth:.331,width:.137,head:.719,eye:.853,eyeSize:.019,noseY:-.035,hump:.031,
    profile:[.27,.26,.41,.68,.91,1,.97,.88,.72,.51,.31,.065],
    colors:[0x52594b,0xa3a48a,0xd1cfb5],fin:0x85836c,ventral:0xc7a43d,iris:0xc1ae6e,
    scales:47,dorsal:[[.727,.396,.110,11,true],[.396,.168,.072,11,false]],anal:[.40,.19,.076,10,false],tail:.142,fork:.71,
    chest:.695,pelvis:.64,pectoral:.175,yellow:true
  }
};
