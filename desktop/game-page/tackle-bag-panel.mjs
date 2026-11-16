import {bagDetails,bagFilters,compareBagRows,matchBag} from './tackle-bag.mjs';
import {tackleFigure} from './tackle-illustration.mjs';

export class TackleBagPanel {
  #doc; #rows=null; #catalog=[]; #filter='all'; #query=''; #selected=null; #buttons=new Map(); #detail=null;
  constructor(doc) {
    this.#doc=doc;
    const search=this.#get('bag-search');
    search.addEventListener('input',()=>{this.#query=search.value;this.#refresh();});
    for(const [key,label] of Object.entries(bagFilters)){
      const button=doc.createElement('button');button.type='button';button.textContent=label;
      button.dataset.kind=key;button.setAttribute('aria-pressed',String(key==='all'));
      button.addEventListener('click',()=>{this.#filter=key;this.#refresh();});
      this.#get('bag-types').append(button);
    }
  }
  #get(id){return this.#doc.getElementById(id);}
  #element(tag,cls,text){
    const node=this.#doc.createElement(tag);
    if(cls)node.className=cls;
    if(text!==undefined)node.textContent=text;
    return node;
  }
  render(rows){
    if(this.#rows===rows)return;
    this.#rows=rows;this.#catalog=[...(rows??[])].sort(compareBagRows).map(row=>bagDetails(row,rows));
    const sameName=new Map();
    for(const item of this.#catalog){
      const key=item.title+'·'+item.compactPlace;
      if(!sameName.has(key))sameName.set(key,[]);
      sameName.get(key).push(item);
    }
    for(const items of sameName.values())if(items.length>1){
      items.sort((a,b)=>a.id.localeCompare(b.id)).forEach((item,i)=>{item.compactPlace+=' · '+(i+1)+'/'+items.length;});
    }
    this.#buttons.clear();
    for(const item of this.#catalog){
      const button=this.#element('button','bag-card');button.type='button';button.dataset.bagItem=item.id;
      const figure=tackleFigure(this.#doc,item.art);
      if(figure)button.append(figure);
      button.append(this.#element('strong','bag-item-name',item.title),this.#element('span','bag-item-place',item.compactPlace));
      button.addEventListener('click',()=>this.#select(item));
      this.#buttons.set(item.id,button);
    }
    this.#refresh();
  }
  #refresh(){
    const filtered=this.#catalog.filter(item=>matchBag(item,this.#filter,this.#query));
    if(!filtered.some(i=>i.id===this.#selected))this.#selected=filtered[0]?.id??null;
    for(const button of this.#get('bag-types').children){
      button.setAttribute('aria-pressed',String(button.dataset.kind===this.#filter));
    }
    const active=this.#doc.activeElement?.dataset?.bagItem;
    const nodes=filtered.map(item=>{
      const button=this.#buttons.get(item.id);
      button.setAttribute('aria-pressed',String(item.id===this.#selected));
      return button;
    });
    this.#get('bag-items').replaceChildren(...nodes);
    const empty=this.#get('bag-empty');
    empty.hidden=nodes.length!==0;empty.textContent=this.#rows===null?'等待装备信息':this.#catalog.length?'没有符合条件的装备':'背包里还没有装备';
    this.#get('bag-count').textContent=filtered.length===this.#catalog.length?`${filtered.length} 件`:`${filtered.length} / ${this.#catalog.length} 件`;
    this.#select(filtered.find(i=>i.id===this.#selected));
    if(active&&filtered.some(item=>item.id===active))this.#buttons.get(active).focus({preventScroll:true});
  }
  #select(item){
    const previous=this.#buttons.get(this.#selected);
    if(previous)previous.setAttribute('aria-pressed','false');
    this.#selected=item?.id??null;
    const panel=this.#get('bag-detail');
    panel.hidden=!item;
    if(!item){this.#detail=null;panel.replaceChildren();return;}
    this.#buttons.get(item.id)?.setAttribute('aria-pressed','true');
    if(this.#detail===item)return;
    this.#detail=item;
    const heading=this.#element('h3','',item.title),group=this.#element('p','bag-detail-group',item.group);
    const props=this.#element('dl','bag-facts');
    for(const [label,value] of [['类型',item.kind],['装配位置',item.location],...item.facts])
      props.append(this.#element('dt','',label),this.#element('dd','',value));
    panel.replaceChildren(group,heading,props);
  }
}
