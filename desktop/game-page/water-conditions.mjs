const waters=new Set(['development-still-water','river-bend','coastal-bay']);
const names={clear:'晴',cloudy:'多云',overcast:'阴',mist:'薄雾',rain:'雨'};
const between=(v,min,max)=>Number.isInteger(v) && v>=min && v<=max;

export class WaterConditions {
  #doc;#revision=0;#state=null;#shown=null;
  constructor(documentId){this.#doc=documentId;}
  get revision(){return this.#revision;}
  apply(message){
    if(message?.kind!=='game-conditions-state'||message.documentId!==this.#doc||
        !Number.isSafeInteger(message.revision)||message.revision<=this.#revision)return false;
    const s=message.state;
    if(!s || !waters.has(s.waterId) || !Object.hasOwn(names,s.weatherCode) || !between(s.clockMinute,0,1439) ||
        !between(s.cloudPercent,0,100) || !between(s.lightPermille,0,1000) || !between(s.rainPermille,0,1000) ||
        !between(s.windDeciMs,0,1500) || !between(s.tempDeciC,-1000,1000))return false;
    if(!this.#state || Object.keys(this.#state).some(k=>this.#state[k]!==s[k])){
      this.#state=Object.freeze({waterId:s.waterId,clockMinute:s.clockMinute,weatherCode:s.weatherCode,cloudPercent:s.cloudPercent,
        windDeciMs:s.windDeciMs,tempDeciC:s.tempDeciC,lightPermille:s.lightPermille,rainPermille:s.rainPermille});
    }
    this.#revision=message.revision;return true;
  }
  forWater(waterId,paused=false){
    if(!waterId || this.#shown?.waterId!==waterId)this.#shown=null;
    if(this.#state?.waterId===waterId && (!this.#shown || !paused))this.#shown=this.#state;
    return this.#shown;
  }
}

export function showWaterConditions(element,s){
  element.hidden=!s;
  if(!s){
    element.textContent='';
    for(const k of ['clockMinute','waterId','weatherCode'])delete element.dataset[k];
    return;
  }
  const time=[Math.floor(s.clockMinute/60),s.clockMinute%60].map(n=>String(n).padStart(2,'0')).join(':');
  const text=`${time}　${names[s.weatherCode]}　${(s.tempDeciC/10).toFixed(1)} °C　风 ${(s.windDeciMs/10).toFixed(1)} m/s`;
  if(element.textContent!==text)element.textContent=text;
  element.dataset.clockMinute=String(s.clockMinute);element.dataset.waterId=s.waterId;element.dataset.weatherCode=s.weatherCode;
}
