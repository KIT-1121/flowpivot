'use strict';
const $ = (s,el=document) => el.querySelector(s);
const all = (s,el=document) => [...el.querySelectorAll(s)];
const html = x => String(x ?? '').replace(/[&<>"']/g, a => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'})[a]);
const native = window.chrome?.webview;
const page = $('#page');
const requests = new Map();
const labels = {home:'首页',sources:'订阅',nodes:'节点',connections:'连接',reach:'连通性检测',ip:'IP 纯净度检测',settings:'设置'};
const book = [['YouTube','https://www.youtube.com/'],['ChatGPT','https://chatgpt.com/'],['Netflix','https://www.netflix.com/']];
let route='home', checksOpen=false, working='', snapshot=null, pending=0, polling=null, serial=0, menuId=null, editor=null, toastTimer, order='original', mode='system', reach=new Map(), ip=null;
function load(key, fallback) { try{return JSON.parse(localStorage.getItem(key)) ?? fallback;} catch{return fallback;} }
function store(key,v) { try{localStorage.setItem(key,JSON.stringify(v));}catch{} }
function address(v) {
  if(!String(v).trim())return null;
  try{const u=new URL(/^[a-z]+:\/\//i.test(v.trim())?v.trim():'https://'+v.trim());return /^https?:$/.test(u.protocol)&&!u.username&&!u.password?u.href:null;}catch{return null;}
}
let latencyRun=null, latencyError=null;
let update={checking:false,started:false,offer:null,text:"",promptAfterEditor:false};
let sites=load('sites',[]); sites=Array.isArray(sites)?sites.map(address).filter(Boolean).slice(0,20):[];
const themes={glass:'玻璃',light:'浅色',dark:'深色'};
const savedTheme=load('theme','glass');
document.documentElement.dataset.theme=Object.hasOwn(themes,savedTheme)?savedTheme:'glass';
function nativeCall(action,args={},onProgress) {
  if(!native)return Promise.reject(new Error('请在 FlowPivot Windows 客户端中打开。'));
  const id=++serial;
  return new Promise((resolve,reject)=>{
    const timeout=setTimeout(()=>{requests.delete(id);reject(new Error('后台操作超时。'));},125000);
    requests.set(id,{resolve,reject,timeout,onProgress});native.postMessage({id,action,args});
  });
}
native?.addEventListener('message',e=>{const d=e.data,r=requests.get(d?.id);if(!r)return;if(d.progress){r.onProgress?.(d.progress);return;}requests.delete(d.id);clearTimeout(r.timeout);d.ok?r.resolve(d.body):r.reject(new Error(d.body));});
function toast(message){const t=$('#toast');t.textContent=message;t.classList.add('visible','show');clearTimeout(toastTimer);toastTimer=setTimeout(()=>t.classList.remove('visible','show'),3800);}
const glyphs={home:'home',sources:'archive',nodes:'network',connections:'activity',settings:'settings'};
const badge=(text,kind='')=>`<span class="badge ${kind}">${html(text)}</span>`;
const square=glyph=>`<span class="square">${icon(glyph)}</span>`;
function heading(action=''){return `<header class="page-head enter"><h1 tabindex="-1">${labels[route]}</h1>${action}</header>`;}
function button(text,act,extra='',primary=false,glyph='',classes=''){return `<button class="button${primary?' primary':''} ${classes}" type="button" data-act="${act}" ${extra}>${glyph?icon(glyph):''}${html(text)}</button>`;}
function empty(text,glyph='activity'){return `<div class="empty">${square(glyph)}<p>${html(text)}</p></div>`;}
function active(){return (snapshot?.nodes||[]).find(n=>n.id===snapshot.node);}
function diagnosticRoute(){return route==='reach'||route==='ip';}
const reduceMotion=matchMedia('(prefers-reduced-motion: reduce)');
const navDuration=175;
let glassMotion,glassTarget;
function nav(animate=false){
  if(!$('#nav-glass')){
    const item=(k,sub=false)=>`<button type="button" class="nav-link${sub?' sub-link':''}" data-route="${k}">${sub?'':icon(glyphs[k])}<span>${labels[k]}</span></button>`;
    $('#nav').innerHTML=`<div class="nav-stage"><div class="nav-glass-layer" aria-hidden="true"><div id="nav-glass"></div></div>`+['home','sources','nodes','connections'].map(k=>item(k)).join('')+`<div id="nav-group" class="nav-group"><button class="nav-link nav-parent" id="checks-toggle" type="button" data-act="checks-toggle" aria-label="检测" aria-controls="checks-subnav">${icon('scan')}<span>检测</span>${icon('chevron','nav-chevron')}</button><div class="subnav" id="checks-subnav"><div class="subnav-inner">${item('reach',true)}${item('ip',true)}</div></div></div>`+item('settings')+'</div>';
    const observer=new ResizeObserver(()=>positionGlass());
    observer.observe($('.nav-stage'));observer.observe($('#nav-group'));
    reduceMotion.addEventListener('change',()=>{glassMotion?.cancel();positionGlass();});
  }
  all('[data-route]',$('#nav')).forEach(link=>{
    if(link.dataset.route===route)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');
  });
  paintNav(animate);
}
function paintNav(animate=false){
  const group=$('#nav-group'), toggle=$('#checks-toggle');
  const collapseActive=diagnosticRoute()&&!checksOpen;
  group.classList.toggle('nav-group-open',checksOpen);
  group.classList.toggle('nav-group-active',diagnosticRoute());
  toggle.setAttribute('aria-expanded',String(checksOpen));
  if(collapseActive)toggle.setAttribute('aria-current','page');else toggle.removeAttribute('aria-current');
  $('#checks-subnav').inert=!checksOpen;
  positionGlass(animate);
}
function positionGlass(animate=false){
  const glass=$('#nav-glass'), stage=glass.closest('.nav-stage');
  const item=diagnosticRoute()&&!checksOpen?$('#checks-toggle'):$(`[data-route="${route}"]`,stage);
  const box=item.getBoundingClientRect(), origin=stage.getBoundingClientRect();
  const target={x:box.left-origin.left+1,y:box.top-origin.top+1,w:box.width-2,h:box.height-2};
  if(target.w<=0||target.h<=0||glassTarget&&Object.keys(target).every(k=>Math.abs(target[k]-glassTarget[k])<.25))return;
  const from=glassTarget?glass.getBoundingClientRect():null;
  glassMotion?.cancel();glassTarget=target;
  glass.style.width=`${target.w}px`;glass.style.height=`${target.h}px`;
  const frame=(x,y,sx=1,sy=1)=>`translate(${x}px,${y}px) scale(${sx},${sy})`;
  glass.style.transform=frame(target.x,target.y);glass.dataset.ready='yes';
  if(!animate||!from||reduceMotion.matches)return;
  const dx=from.left-origin.left+from.width/2-target.x-target.w/2;
  const dy=from.top-origin.top+from.height/2-target.y-target.h/2;
  const distance=Math.hypot(dx,dy);if(distance<2)return;
  const stretch=Math.min(.30,.12+distance/900);
  const alongX=Math.abs(dx)/distance, alongY=Math.abs(dy)/distance;
  const sx=alongX*stretch-alongY*.045, sy=alongY*stretch-alongX*.045;
  const step=(offset,p,x,y,easing)=>({offset,transform:frame(target.x+dx*p,target.y+dy*p,x,y),easing});
  const rebound=Math.min(.11,11/distance);
  glassMotion=glass.animate([
    step(0,1,from.width/target.w,from.height/target.h,'cubic-bezier(.26,.12,.47,.86)'),
    step(.36,.39,1+sx,1+sy,'cubic-bezier(.15,.45,.35,1)'),
    step(.72,-rebound,1-sx*.22,1-sy*.22,'ease-out'),
    step(1,0,1,1,'linear'),
  ],{duration:navDuration});
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
  return heading(actions)+(list.length?`<div class="source-grid enter">${list.map(s=>{
    const id=html(s.id),name=html(s.name);
    return `<section class="card source-card" data-source="${id}" aria-label="${name}">
      <div class="source-head">${square('archive')}<h2><button class="source-select" type="button" data-act="select-source" data-id="${id}" aria-pressed="false">${name}</button></h2>
      ${button('','source-menu',`aria-label="管理 ${name}" aria-haspopup="menu" aria-expanded="false" aria-controls="source-menu" data-id="${id}"`,false,'more','icon-only source-menu-trigger')}</div>
      <div class="source-overview"><p class="source-meta">${s.count} 个节点${s.skipped?` · 跳过 ${s.skipped} 条`:''}</p>${badge(s.format,'muted')}</div>
      <div class="source-state"><span data-source-current hidden>${icon('check')}当前订阅</span><span data-source-available>点击使用</span></div></section>`;
  }).join('')}</div>`:`<section class="card enter">${empty('还没有订阅','archive')}</section>`);
}
function sorted(){const list=[...(snapshot?.nodes||[])];if(order==='name')list.sort((a,b)=>a.name.localeCompare(b.name,'zh-CN',{numeric:true}));if(order==='delay')list.sort((a,b)=>(typeof a.delay==='number'?a.delay:Infinity)-(typeof b.delay==='number'?b.delay:Infinity));return list;}
function nodes(){
  const list=sorted(),source=snapshot?.sources.find(s=>s.id===snapshot.source);
  if(!list.length)return heading()+`<section class="card enter">${empty('请先在「订阅」选择一个订阅','network')}</section>`;
  return heading(source?badge(source.name,'node-source-badge'):'')+`<section class="card list-card enter" aria-label="当前订阅的节点"><div class="node-toolbar"><span class="source-meta" id="node-count">${list.length} 个节点</span><div class="node-controls"><select class="node-sort" id="sort" aria-label="节点排序"><option value="original">订阅顺序</option><option value="delay">延迟从低到高</option><option value="name">节点名称</option></select><button class="button primary latency-action" type="button" data-act="delay"><span class="latency-action-icon">${icon('activity')}<i class="latency-spinner" aria-hidden="true" hidden></i></span><span id="latency-label">测延迟</span></button></div></div>
  <div class="latency-track" hidden><progress id="latency-progress" aria-label="节点测速进度"></progress></div><p class="latency-notice latency-error" id="latency-notice" role="status" hidden></p>
  <div id="node-list">${list.map(n=>`<button type="button" class="row node-select" data-act="select-node" data-id="${html(n.id)}">${square('network')}<span class="row-main"><strong>${html(n.name)}</strong><small>${html(n.kind.toUpperCase())} · ${html(n.engine)}${n.ready?'':' · 缺少内核'}</small></span><span class="node-latency"><i class="latency-spinner" aria-hidden="true" hidden></i><span class="latency-value"></span></span><span class="node-radio" aria-hidden="true"></span></button>`).join('')}</div></section>`;
}
function connections(){return heading(badge('只读','muted'))+`<dl class="connection-metrics enter">${[['total','当前连接'],['routed','规则代理'],['direct','直连']].map(([k,n])=>`<div class="card metric"><dt>${n}</dt><dd id="metric-${k}">—</dd></div>`).join('')}</dl><section class="card connection-list enter"><div id="connection-empty">${empty('暂无活动连接')}</div><table id="connection-table" hidden><thead><tr><th scope="col">目标</th><th scope="col">走向</th><th scope="col">规则</th></tr></thead><tbody id="connection-rows"></tbody></table></section>`;}
function bytes(n){n=Number(n)||0;const u=['B','KB','MB','GB','TB'];let i=0;while(n>=1024&&i<u.length-1){i++;n/=1024;}return `${i?n.toFixed(n>=10?0:1):Math.round(n)} ${u[i]}`;}
function targets(){return [...book,...sites.map(u=>[new URL(u).hostname,u])];}
function checkToolbar(kind,label){return `<div class="check-toolbar"><div class="check-line"><i class="dot" aria-hidden="true"></i><span id="check-context"></span></div>${button(label,kind,'id="check-action"',true,kind==='ip'?'scan':'activity')}</div>`;}
function reachable(){return heading()+`<section class="card check-panel enter" aria-label="网站连通性">${checkToolbar('reach','测连通性')}
  <form id="site-form" class="site-form"><label class="sr-only" for="site-url">网站地址</label><input id="site-url" class="field" required maxlength="400" inputmode="url" autocomplete="off" spellcheck="false" placeholder="example.com"><button type="submit" class="button">${icon('plus')}添加网站</button></form>
  <div class="check-body"><table class="check-table"><thead><tr><th scope="col">站点</th><th scope="col">状态</th><th scope="col">用时</th></tr></thead><tbody>${targets().map(([n,u],i)=>{const r=reach.get(u);return `<tr><th scope="row"><div class="check-site"><div><strong>${html(n)}</strong></div>${i>=3?button('','site-remove',`aria-label="移除 ${html(n)}" data-index="${i-3}"`,false,'x','icon-only site-remove'):''}</div></th><td class="site-status">${r?badge(r.outcome+(r.status?' · '+r.status:''),r.status>=200&&r.status<400?'green':'warning'):badge('未检测','muted')}</td><td class="check-latency">${r?`${r.ms} ms`:'—'}</td></tr>`;}).join('')}</tbody></table></div><p class="check-note">网站可达不代表能登录、播放或地区解锁。</p></section>`;}
function ipPage(){return heading()+`<section class="card check-panel enter" aria-label="出口 IP 检测">${checkToolbar('ip','查出口 IP')}<div class="check-body">${ip?`<div class="ip-address"><span>出口 IP</span><strong>${html(ip.ip)}</strong></div><dl class="ip-metrics">${[['地区',[ip.country,ip.city].filter(Boolean).join(' ')],['ISP',ip.isp],['ASN',ip.asn?`AS${ip.asn}`:''],['机构',ip.org],['风险',ip.risk]].map(([k,v])=>`<div><dt>${k}</dt><dd>${html(v||'未知')}</dd></div>`).join('')}</dl>`:empty('尚未查询出口 IP','scan')}</div><p class="check-note">数据来自 ipwho.is 和 proxycheck.io，仅供参考。</p></section>`;}
function settings(){
  const disabled=snapshot?.online?'disabled':'', theme=document.documentElement.dataset.theme;
  return heading()+`<section class="card settings-card settings-grid enter" aria-label="基础设置">
    <div class="row"><div class="row-main"><strong>界面主题</strong></div><div class="seg theme-options" role="group" aria-label="界面主题">${Object.entries(themes).map(([value,label])=>`<button type="button" data-act="theme" data-value="${value}" aria-pressed="${theme===value}">${label}</button>`).join('')}</div></div>
    <div class="row"><div class="row-main"><strong><label for="close-behavior">关闭窗口时</label></strong></div><select class="field preference-select" id="close-behavior" disabled><option value="exit">退出程序</option><option value="tray"${snapshot?.closeToTray?' selected':''}>最小化到托盘</option></select></div>
    <div class="row"><div class="row-main"><strong>开机启动</strong><small>登录后打开流枢，不自动连接</small></div><button class="switch" type="button" data-act="autostart" role="switch" aria-label="开机启动" aria-checked="${!!snapshot?.autoStart}"></button></div>
    <div class="row"><div class="row-main"><strong><label for="preferred">通用 JSON 首选核心</label></strong><small>仅在两者都兼容时生效</small></div><select class="field preference-select" id="preferred" ${disabled}><option value="Xray"${snapshot?.preferred==='Xray'?' selected':''}>Xray</option><option value="V2Fly"${snapshot?.preferred==='V2Fly'?' selected':''}>V2Fly</option></select></div>
  </section><section class="card settings-card enter" aria-label="软件更新">
    <div class="row"><div class="row-main"><strong>软件更新</strong><small>当前版本 ${html(snapshot?.appVersion||'—')}</small></div><div class="core-actions">${button('检查更新','app-check')}${button('查看新版','app-offer','hidden',true)}</div></div>
    <p id="app-update-status" class="core-note" role="status" hidden></p>
  </section><section class="card core-panel enter" aria-labelledby="core-heading"><div class="core-head"><h2 id="core-heading">内置内核</h2></div>
    <div class="core-grid">${(snapshot?.cores||[]).map(c=>`<div class="row core-row">${square('chip')}<div class="row-main"><strong>${html(c.id)}</strong><small data-bundled="${!!c.version}">${html(c.version||'文件缺失，请重新安装或完整解压')}</small></div></div>`).join('')}</div>
  </section>`;
}
function render(animateNav=false){nav(animateNav);page.innerHTML=`<div class="page-view">${({home,sources,nodes,connections,reach:reachable,ip:ipPage,settings}[route])()}</div>`;if($('#sort'))$('#sort').value=order;paint();}
function navigate(next){if(next===route)return;route=next;checksOpen=diagnosticRoute();menuClose();render(true);$('h1',page)?.focus({preventScroll:true});window.scrollTo(0,0);}
function paint(){
  paintUpdate();
  if(!snapshot)return;
  const online=snapshot.online, t=snapshot.traffic;
  const starting=working==='start', status=starting?'starting':online?'online':'offline', statusText=starting?'启动中…':online?'已连接':'未连接';
  $('#traffic').dataset.status=status;$('#traffic-state').textContent=statusText;
  for(const [id,value] of [['traffic-download',bytes(t.rateDown)+'/s'],['traffic-upload',bytes(t.rateUp)+'/s'],['traffic-down-total',bytes(t.down)],['traffic-up-total',bytes(t.up)]]) $('#'+id).textContent=value;
  if(route==='home') {
    $('#session').dataset.status=status;$('#session-title').textContent=statusText;
    $('#session-node').textContent=active()?active().name+' · '+(snapshot.engine||active().engine):'未选择节点';$('#session-warning').textContent=snapshot.warning||(mode==='tun'&&!online?'TUN 为实验功能；仅支持 Mihomo，需以管理员身份启动客户端，并使用官方固定版核心。':active()&&!active().ready?'内置内核文件缺失，请重新安装或完整解压流枢。':'');
    const local=$('#local-proxy'), port=online&&Number.isInteger(snapshot.port)&&snapshot.port>=1&&snapshot.port<=65535?snapshot.port:null;
    local.hidden=!port;
    if(port){const address=`127.0.0.1:${port}`;$('span',local).textContent=address;local.setAttribute('aria-label',`复制本机代理地址 ${address}`);}
    $('#session-warning').hidden=!$('#session-warning').textContent;
    $('#power-label').textContent=starting?'启动中…':working==='stop'?'关闭中…':online?'关闭':'启动';$('#power').disabled=!!pending||(!online&&(!active()?.ready||(mode===`tun`&&(!snapshot.canTun||active().engine!==`Mihomo`))));
    all('[data-mode]').forEach(b=>{b.setAttribute('aria-pressed',b.dataset.mode===(online?snapshot.mode:mode));b.disabled=online||!!pending;});
  }
  if(route==='sources')all('[data-source]',page).forEach(card=>{
    const selected=card.dataset.source===snapshot.source;
    card.classList.toggle('source-active',selected);
    $('.source-select',card).setAttribute('aria-pressed',String(selected));
    $('[data-source-current]',card).hidden=!selected;
    $('[data-source-available]',card).hidden=selected;
  });
  if(route==='nodes')paintNodes();
  if(route==='settings'){const select=$('#close-behavior');select.disabled=!!pending;if(!pending)select.value=snapshot.closeToTray?'tray':'exit';}
  if(route==='connections'){
    const c=snapshot.connections;for(const k of ['total','direct','routed'])$('#metric-'+k).textContent=c[k];
    const tbody=$('#connection-rows'), rows=c.rows.map(r=>`<tr><td>${html(r.host)}</td><td>${badge(r.route,r.route==='直连'?'green':'')}</td><td>${html(r.rule)}</td></tr>`).join('');if(tbody.innerHTML!==rows)tbody.innerHTML=rows;
    $('#connection-table').hidden=!c.rows.length;$('#connection-empty').hidden=!!c.rows.length;
  }
  if($('#check-action')){
    $('#check-action').disabled=!online||!!pending;
    $('#check-context').textContent=online&&active()?active().name+' · '+(snapshot.engine||active().engine):'请先在首页启动代理';
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
  $('#latency-label').textContent=run?'测速中':'测延迟';
  $('#sort').disabled=!!run;
  list.setAttribute('aria-busy',String(!!run));
  progress.parentElement.hidden=!run;
  if(run?.ids){progress.max=run.ids.size;progress.value=run.results.size;}else progress.removeAttribute('value');
  const count=snapshot.nodes?.length||0;
  $('#node-count').textContent=run?.ids?`已测 ${run.results.size} / ${run.ids.size}`:run?'准备测速…':`${count} 个节点`;
  const status=$('#latency-notice');
  const message=!run&&error?error.text:'';
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
    if(value.textContent!==text)value.textContent=text;
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
  check.disabled=update.checking;check.textContent=update.checking?'检查中…':'检查更新';
  offer.hidden=!update.offer;
  const text=update.text||(update.offer?`可更新至 ${update.offer.version}`:'');
  status.textContent=text;status.hidden=!text;
}
function showUpdate(){
  const dialog=$('#update-dialog');
  if(!update.offer||dialog.open)return;
  if($('#editor').open){update.promptAfterEditor=true;return;}
  update.promptAfterEditor=false;
  menuClose();$('#update-error').hidden=true;
  $('#update-description').textContent=`当前版本 ${snapshot?.appVersion||'—'} → 新版本 ${update.offer.version}`;
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
  }catch(e){const el=$('#update-error');el.textContent=e.message;el.hidden=false;}
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
  menu.innerHTML=item('更新','refresh','refresh',source.refreshable?'':'disabled')+item('编辑名称','rename','edit')+item('替换订阅链接','replace-url','link')+item('移除','remove','trash');
  menu.hidden=false;menu.style.left=Math.max(4,Math.min(x,innerWidth-menu.offsetWidth-4))+'px';menu.style.top=Math.max(4,Math.min(y,innerHeight-menu.offsetHeight-4))+'px';
  $('.menu-item:not(:disabled)',menu)?.focus({preventScroll:true});
}
function edit(title,label,value,action){editor=action;$('#editor-title').textContent=title;$('#editor-label').textContent=label;$('#editor-input').value=value;$('#editor').showModal();$('#editor-input').focus();}
async function mutate(a,args={},visual=true){
  if(pending)return;
  const testing=a==='delay';
  if(testing){latencyRun={source:snapshot.source,ids:null,results:new Map()};latencyError=null;}
  pending++;working=a;paint();
  const b=$(`[data-act="${a}"]`);
  if(b&&!['select-node','select-source','delay'].includes(a))b.disabled=true;
  try {
    if(polling)await polling;
    const result=await nativeCall(a,args,testing?delayProgress:undefined);
    if(testing){
      const count=latencyRun.ids?.size||0,total=snapshot.nodes.length;
      if(count>0)toast(count<total?`本轮完成 ${count} / ${total} 个节点测速`:`已完成 ${count} 个节点测速`);
      latencyRun=null;
    }
    if(result?.traffic){const changed=result.node!==snapshot?.node||result.online!==snapshot?.online||result.source!==snapshot?.source;snapshot=result;if(changed){reach.clear();ip=null;}if(visual||(a==='select-source'&&(route==='nodes'||diagnosticRoute())))render();else{paint();if(testing&&route==='nodes'&&order==='delay')reorderNodeRows();}}
    return result;
  }catch(e){
    if(testing)latencyError={source:latencyRun?.source??snapshot.source,text:`测速未完成：${e.message}`};
    if(!testing||route!=='nodes')toast(e.message);
    return null;
  }finally{
    if(testing)latencyRun=null;
    pending--;working='';
    if(b?.isConnected&&!['power','select-node','select-source','delay'].includes(a))b.disabled=false;
    paint();
  }
}
render();
$('#editor').addEventListener('close',()=>{if(update.promptAfterEditor)showUpdate();});
document.addEventListener('contextmenu',e=>{const card=e.target.closest('[data-source]');if(card){e.preventDefault();menuOpen(card.dataset.source,e.clientX,e.clientY);}});
document.addEventListener('keydown',e=>{
  if(e.key!=='Escape')return;
  if(menuId){const trigger=all('.source-menu-trigger').find(b=>b.dataset.id===menuId);menuClose();trigger?.focus();}
  else if(checksOpen&&e.target.closest('#nav-group')){checksOpen=false;paintNav(true);$('#checks-toggle').focus();}
});
document.addEventListener('click', async e=>{
  const r=e.target.closest('[data-route]');if(r){e.preventDefault();navigate(r.dataset.route);return;}
  if(e.target.closest('.brand')){e.preventDefault();navigate('home');return;}
  const b=e.target.closest('[data-act]');const id=b?.dataset.id||menuId;
  if(b?.dataset.act!=='source-menu')menuClose();if(!b||b.disabled)return;
  const a=b.dataset.act;
  if(a==='checks-toggle'){checksOpen=!checksOpen;paintNav(true);return;}
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
  if(a==='remove'){if(confirm('移除此订阅及其节点？'))await mutate(a,{id});return;}
  if(a==='source-menu'){const box=b.getBoundingClientRect();menuOpen(id,box.left,box.bottom);return;}
  if(a==='select-node')return mutate(a,{id},false);
  if(a==='select-source'){if(id!==snapshot?.source)return mutate(a,{id},false);return;}
  if(a==='refresh')return mutate(a,{id});
  if(a==='delay')return edit('测延迟','检测地址','https://www.gstatic.com/generate_204',v=>mutate(a,{url:address(v)||v},false));
  if(a==='theme'){const theme=b.dataset.value;document.documentElement.dataset.theme=theme;store('theme',theme);all('[data-act=theme]').forEach(x=>x.setAttribute('aria-pressed',String(x===b)));return;}
  if(a==='site-remove'){const [url]=sites.splice(+b.dataset.index,1);reach.delete(url);store('sites',sites);render();return;}
  if(a==='reach'){const list=targets().map(x=>x[1]);const res=await mutate('check',{urls:list},false);if(res){reach=new Map(list.map((u,i)=>[u,res[i]]));if(route==='reach')render();}return;}
  if(a==='ip'){const res=await mutate('ip',{},false);if(res){ip=res;if(route==='ip')render();}return;}
  if(a==='autostart')return mutate(a,{value:snapshot?.autoStart?'off':'on'});
  if(a==='import-file')return mutate(a);
  if(a==='app-check')return checkUpdate();
  if(a==='app-offer')return showUpdate();
  if(a==='app-later'){$('#update-dialog').close();return;}
  if(a==='app-download')return openUpdate(b);
});
document.addEventListener('change',e=>{if(e.target.id==='close-behavior'){void mutate('close-behavior',{value:e.target.value},false);return;}if(e.target.id==='preferred'){void mutate('preferred',{value:e.target.value});return;}if(e.target.id==='sort'){order=e.target.value;reorderNodeRows();}});
document.addEventListener('submit',e=>{
  if(e.target.id==='editor-form'){e.preventDefault();const fn=editor,v=$('#editor-input').value.trim();if(v){$('#editor').close();fn?.(v);}return;}
  if(e.target.id!=='site-form')return;e.preventDefault();const u=address($('#site-url').value);if(!u||targets().some(x=>x[1]===u)||sites.length>=20){toast('请输入新的有效网址（最多 20 个自定义站点）。');return;}sites.push(u);store('sites',sites);render();$('#site-url').focus();
});
async function poll(){
  if(!pending){polling=nativeCall('snapshot').then(s=>{
    const previous=snapshot;snapshot=s;document.body.dataset.ready='yes';
    if(previous&&(previous.online!==s.online||previous.node!==s.node||previous.engine!==s.engine)){reach.clear();ip=null;if(['reach','ip'].includes(route))render();}
    if(!previous||(route==='settings'&&previous.online!==s.online))render();else paint();
    if(!update.started){update.started=true;void checkUpdate(true);}
  }).catch(e=>{if(!snapshot)toast(e.message);});await polling;polling=null;}
  setTimeout(poll,1000);
}
poll();