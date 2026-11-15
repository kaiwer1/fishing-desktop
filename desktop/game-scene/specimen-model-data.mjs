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
    depth:.354,width:.144,head:.705,eye:.844,eyeSize:.020,noseY:-.036,hump:.035,
    profile:[.26,.25,.39,.66,.9,1,.99,.92,.73,.47,.28,.071],
    colors:[0x39444c,0x7d8583,0xbac0ad],fin:0x565b56,ventral:0x696b5b,iris:0xb5a878,
    scales:51,dorsal:[[.73,.40,.119,11,true],[.40,.16,.072,12,false]],anal:[.40,.19,.085,11,false],tail:.141,fork:.62,
    chest:.691,pelvis:.632,pectoral:.174,bars:true
  },
  'spotted-seabass': {
    depth:.238,width:.135,head:.660,eye:.824,eyeSize:.017,noseY:-.004,hump:.013,
    profile:[.35,.31,.42,.62,.82,.94,1,.95,.83,.62,.39,.055],
    colors:[0x475e59,0x9ca9a0,0xe1ded1],fin:0x707d73,ventral:0x899082,iris:0xb3b29a,
    scales:66,dorsal:[[.66,.426,.105,12,true],[.408,.166,.080,14,false]],anal:[.38,.19,.080,10,false],tail:.119,fork:.32,
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
