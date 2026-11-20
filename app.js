'use strict';
const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
const icons = {
  home:'<path d="m3 10 9-7 9 7v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1z"/><path d="M9 21v-7h6v7"/>',
  network:'<rect x="9" y="2.5" width="6" height="6" rx="1.5"/><rect x="2.5" y="15.5" width="6" height="6" rx="1.5"/><rect x="15.5" y="15.5" width="6" height="6" rx="1.5"/><path d="M12 9v3M5.5 15.5V12h13v3.5"/>',
  archive:'<rect x="4" y="4" width="16" height="4" rx="1"/><path d="M6 8v11a1 1 0 0 0 1 1h10a1 1 0 0 0 1-1V8m-8 4h4"/>',
  activity:'<path d="M3 12h4l3-7 4 14 3-7h4"/>',
  settings:'<path d="M12 2.5v3m0 13v3M5.3 5.3l2.1 2.1m9.2 9.2 2.1 2.1M2.5 12h3m13 0h3M5.3 18.7l2.1-2.1m9.2-9.2 2.1-2.1"/><circle cx="12" cy="12" r="6.5"/><circle cx="12" cy="12" r="2.4"/>',
  layers:'<path d="m12 3 9 4.5-9 4.5L3 7.5 12 3zM3 12l9 4.5 9-4.5M3 16.5l9 4.5 9-4.5"/>',
  plus:'<path d="M12 5v14M5 12h14"/>', right:'<path d="m9 6 6 6-6 6"/>', power:'<path d="M12 3v9m4.8-6.5a8 8 0 1 1-9.6 0"/>',
  zap:'<path d="m13 2-9 12h7l-1 8 10-12h-7l1-8Z"/>', arrow:'<path d="M4 12h15m-6-6 6 6-6 6"/>',
  file:'<path d="M13 3H6a1 1 0 0 0-1 1v16a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V9zM13 3v6h6m-10 4h6m-6 4h6"/>',
  link:'<path d="m10 13 4-4m-6 7-1 1a3.5 3.5 0 0 1-5-5l4-4a3.5 3.5 0 0 1 5 0m2 8a3.5 3.5 0 0 0 5 0l4-4a3.5 3.5 0 0 0-5-5l-1 1"/>',
  refresh:'<path d="M19 10a7 7 0 1 0 .2 4M19 5v5h-5"/>', check:'<path d="m5 12 4 4L19 6"/>', x:'<path d="m6 6 12 12M18 6 6 18"/>',
  upload:'<path d="M12 16V4m-4 4 4-4 4 4M4 15v4a1 1 0 0 0 1 1h14a1 1 0 0 0 1-1v-4"/>',
  more:'<circle cx="5" cy="12" r="1"/><circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/>',
  edit:'<path d="m15 5 4 4M4 20l5-1 11-11a2.8 2.8 0 0 0-4-4L5 15z"/>',
  trash:'<path d="M4 7h16m-10 4v6m4-6v6M6 7l1 13h10l1-13M9 7V4h6v3"/>',
  chevron:'<path d="m6 9 6 6 6-6"/>',
  scan:'<path d="M8 3H5a2 2 0 0 0-2 2v3m13-5h3a2 2 0 0 1 2 2v3M3 16v3a2 2 0 0 0 2 2h3m13-5v3a2 2 0 0 1-2 2h-3M7 12h10"/>',
  chip:'<rect x="5" y="5" width="14" height="14" rx="3"/><rect x="9" y="9" width="6" height="6" rx="1"/><path d="M9 2v3m6-3v3M9 19v3m6-3v3M2 9h3m-3 6h3m14-6h3m-3 6h3"/>'
};
$('#icons').innerHTML = `<svg xmlns="http://www.w3.org/2000/svg" aria-hidden="true">${Object.entries(icons).map(([name, paths]) => `<symbol id="icon-${name}" viewBox="0 0 24 24">${paths}</symbol>`).join('')}</svg>`;
const icon = (name, cl = '') => `<svg class="icon ${cl}" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><use href="#icon-${name}"></use></svg>`;
const esc = (str) => String(str).replace(/[&<>"']/g, x => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[x]));
const pages = {
  home:['首页','home'], subscriptions:['订阅','archive'], nodes:['节点','network'],
  connections:['连接','activity'], settings:['设置','settings'],
  reach:['连通性检测','activity'], ip:['IP 纯净度检测','scan']
};
const isCheckPage = (page) => page==='reach'||page==='ip';
let checksOpen=false;
const modes = {system:'系统代理', tun:'TUN'};
const siteLimit=20;
function siteUrl(value){
  const input=value.trim();if(!input||input.length>300)return null;
  try {
    if(/\s|\\/.test(input)||/^(?:https?:)?\/\/(?:\/|$)/i.test(input)||input.startsWith('/'))return null;
    const url=new URL(/^[a-z][a-z\d+.-]*:\/\//i.test(input)?input:'https://'+input);
    if(!['http:','https:'].includes(url.protocol)||url.username||url.password||!url.hostname)return null;
    url.hash='';return url.href;
  } catch {return null;}
}
const siteDefaults = [['YouTube','https://youtube.com/'],['ChatGPT','https://chatgpt.com/'],['Netflix','https://netflix.com/']];
const nodeSorts = {default:'订阅顺序', fastest:'延迟从低到高', slowest:'延迟从高到低', name:'节点名称'};
const nodeNameOrder=new Intl.Collator('zh-CN',{numeric:true});
const nodeRuns=new Map();
const state = {
  page:'home', conn:'offline', activeSource:null, selected:null, mode:'system', autostart:false, theme:'light', nodeSort:'default', customSites:[],
  nodes:[
    {id:'hk', region:'HK', name:'香港 · 01', proto:'VLESS / REALITY', engine:'Xray', source:'starter'},
    {id:'tw', region:'TW', name:'台湾 · 02', proto:'VMess', engine:'Xray', source:'starter'},
    {id:'sg', region:'SG', name:'新加坡 · 01', proto:'Trojan / TLS', engine:'Mihomo', source:'yaml'},
    {id:'jp', region:'JP', name:'东京 · 02', proto:'Shadowsocks', engine:'Mihomo', source:'yaml'}
  ],
  sources:[
    {id:'starter', name:'快速上手', format:'节点列表', available:2, rejected:0, refreshable:true, refresh:false},
    {id:'yaml', name:'区域策略组', format:'Clash YAML', available:2, rejected:0, refreshable:true, refresh:false}
  ]
};
const coreCatalog = ['Mihomo','Xray','sing-box','V2Fly'].map(id=>({id,versions:['样本 01','样本 02','样本 03'],version:'样本 02',busy:false}));
const coreEntry = (id) => coreCatalog.find(core=>core.id===id);
const demoConnections = [
  {target:'media.example.test', route:'proxy', rule:'媒体规则'},
  {target:'sync.example.test', route:'direct', rule:'本地服务'},
  {target:'assets.example.test', route:'proxy', rule:'默认规则'},
  {target:'updates.example.test', route:'direct', rule:'系统更新'},
  {target:'workspace.example.test', route:'proxy', rule:'工作规则'}
];
try {
  const p = JSON.parse(localStorage.getItem('flowpivot-preferences') || '{}');
  state.theme = p.theme === 'dark' ? 'dark' : 'light'; state.autostart = p.autostart === true;
  state.mode = p.mode === 'tun' ? 'tun' : 'system';
  if(Array.isArray(p.customSites))state.customSites=[...new Set(p.customSites.filter(v=>typeof v==='string').map(siteUrl).filter(url=>url&&!siteDefaults.some(([,builtIn])=>builtIn===url)))].slice(0,siteLimit);
  for(const core of coreCatalog){const value=p.cores?.[core.id];if(core.versions.includes(value))core.version=value;}
} catch { /* Optional in file mode. */ }
document.documentElement.dataset.theme = state.theme;
const savePrefs = () => {
  try { localStorage.setItem('flowpivot-preferences', JSON.stringify({theme:state.theme, mode:state.mode, autostart:state.autostart,customSites:state.customSites,cores:Object.fromEntries(coreCatalog.map(({id,version})=>[id,version]))})); }
  catch { /* Optional in file mode. */ }
};
const activeSource = () => state.sources.find(s=>s.id===state.activeSource);
const activeNode = () => state.nodes.find(n=>n.id===state.selected && n.source===state.activeSource);
const badge = (s,c='') => `<span class="badge ${c}">${esc(s)}</span>`;
const btn = (s, act, ico = '', cl = '', extra = '') => `<button class="button ${cl}" type="button" data-act="${act}" ${extra}>${ico ? icon(ico) : ''}${esc(s)}</button>`;
const heading = (tail = '') => `<header class="enter page-head"><h1 tabindex="-1">${pages[state.page][0]}</h1>${tail}</header>`;
const square = (ico) => `<span class="square">${icon(ico)}</span>`;
const empty = (ico, message) => `<div class="empty">${square(ico)}<p>${message}</p></div>`;


function routeDiagram(){return `<figure class="route-diagram" aria-hidden="true">
  <svg viewBox="0 0 540 296" fill="none" preserveAspectRatio="xMidYMid meet">
    <defs><pattern id="route-grid" width="24" height="24" patternUnits="userSpaceOnUse"><path d="M24 0H0V24" class="diagram-grid"/></pattern></defs>
    <rect x="0" y="0" width="540" height="296" fill="url(#route-grid)"/>
    <path d="M24 44v-20h20m452 0h20v20M24 252v20h20m452 0h20v-20" class="diagram-corners"/>
    <path d="M73 150h110l43-43h72l40 43h129M73 150h110l43 43h72l40-43h129M73 150h394" class="diagram-traces"/>
    <path d="M73 150h394" class="diagram-link"/>
    <circle class="diagram-dot" cx="184" cy="150" r="3"/><circle class="diagram-dot" cx="338" cy="150" r="3"/>
    <circle class="diagram-dot" cx="226" cy="107" r="3"/><circle class="diagram-dot" cx="298" cy="193" r="3"/>
    <g class="diagram-block"><rect x="54" y="131" width="38" height="38" rx="7"/><path d="M64 142h18v12H64zm4 16h10m-5-4v4"/></g>
    <g class="diagram-block main"><rect x="235" y="123" width="68" height="54" rx="9"/><path d="M252 139h10l12 10h12m-34 0h34m-34 10h10l12-10m7-5 5 5-5 5"/></g>
    <g class="diagram-block"><rect x="448" y="131" width="38" height="38" rx="7"/><circle cx="467" cy="150" r="9"/><path d="M458 150h18m-9-9a15 15 0 0 1 0 18m0-18a15 15 0 0 0 0 18"/></g>
    <text x="73" y="217">本机</text><text x="269" y="217">代理内核</text><text x="467" y="217">出口</text>
  </svg>
</figure>`;}
function home(){return `${heading()}
  <section class="hero enter" aria-label="启停控制">
    <div class="hero-content">
      <div id="home-status" class="home-status" role="status"><i class="dot"></i><span id="home-status-text"></span></div>
      <h2>代理控制</h2>
      <button id="power" type="button" data-act="power" class="power" aria-describedby="home-node"></button>
      <p id="home-node" class="home-node"></p>
    </div>
    ${routeDiagram()}
  </section>
  <section class="card mode-card enter" aria-labelledby="mode-title"><div class="mode-label">${square('layers')}<h2 id="mode-title">代理模式</h2></div>
    <div class="seg" role="group" aria-label="代理模式">${Object.entries(modes).map(([id,label])=>`<button type="button" data-act="mode" data-mode="${id}" aria-pressed="false">${label}</button>`).join('')}</div>
    <p id="mode-hint" class="mode-hint" hidden>关闭后可切换模式</p>
  </section>`;}
function subscriptions(){return heading(btn('导入订阅','import','plus','primary'))+
  (state.sources.length?'<div class="source-grid enter">'+state.sources.map(source=>{
    const selected=source.id===state.activeSource, name=esc(source.name), id=source.id;
    return '<section class="card source-card '+(selected?'source-active':'')+'" data-source-card="'+id+'" aria-label="'+name+'">'+
      '<div class="source-head">'+square('archive')+'<h2><button class="source-select" type="button" data-act="source" data-id="'+id+'" aria-pressed="'+selected+'" aria-label="'+(selected?'当前订阅':'使用')+' '+name+'">'+name+'</button></h2>'+
      '<button class="button icon-only source-menu-trigger" type="button" data-act="source-menu" data-id="'+id+'" aria-label="管理 '+name+'" aria-haspopup="menu" aria-expanded="false" aria-controls="source-menu">'+icon(source.refresh?'refresh':'more',source.refresh?'spin':'')+'</button></div>'+
      '<div class="source-overview"><p class="source-meta">'+source.available+' 个节点'+(source.rejected?' · '+source.rejected+' 条未映射':'')+'</p>'+badge(source.format,'muted')+'</div>'+
      '<div class="source-state">'+(selected?icon('check')+'<span>当前订阅</span>':'<span>点击使用</span>')+'</div></section>';
  }).join('')+'</div>':'<section class="card enter">'+empty('archive','还没有订阅')+'</section>');}
const nodesForSource = id => state.nodes.filter(n=>n.source===id);
function sortedNodes(list){
  if(state.nodeSort==='name')return list.sort((a,b)=>nodeNameOrder.compare(a.name,b.name));
  if(state.nodeSort==='default')return list;
  return list.sort((a,b)=>{
    const aMeasured=typeof a.latency==='number', bMeasured=typeof b.latency==='number';
    if(aMeasured!==bMeasured)return aMeasured?-1:1;
    return aMeasured?(a.latency-b.latency)*(state.nodeSort==='fastest'?1:-1):0;
  });
}
function latencyState(n){return typeof n.latency==='number'?'measured':n.latency||'idle';}
function latencyLabel(n){return {testing:'测试中…', timeout:'超时', idle:'未测试'}[latencyState(n)] ?? n.latency+' ms';}
function nodeRow(n){
  const chosen=n.id===state.selected;
  return `<button type="button" class="row node-select ${chosen?'row-selected':''}" data-act="choose" data-id="${n.id}" aria-label="${chosen?'已选择':'选择'} ${esc(n.name)}" aria-describedby="delay-${n.id}" aria-pressed="${chosen}"><span class="location-icon">${esc(n.region)}</span>
    <span class="row-main"><strong>${esc(n.name)}</strong><small>${esc(n.proto)} · ${esc(n.engine)}</small></span>
    <span class="node-latency" id="delay-${n.id}" data-state="${latencyState(n)}">${latencyLabel(n)}</span>
    <span class="node-radio" aria-hidden="true">${chosen?icon('check'):''}</span></button>`;
}
function nodes(){
  const source=activeSource();
  if(!source) return `${heading()}<section class="card enter">${empty('network','请先在「订阅」选择一个订阅')}</section>`;
  const list=sortedNodes(nodesForSource(source.id)), busy=nodeRuns.has(source.id);
  if(!list.length)return `${heading(badge(source.name))}<section class="card enter">${empty('network','这个订阅没有可选节点')}</section>`;
  return `${heading(badge(source.name))}<section class="card list-card enter" aria-label="当前订阅的节点">
    <div class="node-toolbar">${badge('模拟测速','muted')}<div class="node-controls">
      <select id="node-sort" class="node-sort" aria-label="节点排序">${Object.entries(nodeSorts).map(([id,label])=>`<option value="${id}" ${id===state.nodeSort?'selected':''}>${label}</option>`).join('')}</select>
      ${btn(busy?'测速中…':'测延迟','test-latency',busy?'refresh':'activity','primary',busy?'disabled':'')}
    </div></div><div id="node-list" aria-busy="${busy}">${list.map(nodeRow).join('')}</div></section>`;
}
function paintNodeSelection(){
  $$('.node-select').forEach(row=>{
    const selected=row.dataset.id===state.selected;
    if(row.getAttribute('aria-pressed')===String(selected))return;
    row.classList.toggle('row-selected',selected);
    row.setAttribute('aria-pressed',String(selected));
    row.setAttribute('aria-label',(selected?'已选择':'选择')+' '+$('.row-main strong',row).textContent);
    $('.node-radio',row).innerHTML=selected?icon('check'):'';
  });
}
function reorderNodes(){
  const list=$('#node-list');if(!list)return;
  const rows=new Map([...list.children].map(row=>[row.dataset.id,row]));
  const focused=document.activeElement;
  sortedNodes(nodesForSource(state.activeSource)).forEach((n,i)=>{
    const row=rows.get(n.id);
    if(list.children[i]!==row)list.insertBefore(row,list.children[i]);
  });
  if(list.contains(focused)&&document.activeElement!==focused)focused.focus({preventScroll:true});
}
function paintNodeLatency(n){
  const cell=document.getElementById('delay-'+n.id);if(!cell)return;
  cell.dataset.state=latencyState(n);setText(cell,latencyLabel(n));
}
function paintLatencyRun(busy){
  const button=$('[data-act=test-latency]');
  button.disabled=busy;button.innerHTML=icon(busy?'refresh':'activity',busy?'spin':'')+(busy?'测速中…':'测延迟');
  $('#node-list').setAttribute('aria-busy',String(busy));
}
function testNodeLatency(){
  const source=state.activeSource, list=nodesForSource(source);
  if(!list.length||nodeRuns.has(source))return;
  paintLatencyRun(true);
  for(const n of list){n.latency='testing';paintNodeLatency(n);}
  const samples={hk:38,tw:61,sg:74,jp:92};
  const duration=matchMedia('(prefers-reduced-motion: reduce)').matches?160:850;
  nodeRuns.set(source,list.map((n,i)=>setTimeout(()=>{
    n.latency=samples[n.id] ?? (i%5===2?'timeout':45+((i+1)*79)%180);
    const visible=state.page==='nodes'&&state.activeSource===source;
    if(visible)paintNodeLatency(n);
    if(i===list.length-1){
      nodeRuns.delete(source);
      if(visible){paintLatencyRun(false);reorderNodes();toast('模拟测速完成');}
    }
  },duration*(.45+.55*(i+1)/list.length))));
}
function connections(){
  const rows=state.conn==='online'?demoConnections:[];
  const direct=rows.filter(r=>r.route==='direct').length;
  const metric=(name,value)=>`<div class="card metric"><dt>${name}</dt><dd>${value}</dd></div>`;
  return `${heading(badge('只读 · 演示','muted'))}
    <dl class="connection-metrics enter">${metric('当前连接',rows.length)}${metric('规则代理',rows.length-direct)}${metric('直连',direct)}</dl>
    <section class="card connection-list enter" aria-label="连接明细">${rows.length?`<table><caption class="sr-only">模拟连接明细</caption>
      <thead><tr><th scope="col">目标</th><th scope="col">走向</th><th scope="col">命中规则</th></tr></thead><tbody>${rows.map(r=>`<tr><td>${esc(r.target)}</td><td>${r.route==='proxy'?badge('规则代理'):badge('直连','green')}</td><td>${esc(r.rule)}</td></tr>`).join('')}</tbody></table>`:empty('activity','暂无连接')}</section>`;
}
function settings(){return `${heading()}<section class="card settings-card enter" aria-label="基础设置">
  <div class="row"><div class="row-main"><strong>界面主题</strong></div><div class="seg" role="group" aria-label="界面主题">${['light','dark'].map((t,i)=>`<button type="button" data-act="theme" data-theme-value="${t}" aria-pressed="${state.theme===t}">${['浅色','深色'][i]}</button>`).join('')}</div></div>
  <div class="row"><div class="row-main"><strong>开机启动</strong><small>仅保存演示偏好</small></div><button class="switch" type="button" data-act="autostart" role="switch" aria-label="开机启动" aria-checked="${state.autostart}"></button></div>
</section>
  <section class="card core-panel enter" aria-labelledby="core-heading">${corePanel()}</section>`;}
function coreLocked(core){return state.conn!=='offline'&&activeNode()?.engine===core.id;}
function corePanel(){
  return '<div class="core-head"><h2 id="core-heading">内核管理</h2>'+badge('模拟版本','muted')+'</div>'+coreCatalog.map(core=>{
    const locked=coreLocked(core), disabled=locked||core.busy, latest=core.version===core.versions.at(-1);
    const action=(act,label,cl='')=>btn(label,act,'',cl,'data-core="'+core.id+'" aria-label="'+label+' '+core.id+'"'+(disabled||(act==='core-update'&&latest)?' disabled':''));
    return '<div class="row core-row" data-core-row="'+core.id+'">'+square('chip')+'<div class="row-main"><strong>'+core.id+'</strong><small><span class="core-release">'+core.version+'</span>'+(locked?' · 使用中':core.busy?' · 处理中':'')+'</small></div><div class="core-actions">'+action('core-update',latest?'已是最新':'更新')+action('core-replace','更换版本')+'</div></div>';
  }).join('')+'<p class="core-note">各内核独立管理；正在使用的内核需断开后才能调整。不会下载或运行程序。</p>';
}

const checkSites = () => [...siteDefaults,...state.customSites.map(url=>[new URL(url).host,url])];
function reachStatus(result,busy){return result?badge(result[0],result[1]):badge(busy?'等待中':'未检测','muted');}
function reachRow([name,url],i,busy){
  const result=checkResults.reach?.get(url), address=new URL(url), custom=i>=siteDefaults.length;
  const detail=custom?address.pathname+address.search:address.host;
  return '<tr><th scope="row"><div class="check-site"><div><strong>'+esc(name)+'</strong>'+(detail==='/'?'':'<small>'+esc(detail)+'</small>')+'</div>'+
    (custom?btn('','remove-site','x','icon-only site-remove','aria-label="移除 '+esc(address.href)+'" data-site="'+esc(url)+'"'):'')+'</div></th><td class="site-status">'+reachStatus(result,busy)+'</td><td class="check-latency">'+esc(result?.[2]||'—')+'</td></tr>';
}
function addSite(form){
  const input=$('#site-url',form), url=siteUrl(input.value);
  if(!url||checkSites().some(([,saved])=>saved===url)||state.customSites.length>=siteLimit){
    input.setCustomValidity(!url?'请输入有效的网址或 HTTP(S) 链接（不含账号密码）。':state.customSites.length>=siteLimit?'最多添加 '+siteLimit+' 个网站。':'这个网站已在列表中。');
    input.reportValidity();return;
  }
  state.customSites.push(url);savePrefs();form.reset();updateChecks({sitesChanged:true,notify:'已添加网站'});input.focus();
}
function removeSite(url){
  const index=state.customSites.indexOf(url);if(index<0)return;
  state.customSites.splice(index,1);checkResults.reach?.delete(url);savePrefs();
  updateChecks({sitesChanged:true,notify:'已移除网站'});
  const rows=$$('.site-remove');(rows[index]||rows.at(-1)||$('#site-url'))?.focus({preventScroll:true});
}
const checkResults = {reach:null, ip:null, busyReach:false, busyIp:false};
const checkTimers = new Set();
const laterCheck = (fn,delay) => {const id=setTimeout(()=>{checkTimers.delete(id);fn();},delay);checkTimers.add(id);};
const checkMetrics = (name,value) => '<div><dt>'+esc(name)+'</dt><dd>'+esc(value)+'</dd></div>';
function sampleIp(node){
  const samples={hk:['203.0.113.24','中国香港','示例宽带 HK','AS64496','住宅网络','样本未见风险标记'],tw:['203.0.113.36','中国台湾','示例云网络 TW','AS64497','数据中心','样本标记为托管网络'],sg:['203.0.113.48','新加坡','示例云网络 SG','AS64498','数据中心','样本标记为托管网络'],jp:['203.0.113.72','日本','示例宽带 JP','AS64499','住宅网络','样本未见风险标记']};
  return Object.hasOwn(samples,node.id)?samples[node.id]:['203.0.113.90','未知','示例接入网络','AS64500','未知','样本数据不足'];
}
function checkPanel(kind){
  const reach=kind==='reach', busy=reach?checkResults.busyReach:checkResults.busyIp;
  const connected=state.conn==='online', context=connected?activeNode().name+' · '+activeNode().engine:'请先在首页启动代理';
  const action=btn(busy?'检测中…':reach?'测连通性':'查出口 IP',reach?'check-reach':'check-ip',busy?'refresh':'scan','primary',busy||!connected?'disabled':'');
  const toolbar='<div class="check-toolbar"><div class="check-line"><i class="dot"></i><span>'+esc(context)+'</span></div>'+action+'</div>';
  if(reach){
    const entries=checkSites().map((site,i)=>reachRow(site,i,busy)).join('');
    const form='<form id="site-form" class="site-form"><label class="sr-only" for="site-url">网站地址</label><input id="site-url" class="field" type="text" inputmode="url" autocomplete="off" spellcheck="false" required maxlength="300" placeholder="example.com">'+
      '<button type="submit" class="button">'+icon('plus')+'添加网站</button></form>';
    return toolbar+form+'<div class="check-body" aria-busy="'+busy+'"><table class="check-table"><caption class="sr-only">模拟连通性结果</caption><thead><tr><th scope="col">站点</th><th scope="col">状态</th><th scope="col">延迟 / 响应</th></tr></thead><tbody>'+entries+'</tbody></table></div>'+
      '<p class="check-note">网站可达不代表能登录、播放或地区解锁。</p>';
  }
  const ip=checkResults.ip;
  const details=ip?'<div class="ip-address"><span>出口 IP</span><strong>'+esc(ip[0])+'</strong></div><dl class="ip-metrics">'+['地区','ISP','ASN','网络类型','风险标记'].map((field,i)=>checkMetrics(field,ip[i+1])).join('')+'</dl>':
    '<div class="empty ip-empty">'+square('scan')+'<p>'+(busy?'正在查询…':'尚未查询出口 IP')+'</p></div>';
  return toolbar+'<div class="check-body" aria-busy="'+busy+'">'+details+'</div><p class="check-note">风险标记仅供参考，不是统一的纯净度评分。</p>';
}
function checkPage(kind){return heading(badge('模拟检测','muted'))+'<section class="card check-panel enter" aria-label="检测结果">'+checkPanel(kind)+'</section>';}
function updateChecks({notify=null,sitesChanged=false}={}){
  if(!isCheckPage(state.page))return;
  const panel=$('.check-panel');
  if(panel){
    const update=document.createElement('div');update.innerHTML=checkPanel(state.page);
    setText($('.check-line span',panel),$('.check-line span',update).textContent);
    const oldButton=$('.check-toolbar button',panel), newButton=$('.check-toolbar button',update);
    if(oldButton.disabled!==newButton.disabled)oldButton.disabled=newButton.disabled;
    if(oldButton.innerHTML!==newButton.innerHTML)oldButton.innerHTML=newButton.innerHTML;
    const body=$('.check-body',panel),nextBody=$('.check-body',update);
    if(state.page==='reach'&&!sitesChanged){
      body.setAttribute('aria-busy',String(checkResults.busyReach));
      const previous=$$('tbody tr',body);
      $$('tbody tr',nextBody).forEach((row,i)=>{
        const oldStatus=$('.site-status',previous[i]), status=$('.site-status',row);
        if(oldStatus.innerHTML!==status.innerHTML)oldStatus.innerHTML=status.innerHTML;
        setText($('.check-latency',previous[i]),$('.check-latency',row).textContent);
      });
    }else if(body.outerHTML!==nextBody.outerHTML){body.replaceWith(nextBody);}
  }
  if(notify)toast(notify);
}
function resetChecks(){
  for(const id of checkTimers)clearTimeout(id);checkTimers.clear();
  Object.assign(checkResults,{reach:null,ip:null,busyReach:false,busyIp:false});
  if(isCheckPage(state.page))updateChecks();
}
function runCheck(kind){
  const node=activeNode();if(state.conn!=='online'||!node)return;
  const fast=matchMedia('(prefers-reduced-motion: reduce)').matches;
  if(kind==='ip'){
    if(checkResults.busyIp)return;
    checkResults.busyIp=true;checkResults.ip=null;updateChecks();
    laterCheck(()=>{checkResults.ip=sampleIp(node);checkResults.busyIp=false;updateChecks({notify:'出口 IP 查询完成（模拟）'});},fast?120:720);
  }else{
    if(checkResults.busyReach)return;
    checkResults.busyReach=true;checkResults.reach=new Map();updateChecks();
    const defaults=[['可达','green','146 ms'],['访问受限','warning','HTTP 403'],node.engine==='Xray'?['超时','warning']:['可达','green','219 ms']];
    const sites=checkSites(), duration=fast?240:1080;
    sites.forEach(([,url],i)=>laterCheck(()=>{
      if(i<siteDefaults.length||state.customSites.includes(url))checkResults.reach.set(url,defaults[i]||['可达','green',(80+url.length%140)+' ms']);
      if(i===sites.length-1){checkResults.busyReach=false;updateChecks({notify:'连通性检测完成（模拟）'});}
      else updateChecks();
    },duration*(i+1)/sites.length));
  }
}
const views = {home,subscriptions,nodes,connections,settings,reach:()=>checkPage('reach'),ip:()=>checkPage('ip')};
const navLink=(id,sub=false)=>{
  const [title,glyph]=pages[id];
  return '<a href="#'+id+'" data-route="'+id+'" class="nav-link'+(sub?' sub-link':'')+'" aria-label="'+title+'" title="'+title+'">'+(sub?'':icon(glyph))+'<span>'+title+'</span></a>';
};
const navGroup='<div class="nav-group" id="nav-group"><button class="nav-link nav-parent" id="checks-toggle" type="button" data-act="checks-toggle" aria-expanded="false" aria-controls="checks-subnav">'+icon('scan')+'<span>检测</span>'+icon('chevron','nav-chevron')+'</button><div class="subnav" id="checks-subnav" inert><div class="subnav-inner">'+navLink('reach',true)+navLink('ip',true)+'</div></div></div>';
$('#nav').innerHTML=['home','subscriptions','nodes','connections'].map(id=>navLink(id)).join('')+navGroup+navLink('settings');
function updateNav(){
  $$('[data-route]').forEach(a=>a.dataset.route===state.page?a.setAttribute('aria-current','page'):a.removeAttribute('aria-current'));
  const nav=$('#nav-group'),sub=$('#checks-subnav');
  nav.classList.toggle('nav-group-active',isCheckPage(state.page));
  nav.classList.toggle('nav-group-open',checksOpen);
  sub.inert=!checksOpen;
  $('#checks-toggle').setAttribute('aria-expanded',String(checksOpen));
}
$('#nav').addEventListener('keydown',e=>{
  if(e.key==='Escape'&&checksOpen&&e.target.closest('#nav-group')){checksOpen=false;updateNav();$('#checks-toggle').focus();}
});
const setText = (el,value) => {if(el.textContent!==value)el.textContent=value;};
const traffic = {rx:0, tx:0, totalRx:0, totalTx:0, time:0, beat:0, clock:null};
const trafficSamples = [[485,73],[742,116],[1160,88],[657,145],[925,62],[553,112],[1350,192]];
const trafficLabels = {offline:'未连接',starting:'连接中',online:'已连接'};
function trafficUnit(bytes,rate=false){
  if(rate&&!bytes)return '0 KB/s';
  const sizes=['B','KB','MB','GB','TB'];
  let n=Math.max(0,bytes),unit=0;
  while(n>=1024&&unit<sizes.length-1){n/=1024;unit++;}
  const value=unit>0&&n<10?n.toFixed(1):String(Math.floor(n));
  return value+' '+sizes[unit]+(rate?'/s':'');
}
function paintTraffic(){
  $('#traffic').dataset.status=state.conn;
  setText($('#traffic-state'),trafficLabels[state.conn]);
  setText($('#traffic-download'),trafficUnit(traffic.rx,true));
  setText($('#traffic-upload'),trafficUnit(traffic.tx,true));
  setText($('#traffic-down-total'),trafficUnit(traffic.totalRx));
  setText($('#traffic-up-total'),trafficUnit(traffic.totalTx));
}
function accrueTraffic(){
  const now=performance.now();
  const seconds=Math.max(0,now-traffic.time)/1000;
  traffic.totalRx+=traffic.rx*seconds;traffic.totalTx+=traffic.tx*seconds;traffic.time=now;
}
function nextTrafficRate(){
  const [rx,tx]=trafficSamples[traffic.beat++%trafficSamples.length];
  traffic.rx=rx*1024;traffic.tx=tx*1024;
}
function startTraffic(){
  traffic.time=performance.now();nextTrafficRate();paintTraffic();
  traffic.clock=setInterval(()=>{accrueTraffic();nextTrafficRate();paintTraffic();},1000);
}
function stopTraffic(){
  if(traffic.clock!==null){clearInterval(traffic.clock);traffic.clock=null;accrueTraffic();}
  traffic.rx=0;traffic.tx=0;paintTraffic();
}
function updateHome(){
  if(state.page!=='home')return;
  const node=activeNode();
  const [label,action] = {offline:['未启动','启动'],starting:['正在启动','取消'],online:['模拟运行中','关闭']}[state.conn];
  $('.hero').dataset.status=state.conn;
  setText($('#home-status-text'),label);
  setText($('#home-node'), coreEntry(node?.engine)?.busy?'当前内核正在处理，请稍候':node?`${node.name} · ${node.engine}`:'请先选择节点');
  const power=$('#power');
  power.disabled=state.conn==='offline'&&(!node||coreEntry(node.engine)?.busy);
  power.innerHTML=`${icon(state.conn==='starting'?'refresh':'power',state.conn==='starting'?'spin':'')}<span>${action}</span>`;
  const locked=state.conn!=='offline';
  $$('[data-act=mode]').forEach(b=>{b.setAttribute('aria-pressed',String(state.mode===b.dataset.mode));b.disabled=locked;});
  $('#mode-hint').hidden=!locked;
}
function render({focus=false}={}){
  if(!$('#source-menu').hidden)closeSourceMenu(false);
  updateNav();
  $('#page').innerHTML=`<div class="page-view" data-page="${state.page}">${views[state.page]()}</div>`;
  updateHome();
  if(focus){window.scrollTo(0,0);$('h1').focus({preventScroll:true});}
}
function route(focus){
  const key=location.hash.slice(1);
  state.page=Object.hasOwn(pages,key)?key:key==='proxies'?'nodes':key==='logs'?'connections':key==='checks'?'reach':'home';
  checksOpen=isCheckPage(state.page);
  render({focus});
}
window.addEventListener('hashchange',()=>{clearTimeout(toastTimer);$('#toast').classList.remove('show');route(true);});route(false);
let toastTimer;
function toast(message){const notice=$('#toast');notice.innerHTML=`${icon('check')}<span>${esc(message)}</span>`;notice.classList.add('show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>notice.classList.remove('show'),2250);}
let connectionTimer;
function stopConnection(){
  const wasRunning=state.conn!=='offline';
  clearTimeout(connectionTimer);state.conn='offline';stopTraffic();updateHome();
  if(wasRunning){resetChecks();if(state.page==='connections')render();}
  return wasRunning;
}
function changeConnection(){
  if(state.conn!=='offline'){stopConnection();return;}
  const node=activeNode();if(!node||coreEntry(node.engine)?.busy)return;
  state.conn='starting';paintTraffic();updateHome();
  connectionTimer=setTimeout(()=>{state.conn='online';startTraffic();updateHome();if(state.page==='connections')render();if(isCheckPage(state.page))updateChecks();},matchMedia('(prefers-reduced-motion: reduce)').matches?120:820);
}
const dialog=$('#import-dialog');
let file=null, example=null, pending=null, working=false, importRun=0, closeTimer, nextSource=0;
const mock={
  yaml:{format:'Clash YAML',engine:'Mihomo',protocols:['Trojan','Shadowsocks'],rejected:0,label:'Mihomo 策略演示'},
  mixed:{format:'Base64 节点列表',engine:null,protocols:['VLESS','Trojan','Shadowsocks'],rejected:1,label:'混合节点演示'},
  sing:{format:'sing-box JSON',engine:'sing-box',protocols:['VLESS'],rejected:0,label:'sing-box 配置演示'},
  v2fly:{format:'V2Fly JSON',engine:'V2Fly',protocols:['VMess'],rejected:0,label:'V2Fly 配置演示'}
};
const pickEngine=(protocol)=>['VLESS','VMess'].includes(protocol)?'Xray':'Mihomo';
const schemes={vless:'VLESS',vmess:'VMess',ss:'Shadowsocks',shadowsocks:'Shadowsocks',trojan:'Trojan',hysteria2:'Hysteria 2',hy2:'Hysteria 2',tuic:'TUIC',socks:'SOCKS',socks5:'SOCKS'};
function ownProtocol(v){const p=String(v||'').toLowerCase();return Object.hasOwn(schemes,p)?schemes[p]:null;}
const counts=(values)=>({protocols:values.filter(Boolean).slice(0,60),rejected:values.filter(v=>!v).length+Math.max(0,values.filter(Boolean).length-60)});
function detect(body){
  const text=body.replace(/^\uFEFF/,'').trim();
  if(!text)throw new Error('没有读到配置内容。');
  if(text.startsWith('{')){
    let j;try{j=JSON.parse(text);}catch{throw new Error('JSON 格式无效。');}
    const outs=Array.isArray(j.outbounds)?j.outbounds.filter(x=>x&&typeof x==='object'):[];
    const sg=outs.some(x=>typeof x.type==='string'), xr=outs.some(x=>typeof x.protocol==='string');
    if(sg&&xr)throw new Error('这份 JSON 同时包含两种内核结构，无法自动匹配。');
    if(sg) return {format:'sing-box JSON',engine:'sing-box',...counts(outs.filter(x=>!['direct','block','dns','selector','urltest'].includes(x.type)).map(x=>ownProtocol(x.type)))};
    if(xr){ const hasReality=outs.some(x=>x.streamSettings?.security==='reality'||x.streamSettings?.realitySettings); if(!hasReality) throw new Error('检测到 Xray / V2Fly JSON，但缺少明确归属。请试用内置样本。');return {format:'Xray JSON',engine:'Xray',...counts(outs.filter(x=>!['freedom','blackhole','dns','loopback'].includes(x.protocol)).map(x=>ownProtocol(x.protocol)))}; }
    throw new Error('未识别到此 JSON 的出站配置。');
  }
  const trimmedYamlLine=/^proxies\s*:/m;
  if(trimmedYamlLine.test(text)){
    const raw=text.slice(text.search(trimmedYamlLine));
    const block=raw.split(/\r?\n(?=[a-zA-Z][\w-]*\s*:)/,1)[0];
    const types=[...block.matchAll(/(?:^|[\s,{])type\s*:\s*["']?([\w-]+)/gm)].map(m=>ownProtocol(m[1]));
    return {format:'Clash YAML',engine:'Mihomo',...counts(types)};
  }
  const lines=(s)=>s.split(/\r?\n/).map(x=>x.trim()).filter(x=>x&&!x.startsWith('#'));
  const scan=(s)=>lines(s).map(x=>{const m=/^([\w+.-]+):\/\/([^\s]+)/i.exec(x);return m?ownProtocol(m[1]):null;});
  if(/^[\w+.-]+:\/\//m.test(text)) return {format:'明文节点列表',engine:null,...counts(scan(text))};
  const b64=text.replace(/\s+/g,'').replace(/-/g,'+').replace(/_/g,'/');
  if(b64.length>=12&&/^[a-zA-Z0-9+/]+={0,2}$/.test(b64)){
    let decoded='';try{decoded=atob(b64);}catch{ /* no match */ }
    if(/^[\w+.-]+:\/\//m.test(decoded))return {format:'Base64 节点列表',engine:null,...counts(scan(decoded))};
  }
  throw new Error('未识别此格式。可以试用内置样本，或导入 YAML、JSON、TXT 文件。');
}
function openImport(){
  if(dialog.open) return;
  clearTimeout(closeTimer); dialog.classList.remove('closing'); file=null; example=null; pending=null; working=false; importRun++;
  $('#import-content').innerHTML=`<div class="dialog-head"><div><h2 id="dialog-title">导入订阅</h2></div><button type="button" class="button icon-only" data-act="close" aria-label="关闭">${icon('x')}</button></div>
  <form class="import-form" id="import-form" novalidate><label for="url">订阅或节点链接</label><input id="url" class="field" autocomplete="off" spellcheck="false" placeholder="粘贴链接" maxlength="4096" aria-describedby="import-note" autofocus>
  <label class="drop" id="drop">${icon('upload')}<span id="file-label">拖入文件，或点击选择</span><input id="file" type="file" accept=".yaml,.yml,.json,.txt,.conf" aria-label="选择配置文件"></label>
  <div class="examples" aria-label="内置样本">${btn('Clash YAML','sample','file','', 'data-example="yaml"')}${btn('混合节点','sample','layers','', 'data-example="mixed"')}${btn('sing-box JSON','sample','file','', 'data-example="sing"')}${btn('V2Fly JSON','sample','file','', 'data-example="v2fly"')}</div>
  <div id="import-note" class="safety-note">离线演示，不访问远程链接。</div>
  <div id="import-result" aria-live="polite"></div>
  <div class="dialog-foot"><button class="button primary" id="submit-import" type="submit">${icon('zap')}识别格式</button></div></form>`;
  dialog.showModal();
}
function closeImport(){
  if(!dialog.open||dialog.classList.contains('closing'))return;
  importRun++; working=false; file=null; example=null; pending=null; dialog.classList.add('closing');
  closeTimer=setTimeout(()=>{dialog.close();dialog.classList.remove('closing');$('#import-content').replaceChildren();},matchMedia('(prefers-reduced-motion: reduce)').matches?0:140);
}
dialog.addEventListener('cancel',e=>{e.preventDefault();closeImport();});
dialog.addEventListener('close',()=>{if(state.page==='subscriptions')$('[data-act=import]')?.focus({preventScroll:true});});
dialog.addEventListener('click',e=>{if(e.target===dialog)closeImport();});
const wait=(n)=>new Promise(resolve=>setTimeout(resolve,n));
function resetResult(){importRun++;working=false;pending=null;$('#import-result').replaceChildren();$('#submit-import').disabled=false;$('#submit-import').innerHTML=`${icon('zap')}识别格式`;}
function useFile(incoming){if(!incoming)return;file=incoming;example=null;$('#url').value='';$('#file-label').textContent='已选择文件';resetResult();}
async function submitImport(){
  if(working||dialog.classList.contains('closing'))return;
  if(pending){saveImport(pending);closeImport();render();toast('已添加订阅');return;}
  const token=++importRun, input=$('#url').value.trim(), selectedFile=file, selectedSample=example;
  working=true; const submit=$('#submit-import'); submit.disabled=true; submit.innerHTML=`${icon('refresh','spin')}正在识别…`;
  try{
    await wait(matchMedia('(prefers-reduced-motion: reduce)').matches?0:330);
    if(token!==importRun)return;
    let result;
    if(selectedSample)result=mock[selectedSample];
    else if(selectedFile){if(selectedFile.size>256*1024)throw new Error('演示仅接收 256 KB 以内的文本配置。');result=detect(await selectedFile.text());}
    else if(/^https?:\/\//i.test(input))throw new Error('离线演示不会访问远程链接。请选择一个内置样本，体验订阅识别流程。');
    else if(input)result=detect(input);
    else throw new Error('请粘贴链接、选择文件，或使用内置样本。');
    if(token!==importRun)return;
    if(!result.protocols.length)throw new Error(`已识别 ${result.format}，但未找到可预览的已知节点类型。`);
    pending={...result,from:selectedSample?'内置样本':selectedFile?'配置文件':'单节点链接'};
    const engines=[...new Set(result.protocols.map(p=>result.engine||pickEngine(p)))].join(' / ');
    $('#import-result').innerHTML=`<div class="result"><strong>已识别 · ${esc(result.format)}</strong><p>建议内核：${esc(engines)} · ${result.protocols.length} 条可预览${result.rejected?` · ${result.rejected} 条未映射`:''}</p></div>`;
    submit.innerHTML=`${icon('check')}添加到订阅`;
  }catch(err){ if(token!==importRun)return; $('#import-result').innerHTML=`<div class="result error" role="alert">${esc(err.message)}</div>`;submit.innerHTML=`${icon('zap')}识别格式`; }
  finally{if(token===importRun){working=false;submit.disabled=false;}}
}
function saveImport(result){
  const id=`source-${++nextSource}`;
  state.sources.unshift({id,name:result.label||`${result.format} · ${nextSource}`,format:result.format,available:result.protocols.length,rejected:result.rejected,refresh:false,refreshable:result.from==='内置样本'});
  result.protocols.forEach((p,i)=>state.nodes.push({id:`${id}-${i}`,region:'DEMO',name:`${p} · ${String(i+1).padStart(2,'0')}`,proto:p,engine:result.engine||pickEngine(p),source:id}));
}
const sourceMenu=$('#source-menu');
let menuOwner=null;
const sourceTrigger=id=>$('[data-act="source-menu"][data-id="'+id+'"]');
function closeSourceMenu(restore=true){
  if(sourceMenu.hidden)return;
  const owner=sourceTrigger(menuOwner);
  owner?.setAttribute('aria-expanded','false');
  sourceMenu.hidden=true;sourceMenu.replaceChildren();menuOwner=null;
  if(restore)owner?.focus({preventScroll:true});
}
function openSourceMenu(id,x,y){
  const owner=sourceTrigger(id), source=state.sources.find(item=>item.id===id);
  if(!source||!owner)return;
  closeSourceMenu(false);menuOwner=id;
  const item=(act,label,glyph,extra='')=>btn(label,act,glyph,'menu-item','role="menuitem" tabindex="-1" data-id="'+id+'" '+extra);
  const updateLabel=source.refresh?'更新中…':'更新订阅';
  sourceMenu.innerHTML=item('refresh',updateLabel,'refresh',!source.refreshable||source.refresh?'aria-disabled="true"'+(!source.refreshable?' title="本地文件和单节点链接请重新导入"':''):'')+
    item('edit-source','编辑订阅','edit')+item('remove','移除订阅','trash');
  sourceMenu.hidden=false;owner.setAttribute('aria-expanded','true');
  const rect=owner.getBoundingClientRect();
  const left=Math.max(8,Math.min(x??rect.right-sourceMenu.offsetWidth,innerWidth-sourceMenu.offsetWidth-8));
  const top=Math.max(8,Math.min(y??rect.bottom+6,innerHeight-sourceMenu.offsetHeight-8));
  sourceMenu.style.left=left+'px';sourceMenu.style.top=top+'px';
  $('[role=menuitem]:not([aria-disabled=true])',sourceMenu)?.focus({preventScroll:true});
}
document.addEventListener('contextmenu',e=>{
  const card=e.target.closest('[data-source-card]');if(!card)return;
  e.preventDefault();openSourceMenu(card.dataset.sourceCard,e.clientX||undefined,e.clientY||undefined);
});
document.addEventListener('pointerdown',e=>{
  if(!sourceMenu.hidden&&!e.target.closest('#source-menu,[data-act=source-menu]'))closeSourceMenu(false);
});
document.addEventListener('focusin',e=>{
  if(!sourceMenu.hidden&&!sourceMenu.contains(e.target)&&!e.target.closest('[data-act=source-menu]'))closeSourceMenu(false);
});
document.addEventListener('keydown',e=>{
  if(e.key==='ContextMenu'||(e.shiftKey&&e.key==='F10')){
    const card=e.target.closest('[data-source-card]');if(card){e.preventDefault();openSourceMenu(card.dataset.sourceCard);}
  }
  if(sourceMenu.hidden||!sourceMenu.contains(e.target))return;
  if(e.key==='Escape'){e.preventDefault();closeSourceMenu();return;}
  if(e.key==='Tab'){closeSourceMenu();return;}
  const items=$$('[role=menuitem]',sourceMenu), i=items.indexOf(document.activeElement);
  const next=e.key==='ArrowDown'?(i+1)%items.length:e.key==='ArrowUp'?(i-1+items.length)%items.length:e.key==='Home'?0:e.key==='End'?items.length-1:null;
  if(next!==null){e.preventDefault();items[next]?.focus({preventScroll:true});}
});
window.addEventListener('resize',()=>closeSourceMenu());
window.addEventListener('scroll',()=>closeSourceMenu(),{passive:true});
const editDialog=$('#edit-dialog');
let editId=null;
function editSource(source){
  editId=source.id;const name=$('#edit-name');name.value=source.name;name.setCustomValidity('');editDialog.showModal();name.select();
}
$('#edit-name').addEventListener('input',e=>e.target.setCustomValidity(''));
$('#edit-source').addEventListener('submit',e=>{
  e.preventDefault();const source=state.sources.find(item=>item.id===editId);if(!source)return;
  const name=$('#edit-name'), value=name.value.trim();if(!value){name.setCustomValidity('请输入订阅名称');name.reportValidity();return;}
  source.name=value;editDialog.close();if(state.page==='subscriptions'){render();sourceTrigger(source.id)?.focus({preventScroll:true});}
  toast('订阅已保存');
});
function updateSource(source){
  if(!source.refreshable||source.refresh)return;
  source.refresh=true;
  const paint=()=>{const trigger=sourceTrigger(source.id);if(trigger)trigger.innerHTML=icon(source.refresh?'refresh':'more',source.refresh?'spin':'');};
  paint();setTimeout(()=>{source.refresh=false;if(!state.sources.includes(source))return;paint();toast('模拟更新完成');},470);
}
const coreDialog=$('#core-dialog');
let replacementId=null;
function paintCores(){
  const panel=$('.core-panel');if(!panel)return;
  const hadFocus=panel.contains(document.activeElement), active=document.activeElement?.dataset;
  panel.innerHTML=corePanel();
  if(hadFocus&&active?.core){
    const controls=$$('[data-core="'+active.core+'"]',panel);
    (controls.find(b=>!b.disabled&&b.dataset.act===active.act)||controls.find(b=>!b.disabled))?.focus({preventScroll:true});
  }
}
function changeCoreVersion(core,version,action){
  if(core.busy||coreLocked(core)||!core.versions.includes(version)||version===core.version)return;
  core.busy=true;paintCores();updateHome();
  setTimeout(()=>{core.version=version;core.busy=false;savePrefs();paintCores();updateHome();toast(core.id+' 已'+action+'（模拟）');},matchMedia('(prefers-reduced-motion: reduce)').matches?100:430);
}
function replaceCore(core){
  if(core.busy||coreLocked(core))return;
  replacementId=core.id;$('#core-description').textContent=core.id+' · 当前 '+core.version+'。仅更换这一内核的模拟版本。';
  const select=$('#core-version');select.replaceChildren(...core.versions.map(version=>new Option(version+(version===core.version?'（当前）':''),version,version===core.version,version===core.version)));
  coreDialog.showModal();select.focus();
}
$('#core-form').addEventListener('submit',e=>{
  e.preventDefault();const core=coreEntry(replacementId), version=$('#core-version').value;coreDialog.close();
  if(core)changeCoreVersion(core,version,'更换版本');
});
const removeDialog=$('#remove-dialog');
let removeId=null;
removeDialog.addEventListener('close',()=>{
  const source=state.sources.find(s=>s.id===removeId);removeId=null;
  if(!source||removeDialog.returnValue!=='remove')return;
  const stopped=source.id===state.activeSource ? stopConnection() : false;
  if(source.id===state.activeSource){state.activeSource=null;state.selected=null;}
  for(const timer of nodeRuns.get(source.id)||[])clearTimeout(timer);
  nodeRuns.delete(source.id);
  state.sources=state.sources.filter(s=>s.id!==source.id);state.nodes=state.nodes.filter(n=>n.source!==source.id);
  if(state.page==='subscriptions'){render();$('[data-act=import]').focus({preventScroll:true});}
  toast(stopped?'已移除订阅并关闭连接':'已移除订阅');
});
document.addEventListener('submit',e=>{
  if(e.target.id==='import-form'){e.preventDefault();void submitImport();}
  if(e.target.id==='site-form'){e.preventDefault();addSite(e.target);}
});
document.addEventListener('input',e=>{
  if(e.target.id==='site-url')e.target.setCustomValidity('');
  if(e.target.id==='url'){file=null;example=null;$('#file').value='';$('#file-label').textContent='拖入文件，或点击选择';resetResult();}
});
document.addEventListener('change',e=>{
  if(e.target.id==='file')useFile(e.target.files[0]);
  if(e.target.id==='node-sort'&&Object.hasOwn(nodeSorts,e.target.value)){state.nodeSort=e.target.value;reorderNodes();}
});
for(const name of ['dragover','dragleave','drop'])dialog.addEventListener(name,e=>{const drop=e.target.closest('#drop');if(!drop)return;e.preventDefault();drop.classList.toggle('on',name==='dragover');if(name==='drop'){ $('#file').value='';useFile(e.dataTransfer.files[0]); }});
document.addEventListener('click',e=>{
  const b=e.target.closest('[data-act]');if(!b)return;
  const {act,id,themeValue,mode,core:coreId}=b.dataset;
  if(b.closest('#source-menu')){
    if(b.getAttribute('aria-disabled')==='true')return;
    closeSourceMenu();
  }
  if(act==='source-menu'){
    if(!sourceMenu.hidden&&menuOwner===id)closeSourceMenu();else openSourceMenu(id);
    return;
  }
  if(act==='edit-source'){const source=state.sources.find(item=>item.id===id);if(source)editSource(source);}
  if(act==='cancel-edit')editDialog.close();
  if(act==='cancel-core')coreDialog.close();
  if(act==='core-update'||act==='core-replace'){
    const core=coreEntry(coreId);if(!core)return;
    if(act==='core-update')changeCoreVersion(core,core.versions.at(-1),'更新');else replaceCore(core);
  }
  if(act==='import')openImport();
  if(act==='close')closeImport();
  if(act==='sample'){example=b.dataset.example;file=null;$('#file').value='';$('#file-label').textContent='拖入文件，或点击选择';$('#url').value=`https://demo.invalid/${example}`;resetResult();void submitImport();}
  if(act==='power')changeConnection();
  if(act==='checks-toggle'){checksOpen=!checksOpen;updateNav();}
  if(act==='test-latency')testNodeLatency();
  if(act==='check-reach')runCheck('reach');
  if(act==='remove-site')removeSite(b.dataset.site);
  if(act==='check-ip')runCheck('ip');
  if(act==='mode'&&state.conn==='offline'&&Object.hasOwn(modes,mode)){state.mode=mode;savePrefs();updateHome();}
  if(act==='source'&&state.activeSource!==id&&state.sources.some(s=>s.id===id)){
    const stopped=stopConnection();state.activeSource=id;state.selected=null;render();
    $(`[data-act=source][data-id="${id}"]`).focus({preventScroll:true});
    toast(stopped?'已切换订阅并关闭连接':'已切换订阅');
  }
  if(act==='choose'&&state.selected!==id&&state.nodes.some(n=>n.id===id&&n.source===state.activeSource)){
    const stopped=stopConnection();state.selected=id;paintNodeSelection();
    toast(stopped?'已换节点，请回首页重新启动':'已选择节点');
  }
  if(act==='refresh'){const source=state.sources.find(item=>item.id===id);if(source)updateSource(source);}
  if(act==='remove'){
    const source=state.sources.find(s=>s.id===id);if(!source)return;
    removeId=id;removeDialog.returnValue='';
    setText($('#remove-detail'),id===state.activeSource?`将移除「${source.name}」及其节点，清除当前选择${state.conn!=='offline'?'并关闭连接':''}。`:`将移除「${source.name}」及其节点。`);
    removeDialog.showModal();
  }
  if(act==='theme'){state.theme=themeValue;document.documentElement.dataset.theme=state.theme;$$('[data-act=theme]').forEach(x=>x.setAttribute('aria-pressed',String(x.dataset.themeValue===state.theme)));savePrefs();}
  if(act==='autostart'){state.autostart=!state.autostart;b.setAttribute('aria-checked',String(state.autostart));savePrefs();}
});
