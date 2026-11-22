'use strict';
// Renderer-only QA. Its bridge fixture is not an engine/network test.
const assert = require('node:assert/strict');
const {pathToFileURL} = require('node:url');
const path = require('node:path');
const {chromium} = require(process.env.FLOWPIVOT_PLAYWRIGHT || 'playwright');
(async()=>{
  const browser=await chromium.launch({headless:true,executablePath:process.env.FLOWPIVOT_BROWSER});
  try {
    const page=await browser.newPage({viewport:{width:1200,height:790}});
    const errors=[];page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript(()=>{
      const state={appVersion:'0.2.0-alpha.8',online:false,mode:'local',cores:['Mihomo','Xray','sing-box','V2Fly'].map(id=>({id,version:'v1',available:'v1'})),source:'a',node:'a4',preferred:'Xray',sources:[{id:'a',name:'测试订阅',format:'Mihomo',count:2,refreshable:true},{id:'b',name:'另一个订阅',format:'Xray',count:1,refreshable:true}],nodes:[{id:'a4',name:'甲',engine:'Mihomo',kind:'http',ready:true,delay:80},{id:'a5',name:'乙',engine:'Mihomo',kind:'socks5',ready:true,delay:30}],traffic:{up:0,down:0,rateUp:0,rateDown:0},connections:{total:0,direct:0,routed:0,rows:[]}};
      let reply;window.updateChecks=0;window.openDownloads=[];window.clipboard=[];window.sourceCalls=0;
      window.chrome ||= {};
      window.chrome.webview={addEventListener(k,f){if(k==='message')reply=f;},postMessage({id,action,args}){
        if(action==='app-check'){
          window.updateChecks++;
          const fail=window.updateCheckFails;
          setTimeout(()=>reply({data:{id,ok:!fail,body:fail?'连不上 GitHub，暂时无法检查更新。':{version:'0.2.0-alpha.9'}}}),60);
          return;
        }
        if(action==='app-download'){
          window.openDownloads.push(args.version);
          queueMicrotask(()=>reply({data:{id,ok:true,body:null}}));return;
        }
        if(action==='select-source'){
          window.sourceCalls++;state.source=args.id;state.node=null;
        }
        if(action==='copy-proxy'){
          const address='127.0.0.1:'+state.port;
          window.clipboard.push(address);
          queueMicrotask(()=>reply({data:{id,ok:true,body:address}}));return;
        }
        if(action==='delay'){
          window.measure={
            batch:()=>reply({data:{id,progress:{ids:['a4','a5']}}}),
            sample:(node,delay)=>{state.nodes.find(n=>n.id===node).delay=delay;reply({data:{id,progress:{id:node,delay}}});},
            end:()=>reply({data:{id,ok:true,body:structuredClone(state)}}),
            fail:()=>reply({data:{id,ok:false,body:'测试错误'}}),
          };
          return;
        }
        if(action==='close-behavior')state.closeToTray=args.value==='tray';
        if(action==='select-node')state.node=args.id;
        if(action==='start'){state.online=true;state.mode=args.mode;state.port=51234;}
        if(action==='stop')state.online=false;
        const body=action==='check'?args.urls.map(()=>({outcome:'已响应',status:200,ms:55})):structuredClone(state);
        queueMicrotask(()=>reply({data:{id,ok:true,body}}));
      }};
    });
    await page.goto(pathToFileURL(path.join(process.argv[2],'web','index.html')).href);
    await page.waitForSelector('body[data-ready=yes]');
    assert.equal(await page.locator('html').getAttribute('data-theme'),'glass');
    const go = async k=>{
      if(['reach','ip'].includes(k)&&await page.locator('#checks-toggle').getAttribute('aria-expanded')==='false')await page.locator('#checks-toggle').click();
      await page.locator(`[data-route=${k}]`).click();
    };
    const update=page.getByRole('dialog',{name:'发现新版本'});
    await update.waitFor();
    assert.equal(await page.evaluate(()=>window.updateChecks),1);
    assert.match(await update.locator('#update-description').textContent(),/0\.2\.0-alpha\.9/);
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
    assert.equal(corePositions[0].y,corePositions[1].y);
    assert.ok(corePositions[0].x<corePositions[1].x && corePositions[0].y<corePositions[2].y);
    assert.equal(await page.locator('.core-panel button').count(),0);
    assert.equal(await page.locator('.core-row [data-bundled=true]').count(),4);
    const closeBehavior=page.getByLabel('关闭窗口时');
    assert.equal(await closeBehavior.inputValue(),'exit');
    await closeBehavior.selectOption('tray');
    await go('home');await go('settings');
    assert.equal(await closeBehavior.inputValue(),'tray');
    await closeBehavior.selectOption('exit');
    await go('home');await go('settings');
    assert.equal(await closeBehavior.inputValue(),'exit');
    await page.getByRole('button',{name:'查看新版'}).click();
    await update.getByRole('button',{name:'更新',exact:true}).click();
    await update.waitFor({state:'hidden'});
    assert.deepEqual(await page.evaluate(()=>window.openDownloads),['0.2.0-alpha.9']);
    await page.evaluate(()=>{window.updateCheckFails=true;});
    await page.getByRole('button',{name:'检查更新'}).click();
    await page.getByText('连不上 GitHub，暂时无法检查更新。').waitFor();
    assert.equal(await update.isHidden(),true);
    await go('home');
    assert.equal(await page.locator('[data-route=home]').evaluate(el=>getComputedStyle(el).display),'flex');
    assert.equal(await page.locator('[data-route=home] use').getAttribute('href'),'#icon-home');
    assert.equal(await page.locator('.route-diagram > svg').count(),1);
    const settled=()=>page.locator('#nav-glass').evaluate(el=>Promise.all(el.getAnimations().map(a=>a.finished.catch(()=>{}))));
    const aligned=()=>page.evaluate(()=>{
      const glass=document.querySelector('#nav-glass').getBoundingClientRect();
      const active=([...document.querySelectorAll('#nav [aria-current=page]')].find(e=>!e.closest('[inert]'))).getBoundingClientRect();
      return ['left','right','top','bottom'].every(k=>Math.abs(glass[k]-active[k])<1.5);
    });
    await settled();assert.equal(await aligned(),true);
    await page.locator('#nav-glass').evaluate(el=>window.initialGlass=el);
    await page.locator('[data-route=sources]').evaluate(el=>el.click());
    const motion=await page.evaluate(()=>{
      const el=window.initialGlass, a=el.getAnimations()[0];if(!a)return null;
      const duration=a.effect.getTiming().duration;
      a.pause();a.currentTime=duration*.36;
      const stretched=el.getBoundingClientRect().height;
      a.currentTime=duration*.72;
      const bounce=el.getBoundingClientRect().top;
      const target=document.querySelector('[data-route=sources]').getBoundingClientRect();
      const pageMs=document.querySelector('main .enter').getAnimations()[0]?.effect.getTiming().duration;
      a.play();return {same:el===document.querySelector('#nav-glass'),stretch:stretched>target.height,overshoot:bounce>target.top,duration,pageMs};
    });
    assert.deepEqual(motion,{same:true,stretch:true,overshoot:true,duration:175,pageMs:175});
    await settled();assert.equal(await aligned(),true);
    await go('ip');await settled();assert.equal(await aligned(),true);
    await page.locator('#checks-toggle').click();await settled();assert.equal(await aligned(),true);
    await go('home');await settled();
    await page.mouse.move(500,240);
    await page.waitForFunction(()=>document.querySelector('#pointer-glow').dataset.on==='yes'&&document.querySelector('#pointer-glow').style.getPropertyValue('--glow-x')==='500px');
    assert.equal(await page.locator('#pointer-glow').evaluate(el=>getComputedStyle(el).pointerEvents),'none');
    await page.mouse.move(100,200);
    await page.waitForFunction(()=>!document.querySelector('#pointer-glow').dataset.on);
    await page.emulateMedia({reducedMotion:'reduce'});
    await page.waitForFunction(()=>matchMedia('(prefers-reduced-motion: reduce)').matches);
    await settled();
    await go('sources');assert.equal(await page.locator('#nav-glass').evaluate(el=>el.getAnimations().filter(a=>a.effect.getTiming().duration>1).length),0); await settled();assert.equal(await aligned(),true);
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
    await go('nodes');
    await page.locator('[data-id=a5]').evaluate(e=>e.dataset.identity='original');
    await page.locator('[data-id=a5]').click();
    await page.waitForSelector('[data-id=a5][aria-pressed=true]');
    assert.equal(await page.locator('[data-id=a5]').getAttribute('data-identity'),'original');
    await page.locator('#sort').selectOption('delay');
    assert.equal(await page.locator('#node-list button').first().getAttribute('data-id'),'a5');
    const startDelay=async()=>{
      await page.getByRole('button',{name:'测延迟',exact:true}).click();
      await page.locator('#editor input').fill('http://example.invalid');
      await page.locator('#editor').getByRole('button',{name:'确定'}).click();
      await page.getByText('准备测速…',{exact:true}).waitFor();
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
    await page.locator('#power').click();await port.waitFor({state:'hidden'});
    await page.locator('#power').click();await port.waitFor();
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
    await page.getByRole('button',{name:'深色',exact:true}).click();
    assert.equal(await page.locator('html').getAttribute('data-theme'),'dark');
    await page.getByRole('button',{name:'浅色',exact:true}).click();
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
    await page.getByRole('button',{name:'玻璃',exact:true}).click();
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
          assert.equal(result.expanded,'false',`submenu still expanded: ${location}`);
        }
      }
    }
    assert.equal(await page.locator('#nav [title], #nav a[href]').count(),0);
    await go('settings');
    assert.equal(await page.getByRole('button',{name:'玻璃',exact:true}).getAttribute('aria-pressed'),'true');
    const out=process.env.FLOWPIVOT_SCREENSHOTS;
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
    await page.getByRole('button',{name:'深色',exact:true}).click();
    await page.reload();
    await page.waitForSelector('body[data-ready=yes]');
    assert.equal(await page.locator('html').getAttribute('data-theme'),'dark');
    assert.deepEqual(errors,[]);
    console.log('PASS: seven routes at 430/600/850/1150/1280 px, styled navigation, readable settings, glass/light/dark theme and persisted choice, whole-card context menu, node identity retained, delay ordering custom-site checks, bundled cores, startup update prompt, defer/confirm, network errors, stable source-card DOM and geometry, streaming node delay, transient success and final sorting, no settings overflow at recording size, short-window scrolling, close-behavior setting, actual-port display/copy, one 175 ms glass lens synchronized with page entry, no hover hints, no transient overflow across submenu-to-main transitions, spotlight and reduced motion.');
  } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
