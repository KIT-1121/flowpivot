'use strict';
// Renderer-only QA. Its bridge fixture is not an engine/network test.
const assert = require('node:assert/strict');
const {pathToFileURL} = require('node:url');
const path = require('node:path');
const {chromium} = require(process.env.FLOWPIVOT_PLAYWRIGHT || 'playwright');
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:process.env.FLOWPIVOT_BROWSER});
  const out=process.env.FLOWPIVOT_SCREENSHOTS;
  try {
    const page=await browser.newPage({viewport:{width:1200,height:790}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(()=>{
      const state={appVersion:'0.2.0-alpha.22',localProxyPort:7897,online:false,closeToTray:true,delayUrl:'https://www.gstatic.com/generate_204',mode:'local',cores:['Mihomo','Xray','sing-box','V2Fly'].map(id=>({id,version:'v1',available:'v1'})),source:'a',node:'a4',preferred:'Xray',sources:[{id:'a',name:'测试订阅',format:'Mihomo',count:2,refreshable:true,yaml:true,quota:{total:107374182400,used:26843545600,remaining:80530636800}},{id:'b',name:'另一个订阅',format:'Xray',count:1,refreshable:true}],nodes:[{id:'a4',name:'甲',engine:'Mihomo',kind:'http',ready:true,delay:80},{id:'a5',name:'乙',engine:'Mihomo',kind:'socks5',ready:true,delay:30}],traffic:{up:0,down:0,rateUp:0,rateDown:0},connections:{total:0,direct:0,routed:0,rows:[]}};
      let reply;window.updateChecks=0;window.openDownloads=[];window.clipboard=[];window.sourceCalls=0;
      window.chrome ||= {};
      window.chrome.webview={addEventListener(k,f){if(k==='message')reply=f;},postMessage({id,action,args}){
        if(action==='language')window.nativeLanguage=args.value;
        if(action.startsWith('logs-')){
          const fallback=String.raw`C:\Test\FlowPivot\logs`;
          if(action==='logs-choose')window.chosenLogs=String.raw`D:\测试 <日志>`;
          if(action==='logs-default')window.chosenLogs=null;
          const folder=action==='logs-folder';if(folder)window.openedLogDir=window.chosenLogs||fallback;
          reply({data:{id,ok:true,body:folder?null:{directory:window.chosenLogs||fallback,isDefault:!window.chosenLogs,file:'FlowPivot-test.log',text:'<script>敏感字符测试</script>  应用启动',warning:''}}});
          return;
        }
        if(action==='app-check'){
          window.updateChecks++;
          const fail=window.updateCheckFails;
          setTimeout(()=>reply({data:{id,ok:!fail,body:fail?'连不上 GitHub，暂时无法检查更新。':{version:'0.2.0-alpha.23'}}}),60);
          return;
        }
        if(action==='app-download'){
          window.openDownloads.push(args.version);
          queueMicrotask(()=>reply({data:{id,ok:true,body:null}}));return;
        }
        if(action==='select-source'){
          window.sourceCalls++;state.source=args.id;state.node=state.nodes[0]?.id||null;
        }
        if(action==='delay-url')state.delayUrl=args.value||'https://www.gstatic.com/generate_204';
        if(action==='local-proxy-port')state.localProxyPort=Number(args.value);
        if(action==='source-export'||action==='source-yaml'||action==='source-override'){
          const body=action==='source-export'?true:action==='source-yaml'?'proxies:\n  - {name: Fixture, type: http, server: example.invalid, port: 80}\n':{text:''};
          window.lastSource={action,id:args.id};queueMicrotask(()=>reply({data:{id,ok:true,body}}));return;
        }
        if(action==='source-yaml-save'||action==='source-override-save')window.savedConfig={action,...args};
        if(action==='connection-close'){
          window.closedConnection=args.id;
          state.connections={total:1,direct:1,routed:0,rows:state.connections.rows.filter(x=>x.id!==args.id)};
        }
        if(action==='domain-route'){
          window.lastDomainRule=args;
          state.domainRoutes||=[];
          const index=state.domainRoutes.findIndex(r=>r.id===args.id);
          if(args.operation==='delete')state.domainRoutes.splice(index,1);
          else {
            const rule={id:args.id||String(Date.now()),kind:args.kind,pattern:args.pattern,value:args.value};
            if(index===-1)state.domainRoutes.push(rule);
            else state.domainRoutes[index]=rule;
          }
        }
        window.seedConnections=()=>{state.connections={total:2,direct:1,routed:1,rows:[{id:'one',target:'one.invalid',host:'one.invalid',route:'直连',rule:'IPCIDR',network:'tcp',up:2048,down:5000},{id:'two',target:'two.invalid',host:'two.invalid',route:'规则代理',rule:'MATCH',network:'udp',up:65536,down:1000}]};};
        if(action==='copy-proxy'){
          const address='127.0.0.1:'+state.port;
          window.clipboard.push(address);
          queueMicrotask(()=>reply({data:{id,ok:true,body:address}}));return;
        }
        if(action==='delay'){
          window.usedDelay={saved:state.delayUrl,args};
          window.measure={
            batch:()=>reply({data:{id,progress:{ids:['a4','a5']}}}),
            sample:(node,delay)=>{state.nodes.find(n=>n.id===node).delay=delay;reply({data:{id,progress:{id:node,delay}}});},
            end:()=>reply({data:{id,ok:true,body:structuredClone(state)}}),
            fail:()=>reply({data:{id,ok:false,body:'测试错误'}}),
          };
          return;
        }
        if(action==='close-behavior')state.closeToTray=args.value==='tray';
        if(action==='refresh-interval'){
          const source=state.sources.find(s=>s.id===args.id);
          source.refreshHours=Number(args.value);source.nextRefresh=source.refreshHours?new Date(Date.now()+source.refreshHours*3600000).toISOString():null;
        }
        const disconnect=()=>{
          state.online=false;state.port=0;
          state.subscriptionDisconnectRevision=(state.subscriptionDisconnectRevision||0)+1;
        };
        if(action==='refresh'){
          state.subscriptionRevision=(state.subscriptionRevision||0)+1;
          if(state.online&&window.changeCurrentNode)disconnect();
        }
        window.simulateAutoRefresh=(changed=false)=>{
          state.subscriptionRevision=(state.subscriptionRevision||0)+1;
          state.sources[0].quota.used=53687091200;
          if(state.online&&changed)disconnect();
        };
        if(action==='rename')state.sources.find(s=>s.id===args.id).name=args.name;
        window.emptyFixture=()=>{state.sources=[];state.source=state.node=null;state.nodes=[];};
        if(action==='import'||action==='import-file'){
          window.importFixture={
            reading:()=>reply({data:{id,progress:{stage:'reading'}}}),
            finish:()=>{
              const source={id:'test'+state.sources.length,name:'导入样本',format:'Mihomo',count:1,refreshable:false};
              if(!state.sources.length){state.source=source.id;state.node='one';state.nodes=[{id:'one',name:'测试节点',engine:'Mihomo',kind:'http',ready:true}];}
              state.sources.push(source);reply({data:{id,ok:true,body:structuredClone(state)}});
            },
            fail:()=>reply({data:{id,ok:false,body:'服务端：HTTP 403，访问被拒绝，请在服务商后台检查权限或更新订阅链接。'}}),
            cancel:()=>reply({data:{id,ok:true,body:null}}),
          };return;
        }
        if(action==='select-node'&&state.online){
          window.finishSwitch=()=>{state.node=args.id;reply({data:{id,ok:true,body:structuredClone(state)}});};return;
        }
        if(action==='select-node')state.node=args.id;
        if(action==='start'){
          const start=()=>{state.online=true;state.mode=args.mode;state.port=state.localProxyPort;};
          if(window.holdStart){
            window.startFixture={finish:()=>{start();reply({data:{id,ok:true,body:structuredClone(state)}});},fail:()=>reply({data:{id,ok:false,body:'测试启动失败'}})};
            return;
          }
          start();
        }
        if(action==='stop')state.online=false;
        const body=action==='check'?args.urls.map(()=>({outcome:'已响应',status:200,ms:55})):structuredClone(state);
        queueMicrotask(()=>reply({data:{id,ok:true,body}}));
      }};
    });
    await page.goto(pathToFileURL(path.join(process.argv[2],'web','index.html')).href);
    await page.waitForSelector('body[data-ready=yes]');
    assert.equal(await page.locator('html').getAttribute('data-theme'),'paper');
    const go = async k=>{
      if(['reach','ip'].includes(k)&&await page.locator('#checks-toggle').getAttribute('aria-expanded')==='false')await page.locator('#checks-toggle').click();
      await page.locator(`[data-route=${k}]`).click();
    };
    const update=page.getByRole('dialog',{name:'发现新版本'});
    await update.waitFor();
    assert.equal(await page.evaluate(()=>window.updateChecks),1);
    assert.match(await update.locator('#update-description').textContent(),/0\.2\.0-alpha\.22/);
    await update.getByRole('button',{name:'暂不更新'}).click();
    await update.waitFor({state:'hidden'});
    assert.deepEqual(await page.evaluate(()=>window.openDownloads),[]);
    await page.setViewportSize({width:1146,height:756});
    await go('reach');
    // Sample the recording-size viewport during the page transition as well as at rest.
    const heights=await page.evaluate(async()=>{
      document.querySelector('[data-route=settings]').click();
      const frames=[];
      do {
        frames.push(document.scrollingElement.scrollHeight);
        await new Promise(requestAnimationFrame);
      } while(document.querySelector('.page-view').getAnimations({subtree:true}).some(a=>a.playState==='running'));
      frames.push(document.scrollingElement.scrollHeight);
      return frames;
    });
    assert.ok(heights.every(h=>h<=756),`settings scrolls at recording size: ${Math.max(...heights)}px`);
    const corePositions=await page.locator('.core-row').evaluateAll(rows=>rows.map(row=>({x:row.offsetLeft,y:row.offsetTop})));
    assert.equal(corePositions.length,4);
    assert.ok(corePositions.every((position,i)=>position.y===corePositions[0].y&&(!i||position.x>corePositions[i-1].x)),'desktop core versions fit on one row');
    assert.equal(await page.locator('.core-panel button').count(),0);
    assert.equal(await page.locator('.core-row [data-bundled=true]').count(),4);
    await page.getByRole('button',{name:'查看日志'}).click();
    const logs=page.getByRole('dialog',{name:'运行日志'});
    await logs.getByText('FlowPivot-test.log').waitFor();
    assert.ok((await logs.locator('#logs-text').textContent()).includes('<script>'));
    assert.equal(await logs.locator('#logs-text script').count(),0);
    assert.equal(await logs.getByRole('button',{name:'恢复默认'}).count(),0);
    await logs.getByRole('button',{name:'更换目录'}).click();
    await logs.getByText(String.raw`D:\测试 <日志>`).waitFor();
    await logs.getByRole('button',{name:'打开目录'}).click();
    assert.equal(await page.evaluate(()=>window.openedLogDir),String.raw`D:\测试 <日志>`);
    await logs.getByRole('button',{name:'恢复默认'}).click();
    await logs.getByText(String.raw`C:\Test\FlowPivot\logs`).waitFor();
    if(out)await page.screenshot({path:path.join(out,'logs-settings.png'),animations:'disabled'});
    await page.keyboard.press('Escape');await logs.waitFor({state:'hidden'});
    const closeBehavior=page.getByLabel('关闭窗口时');
    assert.equal(await closeBehavior.inputValue(),'tray');
    await closeBehavior.selectOption('exit');
    await closeBehavior.selectOption('tray');
    await go('home');await go('settings');
    assert.equal(await closeBehavior.inputValue(),'tray');
    await closeBehavior.selectOption('exit');
    await go('home');await go('settings');
    assert.equal(await closeBehavior.inputValue(),'exit');
    await page.getByRole('button',{name:'查看新版'}).click();
    await update.getByRole('button',{name:'更新',exact:true}).click();
    await update.waitFor({state:'hidden'});
    assert.deepEqual(await page.evaluate(()=>window.openDownloads),['0.2.0-alpha.23']);
    await page.evaluate(()=>{window.updateCheckFails=true;});
    await page.getByRole('button',{name:'检查更新'}).click();
    await page.getByText('连不上 GitHub，暂时无法检查更新。').waitFor();
    assert.equal(await update.isHidden(),true);
    await go('home');
    assert.equal(await page.locator('[data-route=home]').evaluate(el=>getComputedStyle(el).display),'flex');
    assert.equal(await page.locator('[data-route=home] use').getAttribute('href'),'#icon-home');
    assert.equal(await page.locator('.route-diagram > svg').count(),1);
    for(const selector of ['.page-head','.route-diagram','#traffic','.traffic-table tbody']){
      assert.equal(await page.locator(selector).evaluate(el=>getComputedStyle(el).borderTopWidth),'0px');
    }
    assert.equal(await page.locator('.brand img').evaluate(e=>e.complete&&e.naturalWidth>0),true);
    assert.equal(await page.locator('.orbit-brand').getAttribute('href'),'brand.svg');
    assert.equal(await page.locator('.orbit-flow').first().evaluate(e=>getComputedStyle(e).animationName),'none');
    const staticNav=async selected=>{
      const nav=await page.evaluate(()=>{
        const s=document.querySelector('.side'), active=[...document.querySelectorAll('#nav [aria-current=page]')].filter(e=>!e.closest('[inert]'));
        const style=getComputedStyle(s), bounds=s.getBoundingClientRect();
        const current=active[0]&&getComputedStyle(active[0]);
        return {current:active[0]?.dataset.route||active[0]?.id,count:active.length,glass:document.querySelectorAll('#nav-glass,.nav-glass-layer').length,animations:document.querySelector('#nav').getAnimations({subtree:true}).length,
          left:bounds.left,top:bounds.top,corner:style.borderRadius,border:style.borderLeftWidth,blur:style.backdropFilter,shadow:style.boxShadow,
          fill:current?.backgroundColor,background:style.backgroundColor};
      });
      assert.equal(nav.current,selected);
      assert.equal(nav.count,1);
      assert.equal(nav.glass,0);
      assert.equal(nav.animations,0);
      assert.equal(nav.left,0);assert.equal(nav.top,0);
      assert.equal(nav.corner,'0px');assert.equal(nav.border,'0px');assert.equal(nav.blur,'none');assert.equal(nav.shadow,'none');
      assert.notEqual(nav.fill,nav.background);
    };
    await staticNav('home');
    const navElement=await page.locator('.nav-stage').elementHandle();
    await page.locator('[data-route=sources]').evaluate(el=>el.click());
    await staticNav('sources');
    assert.ok(await navElement.evaluate(e=>e===document.querySelector('.nav-stage')));
    await go('ip');await staticNav('ip');
    assert.equal(await page.locator('#checks-subnav .sub-link use').count(),2);
    assert.equal(await page.locator('.subnav-inner').evaluate(el=>getComputedStyle(el).borderLeftWidth),'0px');
    await page.locator('#checks-toggle').click();await staticNav('checks-toggle');
    await go('home');await staticNav('home');
    await go('settings');await page.locator('#theme').selectOption('glass');await go('home');
    await page.mouse.move(500,240);
    await page.waitForFunction(()=>document.querySelector('#pointer-glow').dataset.on==='yes'&&document.querySelector('#pointer-glow').style.getPropertyValue('--glow-x')==='500px');
    assert.equal(await page.locator('#pointer-glow').evaluate(el=>getComputedStyle(el).pointerEvents),'none');
    await page.mouse.move(100,200);
    await page.waitForFunction(()=>!document.querySelector('#pointer-glow').dataset.on);
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.waitForFunction(()=>matchMedia('(prefers-reduced-motion: reduce)').matches);
    await go('sources');await staticNav('sources');
    await page.mouse.move(500,240);assert.equal(await page.locator('#pointer-glow').isHidden(),true);
    await page.emulateMedia({reducedMotion:'no-preference'});
    await go('sources');const cardFoot=await page.locator('.source-state').first().boundingBox();
    assert.ok(cardFoot);await page.mouse.click(cardFoot.x+cardFoot.width/2,cardFoot.y+cardFoot.height/2,{button:'right'});
    await page.getByRole('menuitem',{name:'替换订阅链接'}).waitFor();
    await page.keyboard.press('Escape');
    assert.equal(await page.locator('#source-menu').isHidden(),true);
    const original=p=>p.evaluate(()=>{
      const box=e=>JSON.stringify(e.getBoundingClientRect());
      window.oldSourceGrid=document.querySelector('.source-grid');
      window.oldCards=[...document.querySelectorAll('.source-card')];
      window.oldCardBoxes=window.oldCards.map(box);
      window.oldHeader=document.querySelector('.page-head');
      window.oldScroll=scrollY;
    });
    await page.locator('.source-grid').evaluate(el=>Promise.all(el.getAnimations().map(a=>a.finished)));
    await original(page);
    const cardB=page.locator('[data-source=b] .source-state');
    await cardB.click({force:true});
    await page.locator('[data-source=b].source-active .source-select[aria-pressed=true]').waitFor();
    assert.equal(await page.evaluate(()=>window.oldSourceGrid===document.querySelector('.source-grid')&&window.oldHeader===document.querySelector('.page-head')&&window.oldCards.every((el,i)=>el===document.querySelectorAll('.source-card')[i]&&JSON.stringify(el.getBoundingClientRect())===window.oldCardBoxes[i])&&window.oldScroll===scrollY),true);
    await cardB.click({force:true});assert.equal(await page.evaluate(()=>window.sourceCalls),1);
    await page.locator('[data-source=a] .source-state').click({force:true});
    await page.locator('[data-source=a].source-active').waitFor();
    assert.match(await page.locator('[data-source=a] .source-quota').textContent(),/总流量100(?:\.0)? GB.*已用25(?:\.0)? GB.*剩余75(?:\.0)? GB/s);
    await page.getByText('暂无套餐流量 · 可尝试更新',{exact:true}).waitFor();
    if(out){
      if(await page.locator('#checks-toggle').getAttribute('aria-expanded')==='false')await page.locator('#checks-toggle').click();
      await page.locator('[data-source=a].source-active').waitFor();
      await page.mouse.move(800,600);
      await page.locator('#toast').evaluate(e=>e.classList.remove('show','visible'));
      await page.screenshot({path:path.join(out,'subscriptions.png'),animations:'disabled'});
    }
    const sourceAction=async action=>{await page.locator('[data-source=a] .source-menu-trigger').click();await page.locator('#source-menu [data-act="'+action+'"]').click();};
    await sourceAction('refresh-interval');
    assert.equal(await page.locator('#editor-input').inputValue(),'0');
    await page.locator('#editor-input').fill('12');
    await page.locator('#editor-form button[type=submit]').click();
    await page.getByText('已保存自动更新间隔',{exact:true}).waitFor();
    await page.getByText(/自动更新：每 12 小时/).waitFor();
    await page.evaluate(()=>window.simulateAutoRefresh());
    await page.locator('[data-source=a] .source-quota').getByText('50 GB',{exact:true}).first().waitFor();
    await sourceAction('source-export');
    await page.getByText('YAML 已导出',{exact:true}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.lastSource),{action:'source-export',id:'a'});
    await sourceAction('source-edit');
    await page.locator('#config-editor[open]').waitFor();
    assert.match(await page.locator('#config-text').inputValue(),/^proxies:/);
    await page.locator('#config-save').click();
    await page.locator('#config-editor[open]').waitFor({state:'hidden'});
    assert.equal(await page.evaluate(()=>window.savedConfig.action),'source-yaml-save');
    await sourceAction('source-patch');
    await page.locator('#config-text').fill('{"exclude":["Fixture"]}');
    await page.locator('#config-save').click();
    await page.locator('#config-editor[open]').waitFor({state:'hidden'});
    assert.equal(await page.evaluate(()=>window.savedConfig.action),'source-override-save');
    await go('settings');
    await page.locator('#delay-url').fill('http://example.invalid/');
    await page.locator('#delay-settings button').click();
    await page.getByText('已保存延迟检测地址',{exact:true}).waitFor();
    const portField=page.locator('#proxy-port'), portSave=page.locator('#proxy-port-settings button');
    assert.equal(await portField.inputValue(),'7897');
    await portField.fill('65536');await portSave.click();
    await page.getByText('请输入 1–65535 之间的整数端口。',{exact:true}).waitFor();
    await portField.fill('51234');await portSave.click();
    await page.getByText('已保存本机代理端口',{exact:true}).waitFor();
    await go('nodes');
    await page.locator('[data-id=a5]').evaluate(e=>e.dataset.identity='original');
    await page.locator('[data-id=a5]').click();
    await page.waitForSelector('[data-id=a5][aria-pressed=true]');
    assert.equal(await page.locator('[data-id=a5]').getAttribute('data-identity'),'original');
    assert.equal(await page.locator('#sort').inputValue(),'delay','the default order is increasing latency');
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a5');
    await page.locator('#sort').selectOption('original');
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a4');
    await page.locator('#sort').selectOption('delay');
    const checkSourceBadge=async expected=>{
      const badge=page.locator('.page-head > .node-source-badge');
      assert.equal(await badge.textContent(),expected);
      return badge.evaluate(el=>{
        const range=document.createRange();
        range.selectNodeContents(el);
        const box=el.getBoundingClientRect();
        const header=el.parentElement.getBoundingClientRect();
        const heading=el.previousElementSibling.getBoundingClientRect();
        const rects=[...range.getClientRects()];
        return {
          lines:rects.length,
          fits:heading.right<=box.left&&box.right<=header.right+.5&&
            rects.every(r=>r.left>=box.left-.5&&r.right<=box.right+.5&&r.top>=box.top-.5&&r.bottom<=box.bottom+.5)
        };
      });
    };
    assert.deepEqual(await checkSourceBadge('测试订阅'),{lines:1,fits:true});
    const longName='完整订阅名称 · '+('长名称Fixture0123456789'.repeat(3));
    await go('sources');
    await sourceAction('rename');
    await page.locator('#editor-input').fill(longName);
    await page.locator('#editor button[type=submit]').click();
    await page.getByRole('button',{name:longName,exact:true}).waitFor();
    await go('nodes');
    const size=page.viewportSize();
    for(const width of [430,1146]){
      await page.setViewportSize({width,height:756});
      const badge=await checkSourceBadge(longName);
      assert.ok(badge.fits&&badge.lines>0,`subscription name was clipped at ${width}px`);
      if(width===430)assert.ok(badge.lines>1,'long subscription name should wrap on narrow windows');
    }
    await page.setViewportSize(size);
    const startDelay=async()=>{
      await page.getByRole('button',{name:'测延迟',exact:true}).click();
      await page.getByText('准备测速…',{exact:true}).waitFor();
      await page.waitForFunction(()=>!!window.measure);
      assert.deepEqual(await page.evaluate(()=>window.usedDelay),{saved:'http://example.invalid/',args:{}});
      assert.equal(await page.locator('#editor').isHidden(),true);
    };
    await startDelay();
    await page.waitForFunction(()=>!!window.measure);
    await page.evaluate(()=>window.measure.batch());
    await page.getByText('已测 0 / 2',{exact:true}).waitFor();
    assert.equal(await page.locator('#node-list .latency-spinner:visible').count(),2);
    assert.equal(await page.locator('#sort').isDisabled(),true);
    await page.evaluate(()=>window.measure.sample('a4',19));
    await page.getByText('已测 1 / 2',{exact:true}).waitFor();
    assert.equal(await page.locator('#latency-progress').evaluate(p=>p.value),1);
    assert.equal(await page.locator('[data-id=a4] .latency-value').textContent(),'19 ms');
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a5');
    await go('sources');await go('nodes');
    await page.getByText('已测 1 / 2',{exact:true}).waitFor();
    await page.evaluate(()=>{window.measure.sample('a5','failed');window.measure.end();window.measure=null;});
    const doneToast=page.locator('#toast.show').getByText('已完成 2 个节点测速',{exact:true});
    await doneToast.waitFor();
    assert.equal(await page.locator('#latency-notice').isHidden(),true);
    assert.equal(await page.locator('#latency-progress').isHidden(),true);
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a4');
    assert.equal(await page.locator('[data-id=a5] .latency-value').textContent(),'失败');
    await doneToast.waitFor({state:'hidden',timeout:5000});
    assert.equal(await page.locator('#latency-notice').isHidden(),true);
    await startDelay();
    await page.waitForFunction(()=>!!window.measure);
    await page.evaluate(()=>window.measure.fail());
    await page.getByText('测速未完成：测试错误',{exact:true}).waitFor();
    assert.equal(await page.locator('#latency-progress').isHidden(),true);
    assert.equal(await page.getByRole('button',{name:'测延迟',exact:true}).isEnabled(),true);
    await go('home');assert.equal(await page.locator('#local-proxy').isHidden(),true);
    await page.locator('#power').click();await page.waitForFunction(()=>document.querySelector('#power').textContent==='关闭');
    const port=page.getByRole('button',{name:'复制本机代理地址 127.0.0.1:51234'});
    await port.click();
    await page.getByText('已复制 127.0.0.1:51234',{exact:true}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.clipboard),['127.0.0.1:51234']);
    await go('settings');
    assert.equal(await portField.inputValue(),'51234');
    assert.equal(await portField.isDisabled(),true);
    assert.equal(await portSave.isDisabled(),true);
    await go('home');
    await page.locator('#power').click();await port.waitFor({state:'hidden'});
    await page.locator('#power').click();await port.waitFor();
    await page.evaluate(()=>window.seedConnections());
    await go('connections');
    await page.locator('#connection-rows tr').last().waitFor();
    const checkToolbarRow=async()=>{
      const toolbar=page.locator('.connection-toolbar');
      const centers=await toolbar.evaluate(el=>[...el.children].map(node=>{
        const box=node.getBoundingClientRect();return box.top+box.height/2;
      }));
      assert.equal(centers.length,4);
      assert.ok(Math.max(...centers)-Math.min(...centers)<1,'all four closed toolbar controls fit in one row');
    };
    await page.setViewportSize({width:1146,height:756});
    await checkToolbarRow();
    assert.equal(await page.locator('#connection-rows [data-column=route] button, #route-editor, .target-rules').count(),0);
    const readOnlyRoute=page.locator('#connection-rows tr[data-id=two] [data-column=route]');
    assert.equal(await readOnlyRoute.textContent(),'规则代理');
    await readOnlyRoute.click();
    assert.equal(await page.locator('dialog[open]').count(),0);
    await page.getByRole('button',{name:'分流规则',exact:true}).click();
    const domain=page.locator('#domain-editor');
    await domain.getByText('暂无分流规则').waitFor();
    await domain.locator('#domain-pattern').fill('baidu');
    await domain.getByRole('button',{name:'添加规则'}).click();
    await domain.getByText('域名关键词 · 直连',{exact:true}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.lastDomainRule),{operation:'save',id:'',kind:'keyword',pattern:'baidu',value:'direct'});
    await domain.getByRole('button',{name:'编辑 baidu'}).click();
    assert.equal(await domain.locator('#domain-pattern').inputValue(),'baidu');
    await domain.locator('#domain-kind').selectOption('regex');
    const expression="(^|\\.)baidu\\.com$";
    await domain.locator('#domain-pattern').fill(expression);
    await domain.locator('#domain-value').selectOption('proxy');
    await domain.getByRole('button',{name:'保存',exact:true}).click();
    await domain.locator('#domain-list').getByText(expression,{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.lastDomainRule.value),'proxy');
    await domain.getByRole('button',{name:'删除 '+expression,exact:true}).click();
    await domain.getByText('暂无分流规则').waitFor();
    await domain.locator('#domain-kind').selectOption('ip');
    await domain.getByText('填写 IPv4 或 IPv6，不带端口。',{exact:true}).waitFor();
    await domain.locator('#domain-pattern').fill('192.0.2.10');
    await domain.getByRole('button',{name:'添加规则'}).click();
    await domain.getByText('IP 地址 · 直连',{exact:true}).waitFor();
    assert.deepEqual(await page.evaluate(()=>window.lastDomainRule),{operation:'save',id:'',kind:'ip',pattern:'192.0.2.10',value:'direct'});
    await domain.getByRole('button',{name:'完成',exact:true}).click();
    await page.evaluate(()=>window.seedConnections());
    await page.locator('#connection-rows tr[data-id=one]').waitFor();
    await page.locator('#connection-sort').selectOption('up');
    assert.equal(await page.locator('#connection-rows tr').first().getAttribute('data-id'),'two');
    await page.locator('.connection-columns summary').click();
    await page.locator('[data-connection-column=up]').check();
    assert.equal(await page.locator('th[data-column=up]').isVisible(),true);
    await page.locator('#connection-filter').selectOption('direct');
    assert.equal(await page.locator('#connection-rows tr').count(),1);
    await go('nodes');await go('connections');
    assert.equal(await page.locator('#connection-filter').inputValue(),'direct');
    assert.equal(await page.locator('th[data-column=up]').isVisible(),true);
    await page.locator('#connection-filter').selectOption('all');
    await page.locator('#connection-search').fill('two.invalid');
    await page.getByRole('button',{name:'断开 two.invalid'}).click();
    await page.getByText('没有匹配的连接',{exact:true}).waitFor();
    assert.equal(await page.evaluate(()=>window.closedConnection),'two');
    await page.locator('#connection-search').clear();
    await go('reach');await page.locator('#site-url').fill('test-site.invalid');await page.getByRole('button',{name:'添加网站'}).click();
    await page.getByRole('button',{name:'测连通性'}).click();
    await page.getByText('已响应 · 200').last().waitFor();
    for(const width of [430,600,850,1150,1280]){
      await page.setViewportSize({width,height:800});
      for(const route of ['home','sources','nodes','connections','reach','ip','settings']){
        await go(route);
        const overflow=await page.evaluate(()=>document.documentElement.scrollWidth>innerWidth || document.querySelector('main').scrollWidth>document.querySelector('main').clientWidth+1);
        assert.equal(overflow,false,`${route} overflows at ${width}px`);
        if(route==='settings'){
          const layout=await page.locator('#preferred').evaluate(select=>{
            const label=select.labels[0], title=label.closest('.row-main').getBoundingClientRect();
            return {width:title.width,select:select.getBoundingClientRect().width,height:label.getBoundingClientRect().height};
          });
          assert.ok(layout.width>=105 && layout.select<=172 && layout.height<=44,`preferred label squeezed at ${width}px: ${JSON.stringify(layout)}`);
          assert.equal(await page.locator('.core-row').count(),4);
        }
      }
    }
    await page.locator('#theme').selectOption('dark');
    assert.equal(await page.locator('html').getAttribute('data-theme'),'dark');
    await page.locator('#theme').selectOption('light');
    await page.setViewportSize({width:430,height:540});
    const narrowScroll=await page.evaluate(()=>{
      const doc=document.scrollingElement;
      const heights=[...document.querySelectorAll('.core-row')].map(el=>el.offsetTop);
      window.scrollTo(0,doc.scrollHeight);
      return {heights,scrolled:doc.scrollTop,content:doc.scrollHeight};
    });
    assert.ok(narrowScroll.content>540 && narrowScroll.scrolled>0,'short window must still scroll');
    assert.ok(narrowScroll.heights.every((h,i,all)=>i===0||h>all[i-1]),'narrow settings must stack cores');
    await page.evaluate(()=>window.scrollTo(0,0));
    await page.locator('#theme').selectOption('glass');
    // Exercise the entire animation, not just the final document geometry.
    for(const [width,height] of [[1146,756],[430,760]]){
      await page.setViewportSize({width,height});
      for(const source of ['ip','reach']){
        for(const target of ['settings','home','sources','nodes','connections']){
          await go(source);
          await page.waitForFunction(()=>!document.querySelector('#nav').getAnimations({subtree:true}).some(a=>a.playState==='running'));
          const result=await page.evaluate(async target=>{
            const nav=document.querySelector('#nav'), doc=document.scrollingElement;
            const frames=[];
            const sample=()=>({doc:doc.scrollHeight-doc.clientHeight,docX:doc.scrollWidth-doc.clientWidth,nav:nav.scrollHeight-nav.clientHeight,navX:nav.scrollWidth-nav.clientWidth});
            document.querySelector(`[data-route="${target}"]`).click();
            for(let i=0;i<90;i++){
              frames.push(sample());
              const moving=[...nav.getAnimations({subtree:true}),...document.querySelector('main').getAnimations({subtree:true})].some(a=>a.playState==='running'&&a.effect.getTiming().iterations!==Infinity);
              if(!moving&&i>1)break;
              await new Promise(requestAnimationFrame);
            }
            return {frames,final:sample(),expanded:document.querySelector('#checks-toggle').getAttribute('aria-expanded')};
          },target);
          const location=`${width}x${height} ${source} -> ${target}`;
          assert.ok(result.frames.every(f=>f.doc<=result.final.doc+1 && f.docX<=1 && f.navX<=1 && (width<761||f.nav<=result.final.nav+1)),`transient overflow ${location}: ${JSON.stringify(result)}`);
          assert.equal(result.expanded,'true',`submenu collapsed: ${location}`);
        }
      }
    }
    assert.equal(await page.locator('#checks-toggle').getAttribute('aria-expanded'),'true');
    await page.locator('#checks-toggle').click();
    assert.equal(await page.locator('#checks-toggle').getAttribute('aria-expanded'),'false');
    assert.equal(await page.locator('#nav [title], #nav a[href]').count(),0);
    await go('settings');
    await page.setViewportSize({width:1146,height:756});
    assert.equal(await page.locator('#theme').inputValue(),'glass');
    for(const theme of ['light','dark','mist','paper','ink','glass']){
      await page.locator('#theme').selectOption(theme);
      const palette=await page.evaluate(()=>{
        const get=s=>getComputedStyle(document.querySelector(s));
        return {sidebar:get('.side').backgroundColor,nav:get('.nav-link[aria-current=page]').backgroundImage,glow:get('#pointer-glow').display,body:get('body').backgroundImage};
      });
      assert.equal(palette.sidebar,{
        light:'rgb(234, 240, 240)',dark:'rgb(16, 25, 27)',mist:'rgb(220, 229, 235)',paper:'rgb(229, 220, 205)',ink:'rgb(27, 36, 49)',glass:'rgb(229, 233, 240)'
      }[theme]);
      assert.equal(palette.nav,'none');
      await staticNav('settings');
      const contrast=await page.evaluate(()=>{
        function lum(color){
          const rgb=color.match(/[\d.]+/g).slice(0,3).map(Number).map(v=>v/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);
          return .2126*rgb[0]+.7152*rgb[1]+.0722*rgb[2];
        }
        function ratio(a,b){let x=lum(a),y=lum(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05);}
        const side=getComputedStyle(document.querySelector('.side')), selected=getComputedStyle(document.querySelector('.nav-link[aria-current=page]')), inactive=getComputedStyle(document.querySelector('[data-route=home]'));
        const primary=getComputedStyle(document.documentElement);
        const mainText=primary.getPropertyValue('--text').trim(), muted=primary.getPropertyValue('--muted').trim(), surface=primary.getPropertyValue('--surface').trim();
        const normalize=hex=>'rgb('+hex.slice(1).match(/../g).map(c=>parseInt(c,16)).join(', ')+')';
        const safeHex=v=>/^#[0-9a-f]{6}$/i.test(v);
        return [ratio(selected.color,selected.backgroundColor),ratio(inactive.color,side.backgroundColor),
          ...([mainText,muted,surface].every(safeHex)?[ratio(normalize(mainText),normalize(surface)),ratio(normalize(muted),normalize(surface))]:[])];
      });
      assert.ok(contrast.every(x=>x>=4.5),theme+' nav contrast '+contrast);
      if(['mist','paper','ink'].includes(theme)){
        assert.equal(palette.nav,'none');assert.equal(palette.glow,'none');assert.equal(palette.body,'none');
      }
      const dropdown=page.locator('#close-behavior');
      assert.equal(await dropdown.evaluate(e=>getComputedStyle(e).appearance),'base-select');
      await dropdown.focus();await dropdown.press('Space');
      await page.locator('#close-behavior:open').waitFor();
      const picker=await dropdown.evaluate(e=>getComputedStyle(e,'::picker(select)').backgroundColor);
      assert.equal(picker,{glass:'rgb(250, 251, 254)',dark:'rgb(25, 36, 39)',light:'rgb(255, 255, 255)',mist:'rgb(242, 245, 247)',paper:'rgb(247, 242, 232)',ink:'rgb(32, 41, 54)'}[theme]);
      if(out&&theme==='glass')await page.screenshot({path:path.join(out,'dropdown.png'),animations:'disabled'});
      await dropdown.press('Escape');
      await page.locator('#close-behavior:not(:open)').waitFor();
      if(out&&theme!=='glass'){
        await page.evaluate(()=>document.activeElement?.blur());
        await page.screenshot({path:path.join(out,'theme-'+theme+'.png'),animations:'disabled'});
      }
      await page.setViewportSize({width:430,height:760});
      assert.equal(await page.evaluate(()=>document.scrollingElement.scrollWidth>innerWidth),false,`settings overflows at 430px in ${theme} theme`);
      await page.setViewportSize({width:1146,height:756});
    }
    const disclaimer=page.locator('#disclaimer-dialog');
    const disclaimerButton=page.getByRole('button',{name:'免责声明',exact:true});
    await disclaimerButton.click();
    await disclaimer.getByText(/不提供代理节点或订阅服务/).waitFor();
    await disclaimer.getByRole('button',{name:'完成',exact:true}).click();
    await disclaimer.waitFor({state:'hidden'});
    assert.equal(await disclaimerButton.evaluate(b=>b===document.activeElement),true);
    if(out){
      await page.setViewportSize({width:1150,height:760});
      await page.screenshot({path:path.join(out,'settings.png'),animations:'disabled'});
      await go('home');
      await page.mouse.move(900,385);
      await page.screenshot({path:path.join(out,'home.png'),animations:'disabled'});
      await go('nodes');await startDelay();await page.waitForFunction(()=>!!window.measure);
      await page.evaluate(()=>{window.measure.batch();window.measure.sample('a4',19);});
      await page.screenshot({path:path.join(out,'latency.png'),animations:'disabled'});
      await page.evaluate(()=>window.measure.end());
      await page.setViewportSize({width:430,height:760});await go('settings');
      await page.screenshot({path:path.join(out,'settings-narrow.png'),animations:'disabled'});
    }
    await page.locator('#theme').selectOption('mist');
    await page.reload();
    await page.waitForSelector('body[data-ready=yes]');
    assert.equal(await page.locator('html').getAttribute('data-theme'),'mist');
    assert.deepEqual(errors,[]);
    await update.waitFor();
    await update.getByRole('button',{name:'暂不更新'}).click();
    await go('sources');
    await page.locator('[data-source=a] .source-select').click();
    await go('nodes');
    const nodes=page.locator('#node-list button');
    await nodes.first().click();
    await page.locator('#node-list button[aria-pressed=true]').waitFor();
    await go('home');
    await page.locator('[data-mode=local]').click();
    await page.setViewportSize({width:1150,height:760});
    await go('settings');await page.locator('#theme').selectOption('glass');await go('home');
    const link=page.locator('.route-diagram');
    const power=page.locator('#power');
    const trafficTable=page.locator('.traffic-table');
    const offlineTable=await trafficTable.boundingBox();
    const stateStyle=(selector)=>page.locator(selector).evaluate(e=>{
      const dot=getComputedStyle(e.querySelector('.dot')), badge=getComputedStyle(e);
      return {width:dot.width,light:dot.backgroundColor,halo:dot.boxShadow,fill:badge.backgroundColor,color:badge.color};
    });
    const offlineStyle=await stateStyle('.traffic-state');
    assert.equal(offlineStyle.fill,'rgba(0, 0, 0, 0)');
    const animations=()=>link.evaluate(el=>el.getAnimations({subtree:true}).map(a=>a.animationName));
    if(out)await page.screenshot({path:path.join(out,'orbit-standby.png'),animations:'disabled'});
    await page.evaluate(()=>{window.holdStart=true;});
    await power.click();await page.locator('#session[data-status=starting]').waitFor();
    assert.equal((await stateStyle('.traffic-state')).light,'rgb(178, 107, 17)');
    assert.equal(await power.getAttribute('aria-busy'),'true');
    assert.equal(await power.isDisabled(),true);
    assert.ok((await animations()).includes('orbit-wave'));
    assert.ok((await animations()).includes('orbit-packet'));
    if(out){
      await link.evaluate(el=>el.getAnimations({subtree:true}).forEach(a=>{a.pause();a.currentTime=260;}));
      await page.screenshot({path:path.join(out,'orbit-starting.png')});
    }
    await page.waitForFunction(()=>!!window.startFixture);
    await page.evaluate(()=>window.startFixture.fail());
    await page.locator('#session[data-status=offline]').waitFor();
    await page.locator('#toast.show').filter({hasText:'测试启动失败'}).waitFor();
    assert.equal((await animations()).includes('orbit-packet'),false);
    assert.equal(await power.getAttribute('aria-busy'),'false');
    await page.evaluate(()=>{window.startFixture=null;});
    await power.click();await page.waitForFunction(()=>!!window.startFixture);
    await page.evaluate(()=>window.startFixture.finish());
    await page.locator('#session[data-status=online]').waitFor();
    assert.equal(await page.locator('#traffic-state').textContent(),'已连接');
    const onlineStyle=await stateStyle('.traffic-state');
    assert.equal(onlineStyle.width,'10px');
    assert.equal(onlineStyle.light,'rgb(0, 137, 79)');
    assert.equal(onlineStyle.fill,'rgb(217, 249, 232)');
    assert.notEqual(onlineStyle.halo,'none');
    assert.deepEqual(await stateStyle('.home-status'),onlineStyle);
    assert.deepEqual(await trafficTable.boundingBox(),offlineTable);
    assert.ok((await animations()).includes('orbit-lock'));
    assert.equal(await power.getAttribute('aria-busy'),'false');
    await page.locator('#local-proxy:not([hidden])').waitFor();
    if(out){
      await page.locator('#toast').evaluate(e=>e.classList.remove('show','visible'));
      await link.evaluate(el=>el.getAnimations({subtree:true}).filter(a=>a.animationName!=='orbit-lock'&&a.animationName!=='orbit-wave').forEach(a=>{a.pause();a.currentTime=630;}));
      await page.waitForTimeout(760);
      await page.screenshot({path:path.join(out,'orbit-connected.png')});
    }
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.waitForFunction(()=>matchMedia('(prefers-reduced-motion: reduce)').matches);
    assert.ok((await animations()).every(name=>!name||name==='none'));
    assert.ok(await link.locator('.orbit-flow').first().evaluate(e=>Number(getComputedStyle(e).opacity)>=.8));
    await page.setViewportSize({width:430,height:760});
    await page.locator('#local-proxy').scrollIntoViewIfNeeded();
    await page.waitForFunction(()=>{
      const panel=document.querySelector('.route-diagram'),control=panel.querySelector('#local-proxy');
      const b=panel.getBoundingClientRect(),r=control.getBoundingClientRect();
      return r.width>0&&r.height>0&&r.left>=b.left&&r.right<=b.right&&r.top>=b.top&&r.bottom<=b.bottom;
    },null,{timeout:2500});
    if(out)await page.screenshot({path:path.join(out,'orbit-mobile.png'),animations:'disabled'});
    await page.emulateMedia({reducedMotion:'no-preference'});
    await page.setViewportSize({width:1150,height:760});
    const disconnectNotice=page.locator('#subscription-disconnect');
    await go('sources');await sourceAction('refresh');
    await page.waitForFunction(()=>!document.querySelector('[data-act=import]').disabled);
    assert.equal(await page.getByText('订阅已更新',{exact:true}).isVisible(),false);
    assert.equal(await disconnectNotice.isHidden(),true);
    await go('home');
    assert.equal(await page.locator('#traffic-state').textContent(),'已连接');
    assert.equal(await page.getByText(/重新连接或手动选择节点后应用/).count(),0);
    await go('nodes');
    await page.locator('#node-list button[aria-pressed=false]').first().click();
    await page.getByText('正在切换节点…',{exact:true}).waitFor();
    await page.waitForFunction(()=>!!window.finishSwitch);
    await page.evaluate(()=>window.finishSwitch());
    await go('home');
    await page.locator('#session-title').filter({hasText:'已连接'}).waitFor();
    await page.evaluate(()=>{window.changeCurrentNode=true;});
    await go('sources');await sourceAction('refresh');
    await disconnectNotice.waitFor({state:'visible'});
    const shown=Date.now();
    assert.equal(await disconnectNotice.getAttribute('role'),'alert');
    assert.equal(await disconnectNotice.evaluate(el=>getComputedStyle(el).position),'fixed');
    assert.match(await disconnectNotice.textContent(),/代理已断开/);
    await go('nodes');
    assert.equal(await page.locator('#node-list').getByText(/代理已断开|重新连接或手动选择节点后应用/).count(),0);
    await disconnectNotice.waitFor({state:'hidden',timeout:4200});
    assert.ok(Date.now()-shown>=2600,'disconnect notice stays visible for about three seconds');
    await page.waitForTimeout(1400);
    assert.equal(await disconnectNotice.isHidden(),true);
    await go('home');
    await page.locator('#session-title').filter({hasText:'未连接'}).waitFor();
    await page.evaluate(()=>{window.holdStart=false;window.changeCurrentNode=false;});
    await page.locator('#power').click();
    await page.locator('#session[data-status=online]').waitFor();
    await page.evaluate(()=>window.simulateAutoRefresh(true));
    await disconnectNotice.waitFor({state:'visible'});
    await page.locator('#session-title').filter({hasText:'未连接'}).waitFor();
    await disconnectNotice.waitFor({state:'hidden',timeout:4200});
    await page.evaluate(()=>window.emptyFixture());
    await page.waitForTimeout(1200);
    await go('sources');
    await page.locator('[data-act=import]').click();
    await page.locator('#editor input').fill('https://example.invalid/sample');
    await page.locator('#editor button[type=submit]').click();
    await page.locator('.import-status').filter({hasText:'正在读取并识别订阅'}).waitFor();
    assert.equal(await page.locator('[data-act=import-file]').isDisabled(),true);
    await page.waitForFunction(()=>!!window.importFixture);
    await page.evaluate(()=>window.importFixture.finish());
    await page.locator('[data-source=test0].source-active').waitFor();
    await go('nodes');
    await page.getByText('测试节点',{exact:true}).waitFor();
    assert.equal(await page.locator('[data-id=one]').getAttribute('aria-pressed'),'true');
    await go('sources');
    await page.evaluate(()=>{window.importFixture=null;});
    await page.locator('[data-act=import-file]').click();
    await page.locator('.import-status').filter({hasText:'请选择订阅文件'}).waitFor();
    await page.waitForFunction(()=>!!window.importFixture);
    await page.evaluate(()=>window.importFixture.cancel());
    await page.locator('.import-status').waitFor({state:'hidden'});
    await page.evaluate(()=>{window.importFixture=null;});
    await page.locator('[data-act=import-file]').click();
    await page.waitForFunction(()=>!!window.importFixture);
    await page.evaluate(()=>window.importFixture.reading());
    await page.locator('.import-status').filter({hasText:'正在读取并识别订阅'}).waitFor();
    await page.evaluate(()=>window.importFixture.fail());
    await page.locator('.import-status').waitFor({state:'hidden'});
    await page.locator('.import-failure').filter({hasText:'HTTP 403'}).waitFor();
    await go('settings');
    await page.locator('#language').selectOption('en');
    assert.equal(await page.locator('label[for=proxy-port]').textContent(),'Local proxy port');
    assert.equal(await page.title(),'FlowPivot');
    assert.equal(await page.locator('.brand strong').textContent(),'FlowPivot');
    assert.equal(await page.locator('[data-route=sources] span').textContent(),'Subscriptions');
    assert.equal(await page.evaluate(()=>window.nativeLanguage),'en');
    assert.deepEqual(await page.locator('#theme option').allTextContents(),['Glass','Light','Dark','Mist Blue','Warm Paper','Ink Blue']);
    await page.getByRole('button',{name:'Disclaimer',exact:true}).click();
    const englishDisclaimer=page.getByRole('dialog',{name:'Disclaimer'});
    await englishDisclaimer.getByText(/does not provide proxy servers or subscriptions/).waitFor();
    await page.keyboard.press('Escape');
    await englishDisclaimer.waitFor({state:'hidden'});
    await page.setViewportSize({width:1146,height:756});
    assert.equal(await page.evaluate(()=>document.scrollingElement.scrollHeight-document.scrollingElement.clientHeight),0,'English settings should fit the desktop window');
    await go('connections');
    await checkToolbarRow();
    await page.getByRole('button',{name:'Routing rules',exact:true}).click();
    await domain.getByText('Match type',{exact:true}).waitFor();
    assert.deepEqual(await domain.locator('#domain-kind option').allTextContents(),['Domain keyword','Domain regex','Exact domain','IP address']);
    await domain.locator('#domain-kind').selectOption('regex');
    assert.equal(await domain.locator('#domain-pattern').getAttribute('placeholder'),expression);
    await domain.getByRole('button',{name:'Done',exact:true}).click();
    await go('sources');
    await page.locator('.import-failure').filter({hasText:'Access denied'}).waitFor();
    await page.getByRole('button',{name:'Dismiss'}).click();
    await page.locator('.import-failure').waitFor({state:'hidden'});
    await page.reload();await page.waitForSelector('body[data-ready=yes]');
    assert.equal(await page.locator('html').getAttribute('lang'),'en');
    await page.getByRole('button',{name:'Not now'}).click();
    await go('nodes');
    assert.equal(await page.locator('#sort').inputValue(),'delay');
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a5');
    await go('settings');
    await page.locator('#language').selectOption('zh-CN');
    assert.equal(await page.title(),'流枢');
    assert.equal(await page.locator('.brand strong').textContent(),'流枢');
    assert.equal(await page.evaluate(()=>window.nativeLanguage),'zh-CN');
    await go('sources');
    assert.equal(await page.locator('[data-act=import]').isEnabled(),true);
    assert.deepEqual(errors,[]);
    console.log('PASS: silent unchanged refresh, manual and automatic disconnect notices and dismissal, paper default and theme persistence, centralized routing rules, bilingual text and existing renderer regressions.');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
