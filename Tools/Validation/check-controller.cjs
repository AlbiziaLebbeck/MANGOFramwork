const {chromium} = require('playwright');
const http = require('node:http'), fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..'), template = path.join(root, 'Assets/WebGLTemplates/MANGOsPWA');
const server = http.createServer((req,res) => {
  const pathname = new URL(req.url,'http://localhost').pathname;
  if (pathname === '/favicon.ico') { res.writeHead(204);res.end();return; }
  if (pathname === '/') { res.setHeader('Content-Type','text/html; charset=utf-8');res.end(`<!doctype html><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover"><title>MANGOs controller QA</title>
    <style>body{margin:0;background:#253e4c}canvas{width:100vw;height:100dvh;display:block}#type-test{position:fixed;top:60px;left:10px;z-index:9500;width:100px}</style>
    <script>window.messages=[];window.unityInstance={SendMessage:(object,method,data)=>messages.push({method,data})};window.VOICE_APP_CONFIG={mock:true,requireGameRoom:true,roomNamespace:'qa'};</script>
    <script src="voice-app.js"></script><script src="mangos-controller.js" defer></script>
    <canvas id="unity-canvas" tabindex="0"></canvas><input id="type-test" placeholder="Typing test">`);return; }
  const base = template;
  const target = path.resolve(base, '.'+decodeURIComponent(pathname));
  if (!target.startsWith(base+path.sep)) {res.writeHead(403);res.end();return;}
  fs.readFile(target,(error,data)=>{res.writeHead(error?404:200,{'Content-Type':target.endsWith('.js')?'application/javascript':target.endsWith('.css')?'text/css':'text/plain'});res.end(error?'Missing':data);});
});
const evidence = path.resolve(process.argv[2] || path.join(root, 'Builds/Validation/Controllers'));
fs.mkdirSync(evidence,{recursive:true});
const cases = [
  {name:'desktop',family:'desktop',viewport:{width:1280,height:800}},
  {name:'phone',family:'phone',viewport:{width:390,height:844},isMobile:true,hasTouch:true,userAgent:'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1'},
  {name:'android-tablet',family:'tablet',viewport:{width:820,height:1180},isMobile:true,hasTouch:true,userAgent:'Mozilla/5.0 (Linux; Android 14; SM-X700) AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36'},
  {name:'ipad-desktop-ua',family:'tablet',viewport:{width:1024,height:768},hasTouch:true,ipad:true,userAgent:'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15'},
  {name:'desktop-touchscreen',family:'desktop',viewport:{width:1280,height:800},hasTouch:true},
];
(async()=>{
  await new Promise(r=>server.listen(8768,'127.0.0.1',r));
  const browser = await chromium.launch({channel:'msedge',headless:true});
  const results=[];
  try {
    for (const spec of cases) {
      const {name,family,ipad,...options} = spec;
      const context = await browser.newContext(options), page = await context.newPage(), errors=[];
      if (ipad) await page.addInitScript(()=>{Object.defineProperty(navigator,'platform',{get:()=> 'MacIntel'});Object.defineProperty(navigator,'maxTouchPoints',{get:()=>5});});
      await page.addInitScript(()=>localStorage.setItem('mangos.inputMode','touch'));
      page.on('pageerror',e=>errors.push(e.message));
      await page.goto('http://127.0.0.1:8768/');
      await page.waitForFunction(()=>window.MangosController && window.MangosVoiceOverlay);
      assert.equal((await page.evaluate(()=>MangosController.getState())).family,family);
      assert(await page.locator('#mangos-controls').isHidden());
      await page.evaluate(()=>MangosController.setPlayer({ready:true,canFly:false}));
      assert(await page.locator('[data-action="fly"]').isHidden());
      assert.equal(await page.locator('#mangos-controls select').count(),0);
      assert.equal(await page.evaluate(()=>localStorage.getItem('mangos.inputMode')),null);
      if (family === 'desktop') {
        assert(await page.locator('#mangos-stick').isHidden());
        assert.equal((await page.evaluate(()=>MangosController.getState())).touch,false);
        await page.mouse.move(700,300);await page.mouse.move(780,350,{steps:6});await page.waitForTimeout(80);
        assert(!(await page.evaluate(()=>messages.filter(m=>m.method==='OnInput').map(m=>JSON.parse(m.data)))).some(m=>m.lx||m.ly),'Mouse motion without RMB produced look input');
        await page.mouse.down({button:'right'});await page.mouse.move(900,380,{steps:8});await page.waitForTimeout(80);
        await page.mouse.up({button:'right'});
        assert((await page.evaluate(()=>messages.filter(m=>m.method==='OnInput').map(m=>JSON.parse(m.data)))).some(m=>m.lx>0),'RMB drag did not produce look input');
        await page.evaluate(()=>messages.length=0);await page.mouse.move(950,400,{steps:6});await page.waitForTimeout(80);
        assert(!(await page.evaluate(()=>messages.filter(m=>m.method==='OnInput').map(m=>JSON.parse(m.data)))).some(m=>m.lx||m.ly),'Released RMB kept producing look input');
      } else {
        await page.evaluate(async()=>{await MangosVoiceOverlay.setSession({roomId:'controller-qa'});await MangosVoiceOverlay.join();MangosController.setPlayer({ready:true,canFly:true});});
        assert(await page.locator('#mangos-stick').isVisible());
        assert(await page.locator('[data-action="fly"]').isVisible());
        const stick = await page.locator('#mangos-stick').boundingBox(), jump = await page.locator('[data-action="jump"]').boundingBox();
        const voice = await page.locator('#voice-ui-container').boundingBox();
        assert(stick.y+stick.height < voice.y,'Joystick and voice bar overlap');
        assert(jump.y+jump.height < voice.y,'Action buttons and voice bar overlap');
        const cdp=await context.newCDPSession(page);
        const touches=[{x:stick.x+stick.width*.7,y:stick.y+stick.height*.3,id:1},{x:options.viewport.width*.65,y:options.viewport.height*.4,id:2},{x:jump.x+jump.width/2,y:jump.y+jump.height/2,id:3}];
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:touches});
        touches[1].x+=24;touches[1].y-=16;
        await cdp.send('Input.dispatchTouchEvent',{type:'touchMove',touchPoints:touches});
        await page.waitForTimeout(120);
        const messages=await page.evaluate(()=>messages.filter(m=>m.method==='OnInput').map(m=>JSON.parse(m.data)));
        assert(messages.some(m=>m.x>.1 && m.y>.1 && m.jump),'Simultaneous move + jump missing');
        assert(messages.some(m=>m.lx>0 && m.ly>0),'Camera gesture missing');
        await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        await page.waitForTimeout(80);
        const last=await page.evaluate(()=>JSON.parse(messages.filter(m=>m.method==='OnInput').at(-1).data));
        assert.equal(last.x,0);assert.equal(last.jump,false);
        await page.screenshot({path:path.join(evidence,`controller-${name}.png`)});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:stick.x+stick.width*.7,y:stick.y+stick.height*.3,id:5}]});
        await page.waitForTimeout(60);
        await page.setViewportSize({width:options.viewport.height,height:options.viewport.width});
        await page.waitForTimeout(100);
        const rotatedInput=await page.evaluate(()=>JSON.parse(messages.filter(m=>m.method==='OnInput').at(-1).data));
        assert.equal(rotatedInput.x,0,'Rotation did not reset held joystick');
        await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.equal((await page.evaluate(()=>MangosController.getState())).orientation,options.viewport.width>options.viewport.height?'portrait':'landscape');
        await page.screenshot({path:path.join(evidence,`controller-${name}-rotated.png`)});
        await page.evaluate(()=>MangosController.setUiPanel(true));
        assert(await page.locator('#mangos-stick').isHidden());
        assert.equal((await page.evaluate(()=>MangosController.getState())).panelOpen,true);
        await page.evaluate(()=>MangosController.setUiPanel(false));
        assert(await page.locator('#mangos-stick').isVisible());
        await page.locator('#type-test').focus();
        assert.equal((await page.evaluate(()=>MangosController.getState())).focused,true);
        assert(await page.locator('#mangos-stick').isHidden());
        await page.locator('#type-test').blur();
        await page.keyboard.press('w');
        assert.equal((await page.evaluate(()=>MangosController.getState())).touch,false);
        await cdp.send('Input.dispatchTouchEvent',{type:'touchStart',touchPoints:[{x:50,y:150,id:4}]});
        await cdp.send('Input.dispatchTouchEvent',{type:'touchEnd',touchPoints:[]});
        assert.equal((await page.evaluate(()=>MangosController.getState())).touch,true);
        await page.evaluate(()=>window.dispatchEvent(new Event('blur')));
        const reset=await page.evaluate(()=>JSON.parse(messages.filter(m=>m.method==='OnInput').at(-1).data));assert.equal(reset.x,0);assert(!reset.jump);
        await cdp.detach();
      }
      await page.evaluate(()=>MangosController.setPlayer({ready:false,canFly:false}));
      assert(await page.locator('#mangos-controls').isHidden());
      assert.deepEqual(errors,[]);results.push({name,passed:true});await context.close();
    }
    console.log(JSON.stringify({passed:true,cases:results,checks:['device family','orientation changes','desktop look requires held RMB','automatic input selection without selector','ignores old saved override','avatar modal hides controls','simultaneous joystick + look + jump','pointer release','typing focus','external keyboard auto mode','touch restores auto mode','disconnect cleanup','voice bar spacing']},null,2));
  } finally {await browser.close();server.close();}
})().catch(e=>{console.error(e);server.close();process.exitCode=1;});
