'use strict';
const $ = (s,el=document) => el.querySelector(s);
const all = (s,el=document) => [...el.querySelectorAll(s)];
const html = x => String(x ?? '').replace(/[&<>"']/g, a => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[a]);
const native = window.chrome?.webview;
const page = $('#page');
const requests = new Map();
const labels = {home:'首页',sources:'订阅',nodes:'节点',connections:'连接',reach:'连通性检测',ip:'IP 纯净度检测',settings:'设置'};
const book = [['YouTube','https://www.youtube.com/'],['ChatGPT','https://chatgpt.com/'],['Netflix','https://www.netflix.com/']];
let route='home', checksOpen=false, working='', snapshot=null, pending=0, polling=null, serial=0, menuId=null, editor=null, toastTimer, order='delay', mode='system', reach=new Map(), ip=null;
function load(key, fallback) { try{return JSON.parse(localStorage.getItem(key)) ?? fallback;} catch{return fallback;} }
function store(key,v) { try{localStorage.setItem(key,JSON.stringify(v));}catch{} }
let language=load('language','zh-CN')==='en'?'en':'zh-CN';
let sourceError='';
function tr(value){
  if(language!=='en'||typeof value!=='string')return value;
  if(english.has(value))return english.get(value);
  for(const [rx,replace] of translationRules){const m=value.match(rx);if(m)return replace(...m);}
  return value;
}
const staticLabels=new WeakMap();
function localize(root){
  const walker=document.createTreeWalker(root,NodeFilter.SHOW_TEXT);
  while(walker.nextNode()){
    const n=walker.currentNode, original=staticLabels.get(n)??n.data, key=original.trim();
    if(n.parentElement.closest('[data-verbatim],[data-verbatim-content]')||!key)continue;
    const localized=tr(key);
    if(localized!==key||staticLabels.has(n)){staticLabels.set(n,original);n.data=original.replace(key,()=>localized);}
  }
  for(const element of [root,...all('[aria-label],[placeholder],[title]',root)]){
    if(element.closest('[data-verbatim],[data-verbatim-label]')===element||element.closest('[data-verbatim]'))continue;
    const labels=element._flowLabels??={};
    for(const a of ['aria-label','placeholder','title']){
      if(!element.hasAttribute(a))continue;
      const original=labels[a]??element.getAttribute(a);
      labels[a]=original;element.setAttribute(a,tr(original));
    }
  }
}
function applyLanguage(){
  document.documentElement.lang=language;document.title=tr('流枢');
  for(const element of all('.side > :not(#nav), body > dialog, #source-menu'))localize(element);
  $('#nav').setAttribute('aria-label',tr('工作台'));
  page.setAttribute('aria-label',tr('页面内容'));
  void nativeCall('language',{value:language}).catch(()=>{});
}
function address(v) {
  if(!String(v).trim())return null;
  try{const u=new URL(/^[a-z]+:\/\//i.test(v.trim())?v.trim():'https://'+v.trim());return /^https?:$/.test(u.protocol)&&!u.username&&!u.password?u.href:null;}catch{return null;}
}
let latencyRun=null, latencyError=null, importReading=false;
let update={checking:false,started:false,offer:null,text:"",promptAfterEditor:false};
let sites=load('sites',[]); sites=Array.isArray(sites)?sites.map(address).filter(Boolean).slice(0,20):[];
const connectionColumns={host:'目标',route:'走向',rule:'规则',network:'协议',up:'上传',down:'下载'};
const savedConnections=load('connection-view',{});
const connectionPrefs={
  filter:['all','direct','routed'].includes(savedConnections.filter)?savedConnections.filter:'all',
  sort:['original','host','up','down'].includes(savedConnections.sort)?savedConnections.sort:'original',
  columns:Array.isArray(savedConnections.columns)?Object.keys(connectionColumns).filter(k=>k==='host'||savedConnections.columns.includes(k)):['host','route','rule']
};
let connectionQuery='';
const routeLabels={direct:'直连',proxy:'代理'};
const routeKinds={keyword:'域名关键词',regex:'域名正则',exact:'精确域名',ip:'IP 地址'};
const themes={glass:'玻璃',light:'浅色',dark:'深色',mist:'雾蓝',paper:'暖纸',ink:'墨蓝'};
const defaultTheme='paper', savedTheme=load('theme',defaultTheme);
function applyTheme(theme){
  document.documentElement.dataset.theme=theme;
  void nativeCall('theme',{value:theme}).catch(()=>{});
}
applyTheme(Object.hasOwn(themes,savedTheme)?savedTheme:defaultTheme);
function nativeCall(action,args={},onProgress) {
  if(!native)return Promise.reject(new Error('请在 FlowPivot Windows 客户端中打开。'));
  const id=++serial;
  return new Promise((resolve,reject)=>{
    const timeout=setTimeout(()=>{requests.delete(id);reject(new Error('后台操作超时。'));},125000);
    requests.set(id,{resolve,reject,timeout,onProgress});native.postMessage({id,action,args});
  });
}
native?.addEventListener('message',e=>{const d=e.data,r=requests.get(d?.id);if(!r)return;if(d.progress){r.onProgress?.(d.progress);return;}requests.delete(d.id);clearTimeout(r.timeout);d.ok?r.resolve(d.body):r.reject(new Error(d.body));});
function toast(message){const t=$('#toast');t.textContent=tr(message);t.classList.add('visible','show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>t.classList.remove('visible','show'),3800);}
let disconnectNoticeTimer;
function takeSnapshot(next){
  const previous=snapshot;
  snapshot=next;
  const disconnected=(next.subscriptionDisconnectRevision??0)>(previous?.subscriptionDisconnectRevision??0);
  if(disconnected){
    const notice=$('#subscription-disconnect');
    $('span',notice).textContent=tr('当前节点已变更或被移除，代理已断开。');
    notice.hidden=false;
    clearTimeout(disconnectNoticeTimer);
    disconnectNoticeTimer=setTimeout(()=>{notice.hidden=true;},3000);
  }
}
const glyphs={home:'home',sources:'archive',nodes:'network',connections:'activity',settings:'settings',reach:'activity',ip:'globe'};
const badge=(text,kind='')=>`<span class="badge ${kind}">${html(text)}</span>`;
const square=glyph=>`<span class="square">${icon(glyph)}</span>`;
function heading(action=''){return `<header class="page-head enter"><h1 tabindex="-1">${labels[route]}</h1>${action}</header>`;}
function button(text,act,extra='',primary=false,glyph='',classes=''){return `<button class="button${primary?' primary':''} ${classes}" type="button" data-act="${act}" ${extra}>${glyph?icon(glyph):''}${html(tr(text))}</button>`;}
function empty(text,glyph='activity'){return `<div class="empty">${square(glyph)}<p>${html(text)}</p></div>`;}
function active(){return (snapshot?.nodes||[]).find(n=>n.id===snapshot.node);}
function nodeCaption(){
  if(snapshot.online&&snapshot.runningNodeName)return snapshot.runningNodeName+' · '+snapshot.engine;
  const node=active();
  return node?node.name+' · '+(snapshot.engine||node.engine):tr('未选择节点');
}
function diagnosticRoute(){return route==='reach'||route==='ip';}
const reduceMotion=matchMedia('(prefers-reduced-motion: reduce)');


function nav(){
  if(!$('#nav .nav-stage')){
    const item=(k,sub=false)=>`<button type="button" class="nav-link${sub?' sub-link':''}" data-route="${k}">${icon(glyphs[k])}<span>${labels[k]}</span></button>`;
    $('#nav').innerHTML=`<div class="nav-stage">`+['home','sources','nodes','connections'].map(k=>item(k)).join('')+`<div id="nav-group" class="nav-group"><button class="nav-link nav-parent" id="checks-toggle" type="button" data-act="checks-toggle" aria-label="检测" aria-controls="checks-subnav">${icon('scan')}<span>检测</span>${icon('chevron','nav-chevron')}</button><div class="subnav" id="checks-subnav"><div class="subnav-inner">${item('reach',true)}${item('ip',true)}</div></div></div>`+item('settings')+'</div>';
  }
  all('[data-route]',$('#nav')).forEach(link=>{
    $('span',link).textContent=tr(labels[link.dataset.route]);
    if(link.dataset.route===route)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');
  });
  $('#checks-toggle span').textContent=tr('检测');$('#checks-toggle').setAttribute('aria-label',tr('检测'));
  paintNav();
}
function paintNav(){
  const group=$('#nav-group'), toggle=$('#checks-toggle');
  const collapseActive=diagnosticRoute()&&!checksOpen;
  group.classList.toggle('nav-group-open',checksOpen);
  group.classList.toggle('nav-group-active',diagnosticRoute());
  toggle.setAttribute('aria-expanded',String(checksOpen));
  if(collapseActive)toggle.setAttribute('aria-current','page');else toggle.removeAttribute('aria-current');
  $('#checks-subnav').inert=!checksOpen;
}
const pointer=$('#pointer-glow'), precisePointer=matchMedia('(hover: hover) and (pointer: fine)');
let pointerFrame=0,pointerX=0,pointerY=0;
function hideGlow(){if(pointerFrame)cancelAnimationFrame(pointerFrame);pointerFrame=0;pointer.removeAttribute('data-on');}
page.addEventListener('pointermove',event=>{
  if(event.pointerType!=='mouse'||reduceMotion.matches||!precisePointer.matches)return;
  pointerX=event.clientX;pointerY=event.clientY;
  if(!pointerFrame)pointerFrame=requestAnimationFrame(()=>{
    pointerFrame=0;pointer.style.setProperty('--glow-x',`${pointerX}px`);pointer.style.setProperty('--glow-y',`${pointerY}px`);pointer.dataset.on='yes';
  });
});
page.addEventListener('pointerleave',hideGlow);
window.addEventListener('blur',hideGlow);
reduceMotion.addEventListener('change',hideGlow);precisePointer.addEventListener('change',hideGlow);
function home(){return heading()+`<section class="hero enter" id="session" data-status="offline" aria-label="启停控制"><div class="hero-content"><div class="home-status" role="status"><i class="dot" aria-hidden="true"></i><span id="session-title">未连接</span></div><h2>代理控制</h2><button type="button" class="power" id="power" data-act="power" disabled>${icon('power')}<span id="power-label">启动</span></button><p class="home-node" id="session-node"></p></div>${routeDiagram(`<button type="button" id="local-proxy" class="local-proxy" data-act="copy-proxy" title="复制本机代理地址" hidden>${icon('copy')}<span></span></button>`)}</section>
  <section class="card mode-card enter" aria-labelledby="mode-title"><div class="mode-label">${square('layers')}<h2 id="mode-title">代理模式</h2></div><div class="seg" role="group" aria-label="代理模式"><button type="button" data-act="mode" data-mode="system">系统代理</button><button type="button" data-act="mode" data-mode="tun">TUN</button><button type="button" data-act="mode" data-mode="local">仅本地</button></div><p class="mode-hint" id="session-warning" role="status" hidden></p></section>`;}
function sources(){
  const list=snapshot?.sources||[];
  const actions=`<div class="heading-actions">${button('导入文件','import-file','',false,'file')}${button('导入链接','import','',true,'plus')}</div>`;
  return heading(actions)+`<div class="import-status" role="status" hidden><i class="latency-spinner" aria-hidden="true"></i><span></span></div>
    <section class="card import-failure" role="alert" hidden><div><strong>导入 / 更新失败</strong><p></p></div>${button('知道了','dismiss-source-error')}</section>`+(list.length?`<div class="source-grid enter">${list.map(s=>{
    const id=html(s.id),name=html(s.name);
    return `<section class="card source-card" data-source="${id}" data-verbatim-label aria-label="${name}">
      <div class="source-head">${square('archive')}<h2><button class="source-select" type="button" data-act="select-source" data-id="${id}" aria-pressed="false" data-verbatim>${name}</button></h2>
      ${button('','source-menu',`aria-label="${language==='en'?'Manage':'管理'} ${name}" aria-haspopup="menu" aria-expanded="false" aria-controls="source-menu" data-id="${id}"`,false,'more','icon-only source-menu-trigger')}</div>
      <div class="source-overview"><p class="source-meta">${s.count} 个节点${s.skipped?` · 跳过 ${s.skipped} 条`:''}</p>${badge(s.format,'muted')}</div>
      ${sourceQuota(s)}
      ${s.refreshable?refreshStatus(s):''}
      <div class="source-state"><span data-source-current hidden>${icon('check')}当前订阅</span><span data-source-available>点击使用</span></div></section>`;
  }).join('')}</div>`:`<section class="card enter">${empty('还没有订阅','archive')}</section>`);
}
function refreshStatus(source){
  const hours=source.refreshHours;
  const next=source.nextRefresh?new Date(source.nextRefresh):null;
  const time=next?.getTime()>0?new Intl.DateTimeFormat(language,{month:'short',day:'numeric',hour:'numeric',minute:'2-digit'}).format(next):'—';
  const label=hours?(language==='en'?`Auto-update: every ${hours} h · Next: ${time}`:`自动更新：每 ${hours} 小时 · 下次 ${time}`):tr('自动更新：关闭');
  return `<p class="quota-note" data-verbatim>${html(label)}${source.autoRefreshFailed?' · '+html(tr('上次自动更新失败')):''}</p>`;
}
function sourceQuota(s){
  if(!s.quota)return s.refreshable?'<p class="quota-note">暂无套餐流量 · 可尝试更新</p>':'';
  const {total,used,remaining}=s.quota;
  const show=n=>typeof n==='number'?bytes(n):'—';
  return `<div class="source-quota">${total>0&&used!=null?`<progress max="${Number(total)}" value="${Math.min(total,used)}" aria-label="套餐流量已用比例"></progress>`:''}
    <dl>${[['总流量',total],['已用',used],['剩余',remaining]].map(([key,value])=>`<div><dt>${key}</dt><dd>${show(value)}</dd></div>`).join('')}</dl></div>`;
}
function sorted(){const list=[...(snapshot?.nodes||[])];if(order==='name')list.sort((a,b)=>a.name.localeCompare(b.name,language,{numeric:true}));if(order==='delay')list.sort((a,b)=>(typeof a.delay==='number'?a.delay:Infinity)-(typeof b.delay==='number'?b.delay:Infinity));return list;}
function nodes(){
  const list=sorted(),source=snapshot?.sources.find(s=>s.id===snapshot.source);
  if(!list.length)return heading()+`<section class="card enter">${empty('请先在「订阅」选择一个订阅','network')}</section>`;
  return heading(source?`<span class="badge node-source-badge" data-verbatim>${html(source.name)}</span>`:'')+`<section class="card list-card enter" aria-label="当前订阅的节点"><div class="node-toolbar"><span class="source-meta" id="node-count">${list.length} 个节点</span><div class="node-controls"><select class="node-sort themed-select" id="sort" aria-label="节点排序"><option value="delay">延迟从低到高</option><option value="original">订阅顺序</option><option value="name">节点名称</option></select><button class="button primary latency-action" type="button" data-act="delay"><span class="latency-action-icon">${icon('activity')}<i class="latency-spinner" aria-hidden="true" hidden></i></span><span id="latency-label">测延迟</span></button></div></div>
  <div class="latency-track" hidden><progress id="latency-progress" aria-label="节点测速进度"></progress></div><p class="latency-notice latency-error" id="latency-notice" role="status" hidden></p>
  <div id="node-list">${list.map(n=>`<button type="button" class="row node-select" data-act="select-node" data-id="${html(n.id)}">${square('network')}<span class="row-main"><strong data-verbatim>${html(n.name)}</strong><small>${html(n.kind.toUpperCase())} · ${html(n.engine)}${n.ready?'':' · 缺少内核'}</small></span><span class="node-latency"><i class="latency-spinner" aria-hidden="true" hidden></i><span class="latency-value"></span></span><span class="node-radio" aria-hidden="true"></span></button>`).join('')}</div></section>`;
}
function connectionOptions(list,selected){return list.map(([value,label])=>`<option value="${value}" ${selected===value?'selected':''}>${label}</option>`).join('');}
function connections(){return heading(button('分流规则','domain-rules','id="domain-rules-open" disabled'))+`<dl class="connection-metrics enter">${[['total','当前连接'],['routed','规则代理'],['direct','直连']].map(([k,n])=>`<div class="card metric"><dt>${n}</dt><dd id="metric-${k}">—</dd></div>`).join('')}</dl>
  <section class="card enter" aria-label="活动连接">
    <div class="connection-toolbar">
      <input class="field" type="search" id="connection-search" aria-label="搜索连接目标" placeholder="搜索目标" maxlength="200" value="${html(connectionQuery)}">
      <select class="field themed-select" id="connection-filter" aria-label="筛选走向">${connectionOptions([['all','所有走向'],['direct','直连'],['routed','规则代理']],connectionPrefs.filter)}</select>
      <select class="field themed-select" id="connection-sort" aria-label="连接排序">${connectionOptions([['original','内核顺序'],['host','按目标'],['up','上传最多'],['down','下载最多']],connectionPrefs.sort)}</select>
      <details class="connection-columns"><summary class="button">显示列</summary><div>${Object.entries(connectionColumns).filter(([k])=>k!=='host').map(([k,n])=>`<label><input type="checkbox" data-connection-column="${k}" ${connectionPrefs.columns.includes(k)?'checked':''}> ${n}</label>`).join('')}</div></details>
    </div>
    <div class="connection-list"><div id="connection-empty">${empty('暂无活动连接')}</div>
      <table id="connection-table" hidden><thead><tr>${Object.entries(connectionColumns).map(([k,n])=>`<th scope="col" data-column="${k}">${n}</th>`).join('')}<th scope="col">操作</th></tr></thead><tbody id="connection-rows"></tbody></table>
    </div><p class="check-note" id="connection-limit" hidden>最多显示内核返回的前 200 条；筛选和排序仅作用于这些连接。</p>
  </section>`;}
function paintConnections(){
  const c=snapshot.connections, tbody=$('#connection-rows');
  $('#domain-rules-open').disabled=!!pending;
  for(const k of ['total','direct','routed'])$('#metric-'+k).textContent=c[k];
  const filter=connectionPrefs.filter, q=connectionQuery.trim().toLowerCase();
  const list=c.rows.filter(r=>(filter==='all'||(r.route==='直连')===(filter==='direct'))&&(!q||r.host.toLowerCase().includes(q)));
  const sort=connectionPrefs.sort;
  if(sort==='host')list.sort((a,b)=>a.host.localeCompare(b.host,'zh-CN',{numeric:true}));
  else if(sort==='up'||sort==='down')list.sort((a,b)=>b[sort]-a[sort]);
  const existing=new Map(Array.from(tbody.rows,tr=>[tr.dataset.id,tr]));
  const focused=tbody.contains(document.activeElement)?document.activeElement:null;
  const visible=new Set(connectionPrefs.columns);
  for(const th of all('th[data-column]', $('#connection-table'))) th.hidden=!visible.has(th.dataset.column);
  let cursor=tbody.firstElementChild;
  for(const r of list){
    let row=existing.get(r.id);existing.delete(r.id);
    if(!row){
      row=document.createElement('tr');row.dataset.id=r.id;
      for(const k of Object.keys(connectionColumns))row.insertCell().dataset.column=k;
      const cell=row.insertCell();
      cell.innerHTML=button('断开','connection-close','',false,'','connection-close');
      const b=$('button',cell);b.dataset.id=r.id;b.setAttribute('aria-label',tr('断开')+' '+r.host);
    }
    const cells=row.cells;
    for(let i=0;i<cells.length-1;i++){
      const cell=cells[i], k=cell.dataset.column;
      const text=k==='up'||k==='down'?bytes(r[k]):k==='route'?tr(r[k]):r[k]||'—';
      cell.hidden=!visible.has(k);
      if(cell.textContent!==text)cell.textContent=text;
    }
    $('[data-act=connection-close]',row).disabled=!!pending;
    if(row===cursor)cursor=cursor.nextElementSibling;else tbody.insertBefore(row,cursor);
  }
  for(const tr of existing.values())tr.remove();
  if(focused&&document.activeElement!==focused&&focused.isConnected&&!focused.disabled)focused.focus({preventScroll:true});
  const message=c.rows.length?'没有匹配的连接':'暂无活动连接';
  $('#connection-empty p').textContent=tr(message);
  $('#connection-table').hidden=!list.length;$('#connection-empty').hidden=!!list.length;
  $('#connection-limit').hidden=c.total<=200;
}
const domainDialog=$('#domain-editor');
let domainEditing='', domainSaving=false;
function updateRuleExample(){
  const kind=$('#domain-kind').value;
  const field=$('#domain-pattern');
  field.placeholder={keyword:'baidu',regex:'(^|\\.)baidu\\.com$',exact:'www.baidu.com',ip:'192.0.2.10'}[kind];
  field.maxLength=kind==='exact'?254:128;
  for(const example of all('[data-rule-example]',domainDialog))example.hidden=example.dataset.ruleExample!==kind;
}
function domainForm(rule){
  domainEditing=rule?.id||'';
  $('#domain-kind').value=rule?.kind||'keyword';
  $('#domain-pattern').value=rule?.pattern||'';
  updateRuleExample();
  $('#domain-value').value=rule?.value||'direct';
  $('#domain-reset').hidden=!rule;
  $('#domain-save').textContent=tr(rule?'保存':'添加规则');
  $('#domain-error').hidden=true;
  $('#domain-status').hidden=true;
}
function paintDomainRules(){
  const list=$('#domain-list'), rules=snapshot?.domainRoutes||[];
  $('#domain-empty').hidden=!!rules.length;
  list.hidden=!rules.length;
  list.innerHTML=rules.map((rule,i)=>`<li>
    <span class="domain-item"><span>${html(tr(routeKinds[rule.kind]))} · ${html(tr(routeLabels[rule.value]))}</span><code>${html(rule.pattern)}</code></span>
    <div class="domain-actions">${button('编辑','domain-edit',`data-index="${i}" aria-label="${html(tr('编辑')+' '+rule.pattern)}"`)}
    ${button('删除','domain-delete',`data-index="${i}" aria-label="${html(tr('删除')+' '+rule.pattern)}"`)}</div>
  </li>`).join('');
}
async function changeDomainRule(args){
  if(pending||domainSaving)return;
  const error=$('#domain-error'),status=$('#domain-status');
  const controls=all('button,input,select',domainDialog);
  pending++;domainSaving=true;error.hidden=true;status.hidden=true;
  for(const control of controls)control.disabled=true;
  paint();
  let saved=false;
  try{
    if(polling)await polling;
    takeSnapshot(await nativeCall('domain-route',args));
    reach.clear();ip=null;
    saved=true;
  }catch(e){error.textContent=tr(e.message);error.hidden=false;}
  finally{
    pending--;domainSaving=false;
    for(const control of controls)control.disabled=false;
    if(saved){
      paintDomainRules();
      if(args.operation==='save'||args.id===domainEditing)domainForm();
      status.textContent=tr('分流规则已保存，新连接生效。');status.hidden=false;
      $('#domain-pattern').focus();
    }
    paint();
  }
}
function bytes(n){n=Number(n)||0;const u=['B','KB','MB','GB','TB'];let i=0;while(n>=1024&&i<u.length-1){i++;n/=1024;}return `${i?n.toFixed(n>=10?0:1):Math.round(n)} ${u[i]}`;}
function targets(){return [...book,...sites.map(u=>[new URL(u).hostname,u])];}
function checkToolbar(kind,label){return `<div class="check-toolbar"><div class="check-line"><i class="dot" aria-hidden="true"></i><span id="check-context"></span></div>${button(label,kind,'id="check-action"',true,kind==='ip'?'scan':'activity')}</div>`;}
function reachable(){return heading()+`<section class="card check-panel enter" aria-label="网站连通性">${checkToolbar('reach','测连通性')}
  <form id="site-form" class="site-form"><label class="sr-only" for="site-url">网站地址</label><input id="site-url" class="field" required maxlength="400" inputmode="url" autocomplete="off" spellcheck="false" placeholder="example.com"><button type="submit" class="button">${icon('plus')}添加网站</button></form>
  <div class="check-body"><table class="check-table"><thead><tr><th scope="col">站点</th><th scope="col">状态</th><th scope="col">用时</th></tr></thead><tbody>${targets().map(([n,u],i)=>{const r=reach.get(u);return `<tr><th scope="row"><div class="check-site"><div><strong data-verbatim>${html(n)}</strong></div>${i>=3?button('','site-remove',`aria-label="移除 ${html(n)}" data-index="${i-3}"`,false,'x','icon-only site-remove'):''}</div></th><td class="site-status">${r?badge(r.outcome+(r.status?' · '+r.status:''),r.status>=200&&r.status<400?'green':'warning'):badge('未检测','muted')}</td><td class="check-latency">${r?`${r.ms} ms`:'—'}</td></tr>`;}).join('')}</tbody></table></div><p class="check-note">网站可达不代表能登录、播放或地区解锁。</p></section>`;}
function ipPage(){return heading()+`<section class="card check-panel enter" aria-label="出口 IP 检测">${checkToolbar('ip','查出口 IP')}<div class="check-body">${ip?`<div class="ip-address"><span>出口 IP</span><strong>${html(ip.ip)}</strong></div><dl class="ip-metrics">${[['地区',[ip.country,ip.city].filter(Boolean).join(' ')],['ISP',ip.isp],['ASN',ip.asn?`AS${ip.asn}`:''],['机构',ip.org],['风险',ip.risk]].map(([k,v])=>`<div><dt>${k}</dt><dd>${html(v||'未知')}</dd></div>`).join('')}</dl>`:empty('尚未查询出口 IP','scan')}</div><p class="check-note">数据来自 ipwho.is 和 proxycheck.io，仅供参考。</p></section>`;}
function settings(){
  const disabled=snapshot?.online?'disabled':'', theme=document.documentElement.dataset.theme;
  return heading(button('免责声明','disclaimer-open','',false,'','disclaimer-trigger'))+`<section class="card settings-card settings-grid enter" aria-label="基础设置">
    <div class="row"><div class="row-main"><strong><label for="theme">界面主题</label></strong></div><select id="theme" class="field preference-select themed-select">${connectionOptions(Object.entries(themes),theme)}</select></div>
    <div class="row"><div class="row-main"><strong><label for="close-behavior">关闭窗口时</label></strong></div><select class="field preference-select themed-select" id="close-behavior" disabled><option value="tray"${snapshot?.closeToTray!==false?' selected':''}>最小化到托盘</option><option value="exit">退出程序</option></select></div>
    <div class="row"><div class="row-main"><strong>开机启动</strong><small>登录后打开流枢，不自动连接</small></div><button class="switch" type="button" data-act="autostart" role="switch" aria-label="开机启动" aria-checked="${!!snapshot?.autoStart}"></button></div>
    <div class="row"><div class="row-main"><strong><label for="preferred">通用 JSON 首选核心</label></strong><small>仅在两者都兼容时生效</small></div><select class="field preference-select themed-select" id="preferred" ${disabled}><option value="Xray"${snapshot?.preferred==='Xray'?' selected':''}>Xray</option><option value="V2Fly"${snapshot?.preferred==='V2Fly'?' selected':''}>V2Fly</option></select></div>
    <div class="row"><div class="row-main"><strong><label for="language">界面语言</label></strong></div><select class="field preference-select themed-select" id="language"><option value="zh-CN" ${language==='zh-CN'?'selected':''}>简体中文</option><option value="en" ${language==='en'?'selected':''}>English</option></select></div>
    <form class="row delay-settings" id="delay-settings">
      <label for="delay-url"><strong>延迟检测地址</strong></label><div class="delay-settings-controls">
        <input class="field" type="url" id="delay-url" maxlength="4096" autocomplete="off" spellcheck="false" placeholder="留空恢复默认" value="${html(snapshot?.delayUrl||'')}">
        <button class="button" type="submit">保存</button>
      </div>
    </form>
    <form class="row proxy-port-settings" id="proxy-port-settings">
      <div class="row-main"><label for="proxy-port"><strong>本机代理端口</strong></label>${snapshot?.online?'<small>停止代理后可修改</small>':''}</div>
      <div class="proxy-port-controls"><input class="field" id="proxy-port" type="text" inputmode="numeric" pattern="[0-9]{1,5}" maxlength="5" required value="${html(snapshot?.localProxyPort??'')}" ${disabled}>
      <button class="button" type="submit" ${disabled}>保存</button></div>
    </form>
  </section><section class="card settings-card settings-grid enter" aria-label="维护">
    <div class="row"><div class="row-main"><strong>软件更新</strong><small>当前版本 ${html(snapshot?.appVersion||'—')}</small></div><div class="core-actions">${button('检查更新','app-check')}${button('查看新版','app-offer','hidden',true)}</div></div>
    <div class="row"><div class="row-main"><strong>运行日志</strong></div>${button('查看日志','logs-view')}</div>
    <p id="app-update-status" class="core-note" role="status" hidden></p>
  </section><section class="card core-panel enter" aria-labelledby="core-heading"><div class="core-head"><h2 id="core-heading">内置内核</h2></div>
    <div class="core-grid">${(snapshot?.cores||[]).map(c=>`<div class="row core-row">${square('chip')}<div class="row-main"><strong>${html(c.id)}</strong><small data-bundled="${!!c.version}">${html(c.version||'文件缺失，请重新安装或完整解压')}</small></div></div>`).join('')}</div>
  </section>`;
}
function render(){nav();page.innerHTML=`<div class="page-view">${({home,sources,nodes,connections,reach:reachable,ip:ipPage,settings}[route])()}</div>`;if(language==='en')localize(page);if($('#sort'))$('#sort').value=order;paint();}
function navigate(next){if(next===route)return;route=next;if(diagnosticRoute())checksOpen=true;menuClose();render();$('h1',page)?.focus({preventScroll:true});window.scrollTo(0,0);}
function paint(){
  paintUpdate();
  if(!snapshot)return;
  const online=snapshot.online, t=snapshot.traffic;
  const starting=working==='start', switching=online&&working==='select-node';
  const status=starting||switching?'starting':online?'online':'offline', statusText=switching?'正在切换节点…':starting?'启动中…':online?'已连接':'未连接';
  $('#traffic').dataset.status=status;if($('#traffic-state').textContent!==tr(statusText))$('#traffic-state').textContent=tr(statusText);
  for(const [id,value] of [['traffic-download',bytes(t.rateDown)+'/s'],['traffic-upload',bytes(t.rateUp)+'/s'],['traffic-down-total',bytes(t.down)],['traffic-up-total',bytes(t.up)]]) $('#'+id).textContent=value;
  if(route==='home') {
    if($('#session').dataset.status!==status)$('#session').dataset.status=status;
    $('#session-title').textContent=tr(statusText);
    $('#power').setAttribute('aria-busy',String(starting||switching||working==='stop'));
    $('#session-node').textContent=nodeCaption();$('#session-warning').textContent=tr(snapshot.warning||(mode==='tun'&&!online?'TUN 为实验功能；仅支持 Mihomo，需以管理员身份启动客户端，并使用官方固定版核心。':!online&&active()&&!active().ready?'内置内核文件缺失，请重新安装或完整解压流枢。':''));
    const local=$('#local-proxy'), port=online&&Number.isInteger(snapshot.port)&&snapshot.port>=1&&snapshot.port<=65535?snapshot.port:null;
    local.hidden=!port;
    if(port){const address=`127.0.0.1:${port}`;$('span',local).textContent=address;local.setAttribute('aria-label',tr(`复制本机代理地址 ${address}`));}
    $('#session-warning').hidden=!$('#session-warning').textContent;
    $('#power-label').textContent=tr(switching?'切换中…':starting?'启动中…':working==='stop'?'关闭中…':online?'关闭':'启动');$('#power').disabled=!!pending||(!online&&(!active()?.ready||(mode===`tun`&&(!snapshot.canTun||active().engine!==`Mihomo`))));
    all('[data-mode]').forEach(b=>{b.setAttribute('aria-pressed',b.dataset.mode===(online?snapshot.mode:mode));b.disabled=online||!!pending;});
  }
  if(route==='sources'){
    const importing=working==='import'||working==='import-file', updating=working==='refresh'||working==='replace-url';
    const status=$('.import-status',page), text=tr(updating?'正在更新订阅…':importing?(working==='import-file'&&!importReading?'请选择订阅文件…':'正在读取并识别订阅…'):'');
    status.hidden=!importing&&!updating;
    if($('span',status).textContent!==text)$('span',status).textContent=text;
    const failure=$('.import-failure',page), detail=$('p',failure), error=tr(sourceError);
    failure.hidden=!error;if(detail.textContent!==error)detail.textContent=error;
    all('[data-act=import],[data-act=import-file]',page).forEach(b=>{b.disabled=!!pending;b.setAttribute('aria-busy',String(importing));});
  }
  if(route==='sources')all('[data-source]',page).forEach(card=>{
    const selected=card.dataset.source===snapshot.source;
    card.classList.toggle('source-active',selected);
    $('.source-select',card).setAttribute('aria-pressed',String(selected));
    $('[data-source-current]',card).hidden=!selected;
    $('[data-source-available]',card).hidden=selected;
  });
  if(route==='nodes')paintNodes();
  if(route==='settings'){
    const select=$('#close-behavior');select.disabled=!!pending;if(!pending)select.value=snapshot.closeToTray?'tray':'exit';
    $('#proxy-port').disabled=$('#proxy-port-settings button').disabled=online||!!pending;
  }
  if(route==='connections')paintConnections();
  if($('#check-action')){
    $('#check-action').disabled=!online||!!pending;
    $('#check-context').textContent=online?nodeCaption():tr('请先在首页启动代理');
  }
}
function paintNodes(){
  const list=$('#node-list');if(!list)return;
  const run=latencyRun?.source===snapshot.source?latencyRun:null;
  const error=latencyError?.source===snapshot.source?latencyError:null;
  const progress=$('#latency-progress'),button=$('[data-act=delay]');
  button.disabled=!!pending;
  $('.latency-action-icon .icon',button).toggleAttribute('hidden',!!run);
  button.setAttribute('aria-busy',String(!!run));
  $('.latency-spinner',button).hidden=!run;
  $('#latency-label').textContent=tr(run?'测速中':'测延迟');
  $('#sort').disabled=!!run;
  list.setAttribute('aria-busy',String(!!run));
  progress.parentElement.hidden=!run;
  if(run?.ids){progress.max=run.ids.size;progress.value=run.results.size;}else progress.removeAttribute('value');
  const count=snapshot.nodes?.length||0;
  $('#node-count').textContent=tr(run?.ids?`已测 ${run.results.size} / ${run.ids.size}`:run?'准备测速…':`${count} 个节点`);
  const status=$('#latency-notice');
  const message=!run&&error?(language==='en'?'Latency test failed: '+tr(error.detail):error.text):'';
  if(status.textContent!==message)status.textContent=message;
  status.hidden=!message;
  const data=new Map((snapshot.nodes||[]).map(n=>[n.id,n]));
  all('.node-select',list).forEach(row=>{
    const id=row.dataset.id, selected=id===snapshot.node;
    row.classList.toggle('row-selected',selected);row.setAttribute('aria-pressed',String(selected));
    const radio=$('.node-radio',row);
    if(selected!==!!radio.firstElementChild)radio.innerHTML=selected?icon('check'):'';
    const waiting=run?.ids?.has(id)&&!run.results.has(id);
    const delay=run?.results.has(id)?run.results.get(id):data.get(id)?.delay;
    const measured=typeof delay==='number', failed=delay==='failed';
    const cell=$('.node-latency',row),value=$('.latency-value',cell);
    cell.dataset.state=waiting?'testing':measured?'measured':failed?'timeout':'';
    $('.latency-spinner',cell).hidden=!waiting;
    const text=waiting?'测速中':measured?`${delay} ms`:failed?'失败':delay==='missing'?'缺少内核':'—';
    if(value.textContent!==tr(text))value.textContent=tr(text);
  });
}
function reorderNodeRows(){
  const list=$('#node-list');if(!list)return;
  const byId=new Map(all('.node-select',list).map(row=>[row.dataset.id,row]));
  const focused=list.contains(document.activeElement)?document.activeElement:null;
  let cursor=list.firstElementChild;
  for(const {id} of sorted()){
    const row=byId.get(id);if(!row)continue;
    if(row===cursor)cursor=cursor.nextElementSibling;else list.insertBefore(row,cursor);
  }
  if(focused&&document.activeElement!==focused)focused.focus({preventScroll:true});
}
function delayProgress(progress){
  const run=latencyRun;if(!run)return;
  if(Array.isArray(progress.ids)&&progress.ids.length)run.ids=new Set(progress.ids);
  else if(run.ids?.has(progress.id))run.results.set(progress.id,progress.delay);
  if(route==='nodes')paintNodes();
}
function paintUpdate(){
  const check=$('[data-act=app-check]'), offer=$('[data-act=app-offer]'), status=$('#app-update-status');
  if(!check)return;
  check.disabled=update.checking;check.textContent=tr(update.checking?'检查中…':'检查更新');
  offer.hidden=!update.offer;
  const text=update.text||(update.offer?`可更新至 ${update.offer.version}`:'');
  status.textContent=tr(text);status.hidden=!text;
}
function showUpdate(){
  const dialog=$('#update-dialog');
  if(!update.offer||dialog.open)return;
  if(document.querySelector('dialog[open]')){update.promptAfterEditor=true;return;}
  update.promptAfterEditor=false;
  menuClose();$('#update-error').hidden=true;
  $('#update-description').textContent=tr(`当前版本 ${snapshot?.appVersion||'—'} → 新版本 ${update.offer.version}`);
  dialog.showModal();
}
async function checkUpdate(auto=false){
  if(update.checking||!snapshot)return;
  update.checking=true;update.text='';paintUpdate();
  try{
    update.offer=await nativeCall('app-check');
    if(update.offer)showUpdate();
    else if(!auto)update.text='暂无可用更新。';
  }catch(e){update.text=e.message;}
  finally{update.checking=false;paintUpdate();}
}
async function openUpdate(button){
  if(!update.offer||button.disabled)return;
  button.disabled=true;
  try{
    await nativeCall('app-download',{version:update.offer.version});
    $('#update-dialog').close();toast('已打开下载。安装或替换前请退出流枢。');
  }catch(e){const el=$('#update-error');el.textContent=tr(e.message);el.hidden=false;}
  finally{button.disabled=false;}
}
function menuClose(){
  $('#source-menu').hidden=true;
  $('.source-menu-trigger[aria-expanded=true]')?.setAttribute('aria-expanded','false');
  menuId=null;
}
function menuOpen(id,x,y){
  const source=snapshot?.sources.find(v=>v.id===id);if(!source)return;
  menuClose();menuId=id;const menu=$('#source-menu');
  const trigger=all('.source-menu-trigger').find(b=>b.dataset.id===id);
  trigger?.setAttribute('aria-expanded','true');
  const item=(text,act,glyph,extra='')=>button(text,act,'role="menuitem" '+extra,false,glyph,'menu-item');
  menu.innerHTML=item('更新','refresh','refresh',source.refreshable?'':'disabled')+(source.refreshable?item('自动更新间隔','refresh-interval','activity'):'')+item('编辑名称','rename','edit')+item('替换订阅链接','replace-url','link')+
    (source.yaml?item('导出 YAML','source-export','file')+item('编辑节点 YAML','source-edit','edit')+item('本地覆写','source-patch','settings'):'')+item('移除','remove','trash');
  menu.hidden=false;menu.style.left=Math.max(4,Math.min(x,innerWidth-menu.offsetWidth-4))+'px';menu.style.top=Math.max(4,Math.min(y,innerHeight-menu.offsetHeight-4))+'px';
  $('.menu-item:not(:disabled)',menu)?.focus({preventScroll:true});
}
const logsDialog=$('#logs-dialog');
const disclaimerDialog=$('#disclaimer-dialog');
async function logAction(action){
  if(pending)return;
  const status=$('#logs-status'), text=$('#logs-text');
  if(action==='logs-view'){
    $('#logs-path').textContent=''; text.textContent=''; $('[data-act=logs-default]').hidden=true;
    logsDialog.showModal();
  }
  const actions=all('.log-actions button',logsDialog);
  pending++;actions.forEach(b=>b.disabled=true);logsDialog.setAttribute('aria-busy','true');
  try{
    if(action!=='logs-folder')status.textContent=tr(action==='logs-choose'?'请选择日志目录…':'正在读取日志…');
    if(polling)await polling;
    const result=await nativeCall(['logs-view','logs-refresh'].includes(action)?'logs-read':action);
    if(result){
      $('#logs-path').textContent=result.directory;
      $('[data-act=logs-default]').hidden=result.isDefault;
      status.textContent=tr(result.warning)||result.file;
      text.textContent=result.text||tr('今天暂无日志。可在「打开目录」查看历史文件。');
      text.scrollTop=text.scrollHeight;
    }else if(action==='logs-choose')status.textContent=tr('未更改日志目录。');
  }catch(e){status.textContent=tr(e.message);}
  finally{pending--;actions.forEach(b=>b.disabled=false);logsDialog.setAttribute('aria-busy','false');}
}
function edit(title,label,value,action){editor=action;$('#editor-title').textContent=tr(title);$('#editor-label').textContent=tr(label);$('#editor-input').value=value;$('#editor').showModal();$('#editor-input').focus();}
const configDialog=$('#config-editor');
let configTarget=null;
async function openConfig(id,patch){
  const result=await mutate(patch?'source-override':'source-yaml',{id},false);
  if(result==null||route!=='sources')return;
  configTarget={id,patch};
  $('#config-title').textContent=tr(patch?'本地覆写':'编辑节点 YAML');
  $('#config-help').textContent=tr(patch?'JSON 支持 prefix（节点名前缀）和 exclude（按名称排除）。留空清除，不执行脚本。':'仅编辑 proxies 节点。保存为本地副本，不修改原订阅。');
  $('#config-text').value=patch?result.text||'':result;
  $('#config-text').placeholder=patch?'{"prefix":"前缀 ","exclude":["节点名"]}':'';
  $('#config-text').maxLength=patch?12000:1600000;
  $('#config-save').textContent=tr(patch?'保存':'存为副本');
  $('#config-error').hidden=true;
  configDialog.showModal();
}
async function saveConfig(){
  if(pending||!configTarget)return;
  const {id,patch}=configTarget, text=$('#config-text'), save=$('#config-save'), status=$('#config-error');
  pending++;text.readOnly=true;save.disabled=true;status.hidden=true;
  try{
    if(polling)await polling;
    takeSnapshot(await nativeCall(patch?'source-override-save':'source-yaml-save',{id,body:text.value}));
    render();configDialog.close();toast(patch?'已保存覆写':'已保存本地副本');
  }catch(e){status.textContent=tr(e.message);status.hidden=false;}
  finally{pending--;text.readOnly=false;save.disabled=false;paint();}
}
async function mutate(a,args={},visual=true){
  if(pending)return;
  const testing=a==='delay', importing=a==='import'||a==='import-file';
  const sourceOperation=importing||a==='refresh'||a==='replace-url';
  if(sourceOperation)sourceError='';
  if(importing)importReading=a==='import';
  if(testing){latencyRun={source:snapshot.source,ids:null,results:new Map()};latencyError=null;}
  pending++;working=a;paint();
  const b=$(`[data-act="${a}"]`);
  if(b&&!['select-node','select-source','delay'].includes(a))b.disabled=true;
  try {
    if(polling)await polling;
    const result=await nativeCall(a,args,testing?delayProgress:a==='import-file'?()=>{importReading=true;paint();}:undefined);
    if(importing&&result?.traffic)toast('订阅已导入');
    if(a==='refresh'&&result?.traffic&&!snapshot.online)toast('订阅已更新');
    if(a==='refresh-interval'&&result?.traffic)toast('已保存自动更新间隔');
    if(testing){
      const count=latencyRun.ids?.size||0,total=snapshot.nodes.length;
      if(count>0)toast(count<total?`本轮完成 ${count} / ${total} 个节点测速`:`已完成 ${count} 个节点测速`);
      latencyRun=null;
    }
    if(result?.traffic){const changed=result.node!==snapshot?.node||result.online!==snapshot?.online||result.source!==snapshot?.source;takeSnapshot(result);if(changed){reach.clear();ip=null;}if(visual||(a==='select-source'&&(route==='nodes'||diagnosticRoute())))render();else{paint();if(testing&&route==='nodes'&&order==='delay')reorderNodeRows();}}
    return result;
  }catch(e){
    if(sourceOperation)sourceError=e.message;
    if(testing)latencyError={source:latencyRun?.source??snapshot.source,text:`测速未完成：${e.message}`,detail:e.message};
    if(!testing||route!=='nodes')toast(e.message);
    return null;
  }finally{
    if(testing)latencyRun=null;
    pending--;working='';
    if(b?.isConnected&&!['power','select-node','select-source','delay'].includes(a))b.disabled=false;
    paint();
  }
}
applyLanguage();render();
domainDialog.addEventListener('cancel',e=>{if(domainSaving)e.preventDefault();});
domainDialog.addEventListener('close',()=>{if(update.promptAfterEditor)showUpdate();});
logsDialog.addEventListener('close',()=>{if(update.promptAfterEditor)showUpdate();});
disclaimerDialog.addEventListener('close',()=>{if(update.promptAfterEditor)showUpdate();});
$('#editor').addEventListener('close',()=>{if(update.promptAfterEditor)showUpdate();});
configDialog.addEventListener('cancel',e=>{if($('#config-save').disabled)e.preventDefault();});
configDialog.addEventListener('close',()=>{
  $('#config-text').value='';configTarget=null;
  if(update.promptAfterEditor)showUpdate();
});
document.addEventListener('contextmenu',e=>{const card=e.target.closest('[data-source]');if(card){e.preventDefault();menuOpen(card.dataset.source,e.clientX,e.clientY);}});
document.addEventListener('keydown',e=>{
  if(e.key!=='Escape')return;
  const columns=$('.connection-columns[open]');
  if(columns&&!document.querySelector('dialog[open]')){columns.open=false;$('summary',columns).focus();return;}
  if(menuId){const trigger=all('.source-menu-trigger').find(b=>b.dataset.id===menuId);menuClose();trigger?.focus();}
  else if(checksOpen&&e.target.closest('#nav-group')){checksOpen=false;paintNav();$('#checks-toggle').focus();}
});
document.addEventListener('click', async e=>{
  const columns=$('.connection-columns[open]');
  if(columns&&!columns.contains(e.target))columns.open=false;
  const r=e.target.closest('[data-route]');if(r){e.preventDefault();navigate(r.dataset.route);return;}
  if(e.target.closest('.brand')){e.preventDefault();navigate('home');return;}
  const b=e.target.closest('[data-act]');const id=b?.dataset.id||menuId;
  if(b?.dataset.act!=='source-menu')menuClose();if(!b||b.disabled)return;
  const a=b.dataset.act;
  if(a==='checks-toggle'){checksOpen=!checksOpen;paintNav();return;}
  if(a==='power')return mutate(snapshot.online?'stop':'start',{mode},false);
  if(a==='copy-proxy'){
    const address=await mutate(a,{},false);
    if(address)toast('已复制 '+address);
    return;
  }
  if(a==='mode'){mode=b.dataset.mode;paint();return;}
  if(a==='cancel'){$('#editor').close();return;}
  if(a==='import')return edit('导入订阅','订阅地址或节点链接','',v=>mutate('import',{input:v}));
  if(a==='rename')return edit('编辑订阅','订阅名称',snapshot.sources.find(x=>x.id===id)?.name||'',v=>mutate('rename',{id,name:v}));
  if(a==='replace-url')return edit('替换订阅链接','新 HTTPS 链接（旧链接不显示）','',v=>mutate(a,{id,url:v}));
  if(a==='remove'){if(confirm(tr('移除此订阅及其节点？')))await mutate(a,{id});return;}
  if(a==='dismiss-source-error'){sourceError='';paint();return;}
  if(a==='source-menu'){const box=b.getBoundingClientRect();menuOpen(id,box.left,box.bottom);return;}
  if(a==='select-node')return mutate(a,{id,source:snapshot.source,revision:snapshot.nodeRevision},false);
  if(a==='select-source'){if(id!==snapshot?.source)return mutate(a,{id},false);return;}
  if(a==='refresh')return mutate(a,{id});
  if(a==='refresh-interval')return edit('自动更新间隔','间隔（小时，1–720；填 0 关闭）',String(snapshot.sources.find(s=>s.id===id)?.refreshHours||0),value=>mutate(a,{id,value}));
  if(a==='delay')return mutate(a,{},false);
  if(a==='connection-close')return mutate(a,{id},false);
  if(a==='domain-rules'){paintDomainRules();domainForm();domainDialog.showModal();return;}
  if(a==='domain-close'){domainDialog.close();return;}
  if(a==='domain-reset'){domainForm();$('#domain-pattern').focus();return;}
  if(a==='domain-edit'||a==='domain-delete'){
    const rule=snapshot?.domainRoutes?.[+b.dataset.index];
    if(!rule)return;
    if(a==='domain-delete')return changeDomainRule({operation:'delete',id:rule.id});
    domainForm(rule);$('#domain-pattern').focus();return;
  }
  if(a==='source-export'){if(await mutate(a,{id},false))toast('YAML 已导出');return;}
  if(a==='source-edit'||a==='source-patch')return openConfig(id,a==='source-patch');
  if(a==='config-cancel'){if(!$('#config-save').disabled)configDialog.close();return;}
  if(a==='site-remove'){const [url]=sites.splice(+b.dataset.index,1);reach.delete(url);store('sites',sites);render();return;}
  if(a==='reach'){const list=targets().map(x=>x[1]);const res=await mutate('check',{urls:list},false);if(res){reach=new Map(list.map((u,i)=>[u,res[i]]));if(route==='reach')render();}return;}
  if(a==='ip'){const res=await mutate('ip',{},false);if(res){ip=res;if(route==='ip')render();}return;}
  if(a==='autostart')return mutate(a,{value:snapshot?.autoStart?'off':'on'});
  if(a==='import-file')return mutate(a);
  if(a==='logs-close'){logsDialog.close();return;}
  if(a==='disclaimer-open'){disclaimerDialog.showModal();return;}
  if(a==='disclaimer-close'){disclaimerDialog.close();return;}
  if(['logs-view','logs-refresh','logs-folder','logs-choose','logs-default'].includes(a))return logAction(a);
  if(a==='app-check')return checkUpdate();
  if(a==='app-offer')return showUpdate();
  if(a==='app-later'){$('#update-dialog').close();return;}
  if(a==='app-download')return openUpdate(b);
});
document.addEventListener('change',e=>{if(e.target.id==='close-behavior'){void mutate('close-behavior',{value:e.target.value},false);return;}if(e.target.id==='preferred'){void mutate('preferred',{value:e.target.value});return;}if(e.target.id==='sort'){order=e.target.value;reorderNodeRows();}});
document.addEventListener('input',e=>{if(e.target.id==='connection-search'){connectionQuery=e.target.value;paintConnections();}});
document.addEventListener('change',e=>{
  const field=e.target;
  if(field.id==='domain-kind'){updateRuleExample();return;}
  if(field.id==='theme'&&Object.hasOwn(themes,field.value)){
    applyTheme(field.value);store('theme',field.value);return;
  }
  if(field.id==='language'){
    language=field.value==='en'?'en':'zh-CN';store('language',language);
    const delay=$('#delay-url').value, port=$('#proxy-port').value;
    applyLanguage();render();$('#delay-url').value=delay;$('#proxy-port').value=port;$('#language').focus({preventScroll:true});return;
  }
  if(field.id==='connection-filter')connectionPrefs.filter=field.value;
  else if(field.id==='connection-sort')connectionPrefs.sort=field.value;
  else if(field.dataset.connectionColumn)connectionPrefs.columns=['host',...all('[data-connection-column]:checked').map(c=>c.dataset.connectionColumn)];
  else return;
  store('connection-view',connectionPrefs);paintConnections();
});
document.addEventListener('submit',e=>{
  if(e.target.id==='domain-form'){
    e.preventDefault();
    void changeDomainRule({operation:'save',id:domainEditing,kind:$('#domain-kind').value,pattern:$('#domain-pattern').value,value:$('#domain-value').value});
    return;
  }
  if(e.target.id==='config-form'){e.preventDefault();void saveConfig();return;}
  if(e.target.id==='proxy-port-settings'){
    e.preventDefault();
    if(pending||!snapshot||snapshot.online)return;
    const value=$('#proxy-port').value;
    if(Number(value)<1||Number(value)>65535){toast('请输入 1–65535 之间的整数端口。');return;}
    void mutate('local-proxy-port',{value},false).then(result=>{
      if(!result)return;
      const field=$('#proxy-port');
      if(field)field.value=result.localProxyPort;
      toast('已保存本机代理端口');
    });
    return;
  }
  if(e.target.id==='delay-settings'){
    e.preventDefault();
    if(pending)return;
    const field=$('#delay-url'), submit=$('button',e.target);
    field.readOnly=true;submit.disabled=true;
    void mutate('delay-url',{value:field.value},false).then(result=>{
      if(result){field.value=result.delayUrl;toast('已保存延迟检测地址');}
    }).finally(()=>{field.readOnly=false;submit.disabled=false;});
    return;
  }
  if(e.target.id==='editor-form'){e.preventDefault();const fn=editor,v=$('#editor-input').value.trim();if(v){$('#editor').close();fn?.(v);}return;}
  if(e.target.id!=='site-form')return;e.preventDefault();const u=address($('#site-url').value);if(!u||targets().some(x=>x[1]===u)||sites.length>=20){toast('请输入新的有效网址（最多 20 个自定义站点）。');return;}sites.push(u);store('sites',sites);render();$('#site-url').focus();
});
async function poll(){
  if(!pending){polling=nativeCall('snapshot').then(s=>{
    const previous=snapshot;takeSnapshot(s);document.body.dataset.ready='yes';
    if(previous&&(previous.online!==s.online||previous.node!==s.node||previous.engine!==s.engine)){reach.clear();ip=null;if(['reach','ip'].includes(route))render();}
    if(!previous||(route==='settings'&&previous.online!==s.online)||(route==='sources'&&previous.subscriptionRevision!==s.subscriptionRevision)||(route==='nodes'&&previous.nodeRevision!==s.nodeRevision)){menuClose();render();}else paint();
    if(!update.started){update.started=true;void checkUpdate(true);}
  }).catch(e=>{if(!snapshot)toast(e.message);});await polling;polling=null;}
  setTimeout(poll,1000);
}
poll();
