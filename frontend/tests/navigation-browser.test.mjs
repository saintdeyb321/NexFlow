import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './navigation-preview.server.mjs';

// Real Chromium rendering/keyboard + localhost fixture; no production identity or external API.
let preview, browser, socket, profile, exit;
let sequence = 0;
const pending = new Map();
const wait = ms => new Promise(done => setTimeout(done, ms));
const send = (method, params = {}) => new Promise((done, reject) => {
  const id = ++sequence;
  const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 15_000);
  pending.set(id, { done, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
});
const evaluate = async expression => {
  const response = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (response.exceptionDetails) throw new Error(response.exceptionDetails.exception?.description ?? response.exceptionDetails.text);
  return response.result.value;
};
const until = async (expression, message) => {
  for (let step = 0; step < 160; step++) { if (await evaluate(`Boolean(${expression})`)) return; await wait(50); }
  throw new Error(message);
};
const click = async text => evaluate(`(()=>{const e=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(text)}&&e.getClientRects().length);if(!e||e.disabled)throw Error('Button unavailable');e.click()})()`);
const change = async (label, value) => evaluate(`(()=>{const l=[...document.querySelectorAll('label')].find(e=>e.textContent.replace('*','').trim()===${JSON.stringify(label)});const e=document.getElementById(l.htmlFor);Object.getOwnPropertyDescriptor(e.tagName==='SELECT'?HTMLSelectElement.prototype:e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype,'value').set.call(e,${JSON.stringify(value)});e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}))})()`);
const capture = async name => {
  const image = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join('node_modules/.tmp', `ux04-${name}.png`), Buffer.from(image.data, 'base64'));
};
const metrics = (width, height = 1000) => send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
const key = async (value, code, vk, shiftKey = false) => {
  const modifiers = shiftKey ? 8 : 0;
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: value, code, modifiers, windowsVirtualKeyCode: vk, text: value === 'Enter' ? '\r' : undefined });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: value, code, modifiers, windowsVirtualKeyCode: vk });
};
before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'ux04-browser-'));
  const executable = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
  assert.ok(executable, 'Installed Chromium browser required; no packages are installed by this test.');
  browser = spawn(executable, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check', '--disable-background-networking', '--disable-sync', '--disable-extensions', '--disable-component-update', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  exit = new Promise(done => browser.once('exit', done));
  const activePort = join(profile, 'DevToolsActivePort');
  for (let step = 0; !existsSync(activePort) && step < 160; step++) await wait(50);
  assert.ok(existsSync(activePort), 'Headless browser did not expose its local debugging port.');
  const port = readFileSync(activePort, 'utf8').split('\n')[0];
  const page = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' }).then(result => result.json());
  socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((done, reject) => { socket.addEventListener('open', done, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id) {
      const request = pending.get(message.id); if (!request) return;
      clearTimeout(request.timer); pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error.message)); else request.done(message.result);
    } else if (message.method === 'Fetch.requestPaused') {
      const url = message.params.request.url;
      void send(/^http:\/\/127\.0\.0\.1:/.test(url) || url.startsWith('data:') ? 'Fetch.continueRequest' : 'Fetch.failRequest',
        /^http:\/\/127\.0\.0\.1:/.test(url) || url.startsWith('data:') ? { requestId: message.params.requestId } : { requestId: message.params.requestId, errorReason: 'BlockedByClient' }).catch(() => {});
    }
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Fetch.enable', { patterns: [{ urlPattern: '*' }] });
  await metrics(1280); await send('Page.navigate', { url: preview.url });
  await until("window.navigationAudit&&window.navigationAudit.navigate&&document.querySelector('[data-business-identity]')", 'Navigation preview did not load.');
});
after(async () => {
  if (socket?.readyState === WebSocket.OPEN) {
    const closed = new Promise(done => socket.addEventListener('close', done, { once: true }));
    await send('Browser.close').catch(() => {});
    await Promise.race([closed, wait(3000)]); socket.close();
  }
  if (browser) { await Promise.race([exit, wait(3000)]); browser.kill(); }
  await preview?.server.close();
  // Only this newly created profile inside the test cache is removed.
  const root = resolve('node_modules/.tmp') + '/';
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'ux04-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});


const profileCalls = method => evaluate("window.navigationAudit.calls.filter(c=>c.path==='/business/profile'&&c.method==="+JSON.stringify(method)+")");
const identities = () => evaluate("[...document.querySelectorAll('[data-business-identity] span[title]')].filter(e=>e.getClientRects().length).map(e=>e.textContent)");
const ready = () => until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Comercial Alfa'||e.textContent==='Comercial Beta')", 'Business identity did not load.');
const reset = async () => {
  await metrics(1280);
  await evaluate("document.querySelectorAll('button[aria-label=\"Cerrar notificación\"]').forEach(button=>button.click())");
  await evaluate("(()=>{const a=window.navigationAudit;Object.assign(a.fixture,{loadDelay:0,saveDelay:0,failLoad:0,failSave:0,ignoreAbort:false});sessionStorage.clear();a.remote.clear();a.locations.clear();a.calls.length=0;a.navigate('/');a.setIdentity(structuredClone(a.defaultIdentity))})()");
  // Wait for the new route and query, rather than matching the previous render's identity.
  await until("document.querySelector('[data-current-module]')?.textContent==='Dashboard'&&window.navigationAudit.calls.some(c=>c.path==='/business/profile'&&c.method==='get')&&window.navigationAudit.client.isFetching()===0", 'Navigation reset did not finish.');
  await ready();
};
const navigate = async path => { await evaluate("window.navigationAudit.navigate("+JSON.stringify(path)+")"); await until("document.querySelector('[data-current-module]').textContent!==''",'Route not loaded.'); };
const link = async label => evaluate("(()=>{const a=[...document.querySelectorAll('nav[aria-label=\"Navegación principal\"] a')].find(e=>e.textContent.trim()==="+JSON.stringify(label)+"&&e.getClientRects().length);if(!a)throw Error('Link unavailable');a.click()})()");
const editProfile = async () => {
  await link('Negocio');
  await until("[...document.querySelectorAll('#workspace-content button')].some(e=>e.textContent.includes('Editar Perfil')&&!e.disabled)",'Profile edit action not ready.');
  await click('Editar Perfil');
};
const openMenu = async () => {
  await evaluate("document.querySelector('button[aria-label=\"Abrir menú\"]').focus()");
  await evaluate("document.querySelector('button[aria-label=\"Abrir menú\"]').click()");
  await until("document.querySelector('[role=dialog]')",'Drawer did not open.');
};
const doubleSave = () => evaluate("(()=>{const f=document.querySelector('#workspace-content form');f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))})()");

test('nombre del negocio real, cuenta personal y grupos ordenados; perfil comparte caché sin GET redundante', async () => {
  await reset(); assert.deepEqual(await identities(),['Comercial Alfa','Comercial Alfa']);
  assert.equal(await evaluate("document.body.textContent.includes('Nombre interno del workspace')"),false);
  assert.ok(await evaluate("document.querySelector('aside [aria-label=\"Mi cuenta\"]').textContent.includes('Ana Pérez')"));
  assert.ok(await evaluate("document.querySelector('aside [aria-label=\"Mi cuenta\"]').textContent.includes('test@example.invalid')"));
  assert.deepEqual(await evaluate("[...document.querySelectorAll('aside nav [role=group]')].map(g=>({label:g.getAttribute('aria-label'),links:[...g.querySelectorAll('a')].map(a=>a.textContent.trim())}))"),
    [{label:'Operación',links:['Dashboard','Mensajes','Reservas','Pedidos','Solicitudes']},{label:'Gestión',links:['Servicios','Catálogo','Base de conocimiento']},{label:'Administración',links:['Negocio']}]);
  assert.equal((await profileCalls('get')).length,1);
  await link('Negocio'); await until("document.querySelector('#workspace-content form')",'Profile not loaded.');
  assert.equal((await profileCalls('get')).length,1); assert.equal((await profileCalls('put')).length,0);
  assert.equal(await evaluate("document.querySelector('[data-current-module]').textContent"),'Negocio');
});

test('PUT exitoso actualiza ambas identidades tras respuesta, antes del GET lento, sin recarga ni duplicados', async () => {
  await reset(); await editProfile(); await change('Nombre Comercial','Nombre confirmado');
  await evaluate("window.navigationAudit.fixture.saveDelay=500;window.navigationAudit.fixture.loadDelay=600");
  const boot=await evaluate("window.navigationAudit.boot");
  await doubleSave(); await until("window.navigationAudit.calls.some(c=>c.method==='put')",'PUT not started.');
  assert.deepEqual(await identities(),['Comercial Alfa','Comercial Alfa']);
  assert.ok(await evaluate("document.querySelector('#workspace-content input').disabled"));
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Nombre confirmado')",'Confirmed identity not published.');
  assert.deepEqual(await identities(),['Nombre confirmado','Nombre confirmado']);
  assert.equal((await profileCalls('put')).length,1);
  assert.equal(await evaluate("window.navigationAudit.boot"),boot);
  await until("[...document.querySelectorAll('button')].some(e=>e.textContent.includes('Editar Perfil')&&!e.disabled)",'Reconciliation did not finish.');
  assert.equal((await profileCalls('get')).length,2);
});

for(const status of [400,503]) test('PUT '+status+' preserva identidad anterior y borrador, permite reintentar y bloquea doble envío', async () => {
  await reset(); await editProfile(); await change('Nombre Comercial','Borrador privado');
  await evaluate("window.navigationAudit.fixture.failSave="+status+";window.navigationAudit.fixture.saveDelay=350");
  await doubleSave();
  await until("document.body.textContent.includes('La API rechazó los cambios del perfil.')&&!document.querySelector('#workspace-content input').disabled",'Save error not shown or draft remained locked.');
  assert.deepEqual(await identities(),['Comercial Alfa','Comercial Alfa']); assert.equal((await profileCalls('put')).length,1);
  assert.equal(await evaluate("document.querySelector('#workspace-content input').value"),'Borrador privado');
  assert.equal(await evaluate("document.querySelector('#workspace-content input').disabled"),false);
  await evaluate("window.navigationAudit.fixture.failSave=0;window.navigationAudit.fixture.saveDelay=0");
  await click('Guardar Cambios'); await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Borrador privado')",'Retry failed.');
  assert.deepEqual(await identities(),['Borrador privado','Borrador privado']);
  await until("[...document.querySelectorAll('#workspace-content button')].some(e=>e.textContent.includes('Editar Perfil')&&!e.disabled)",'Retry reconciliation did not finish.');
});

test('vacío/espacios usan Por definir; loading y errores 403/503 se distinguen y permiten recuperación', async () => {
  for(const name of ['', '   ']) {
    await reset();
    await evaluate("(()=>{const a=window.navigationAudit;a.remote.set('workspace-a',{...a.initialProfile('workspace-a'),commercialName:"+JSON.stringify(name)+"});void a.client.invalidateQueries({queryKey:['workspace','workspace-a','business','profile']})})()");
    await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Por definir')",'Empty profile fallback incorrect.');
    assert.deepEqual(await identities(),['Por definir','Por definir']);
  }
  for(const status of [403,503]) {
    await reset();
    await evaluate("(()=>{const a=window.navigationAudit;a.fixture.failLoad="+status+";a.fixture.loadDelay=400;sessionStorage.clear();a.setIdentity(structuredClone(a.defaultIdentity))})()");
    await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Cargando negocio…')",'Loading identity missing.');
    assert.equal((await identities()).includes('Por definir'),false);
    await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Negocio no disponible')",'Error identity missing.');
    await evaluate("window.navigationAudit.fixture.failLoad=0;window.navigationAudit.fixture.loadDelay=0");
    await evaluate("[...document.querySelectorAll('button[aria-label=\"Reintentar carga del negocio\"]')].find(e=>e.getClientRects().length).click()");
    await ready();
  }
});

test('sin BUSINESS_PROFILE/READ o workspace: cero GET y sin mostrar nombre cacheado ajeno', async () => {
  await reset(); await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.BUSINESS_PROFILE=[];a.setIdentity(me);a.calls.length=0;a.client.setQueryData(['workspace','workspace-a','business','profile'],{...a.initialProfile('workspace-a'),commercialName:'Dato prohibido'})})()");
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Por definir')",'Permission fallback wrong.');
  assert.deepEqual(await identities(),['Por definir','Por definir']); assert.equal((await profileCalls('get')).length,0);
  await navigate('/settings'); await until("document.body.textContent.includes('No tienes permiso para consultar el perfil')",'Forbidden profile editor rendered.');
  assert.equal((await profileCalls('get')).length,0); assert.equal(await evaluate("document.body.textContent.includes('Dato prohibido')"),false);
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.workspace=null;a.setIdentity(me);a.calls.length=0})()");
  assert.equal((await profileCalls('get')).length,0); assert.ok((await identities()).every(name=>name==='Por definir'));
});

test('GET lento y PUT pendiente no cruzan workspace ni restauran borrador de la sesión anterior', async () => {
  await reset(); await editProfile(); await change('Nombre Comercial','Solo Alfa');
  await evaluate("window.navigationAudit.fixture.saveDelay=650");
  await click('Guardar Cambios'); await until("window.navigationAudit.calls.some(c=>c.method==='put')",'PUT did not start.');
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.fixture.loadDelay=400;a.setIdentity(me)})()");
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Cargando negocio…')",'New workspace loading missing.');
  assert.equal((await identities()).includes('Comercial Alfa'),false);
  await ready(); await wait(800);
  assert.deepEqual(await identities(),['Comercial Beta','Comercial Beta']);
  assert.equal(await evaluate("document.querySelector('#workspace-content input').value"),'Comercial Beta');
  assert.equal((await profileCalls('put'))[0].workspace,'workspace-a');
  assert.equal(await evaluate("window.navigationAudit.client.getQueryData(['workspace','workspace-a','business','profile'])===undefined"),true);
  await reset(); await evaluate("(()=>{const a=window.navigationAudit;a.fixture.loadDelay=600;a.fixture.ignoreAbort=true;sessionStorage.clear();a.setIdentity(structuredClone(a.defaultIdentity))})()");
  await until("window.navigationAudit.calls.filter(c=>c.path==='/business/profile').length>=2",'Slow GET not started.');
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.fixture.loadDelay=0;a.setIdentity(me)})()");
  await ready(); await wait(750); assert.deepEqual(await identities(),['Comercial Beta','Comercial Beta']);
});

test('caché restaurada pertenece a usuario/workspace autorizado; cambio de cuenta/permisos y logout sin fuga', async () => {
  await reset(); await evaluate("window.navigationAudit.persistQueryCache()");
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.setIdentity(me)})()"); await ready();
  assert.deepEqual(await identities(),['Comercial Beta','Comercial Beta']);
  const before=(await profileCalls('get')).filter(c=>c.workspace==='workspace-a').length;
  await evaluate("window.navigationAudit.setIdentity(structuredClone(window.navigationAudit.defaultIdentity))"); await ready();
  assert.deepEqual(await identities(),['Comercial Alfa','Comercial Alfa']);
  assert.equal((await profileCalls('get')).filter(c=>c.workspace==='workspace-a').length,before);
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.user.id='other-user';a.remote.set('workspace-a',{...a.initialProfile('workspace-a'),commercialName:'Cuenta nueva'});a.fixture.loadDelay=400;a.setIdentity(me)})()");
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Cargando negocio…')",'New user reused old cache.');
  assert.equal((await identities()).includes('Comercial Alfa'),false);
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Cuenta nueva')",'New user profile missing.');
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.store.getState().me);me.capabilities.BUSINESS_PROFILE=[];a.setIdentity(me);a.calls.length=0})()");
  await wait(100); assert.equal((await profileCalls('get')).length,0); assert.deepEqual(await identities(),['Por definir','Por definir']);
  const logoutBefore=await evaluate("window.navigationAudit.logoutCount()");
  await click('Cerrar sesión'); assert.equal(await evaluate("window.navigationAudit.logoutCount()"),logoutBefore+1);
  await until("window.navigationAudit.store.getState().me===null",'Logout not called.');
  assert.equal(await evaluate("document.body.textContent.includes('Cuenta nueva')"),false);
});

test('permisos reales ocultan enlaces y grupos vacíos; superadmin conserva identidad y rutas globales', async () => {
  await reset(); await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.entitlements=['CATALOG'];me.capabilities={CATALOG:['READ']};a.setIdentity(me);a.calls.length=0})()");
  await until("document.querySelectorAll('aside nav a').length===1",'Unauthorized links remained.');
  assert.equal(await evaluate("document.querySelector('aside nav a').getAttribute('href')"),'/catalog');
  assert.deepEqual(await evaluate("[...document.querySelectorAll('aside nav [role=group]')].map(e=>e.getAttribute('aria-label'))"),['Gestión']);
  assert.equal((await profileCalls('get')).length,0);
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.workspace=null;me.user.isSuperAdmin=true;a.setIdentity(me);a.calls.length=0;a.navigate('/superadmin')})()");
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Administración Global')",'Global identity missing.');
  assert.deepEqual(await identities(),['Administración Global','Administración Global']);
  assert.equal(await evaluate("document.querySelector('aside nav a').getAttribute('href')"),'/superadmin');
  assert.equal(await evaluate("document.querySelector('[data-current-module]').textContent"),'Consola SuperAdmin');
  assert.equal((await profileCalls('get')).length,0);
  await evaluate("(()=>{const a=window.navigationAudit;const me=structuredClone(a.defaultIdentity);me.user.isSuperAdmin=true;a.setIdentity(me);a.calls.length=0;a.client.setQueryData(['workspace','workspace-a','business','profile'],a.initialProfile('workspace-a'))})()");
  assert.deepEqual(await identities(),['Administración Global','Administración Global']); assert.equal((await profileCalls('get')).length,0);
});

test('todos los enlaces existentes navegan; aria-current y breadcrumb respetan raíz, subrutas y prefijos', async () => {
  await reset();
  for(const [label,path] of [['Dashboard','/'],['Mensajes','/inbox'],['Reservas','/reservations'],['Pedidos','/orders'],['Solicitudes','/requests'],['Servicios','/services'],['Catálogo','/catalog'],['Base de conocimiento','/faqs'],['Negocio','/settings']]) {
    await link(label); await until("document.querySelector('[data-current-module]').textContent==="+JSON.stringify(label),'Breadcrumb mismatch.');
    assert.equal(await evaluate("document.querySelector('aside nav a[aria-current=page]').getAttribute('href')"),path);
    await navigate(path==='/'?'/catalogue':path+'/detail');
    await until("document.querySelector('[data-current-module]').textContent==="+JSON.stringify(path==='/'?'NexFlow':label),'Subroute matching mismatch.');
  }
  await navigate('/superadministrator'); await until("document.querySelector('[data-current-module]').textContent==='NexFlow'",'Prefix collision.');
  assert.equal(await evaluate("document.querySelectorAll('aside nav a[aria-current=page]').length"),0);
});

test('selector autorizado conserva all, elimina sedes inexistentes y no provoca escrituras; notificaciones y skip link intactos', async () => {
  await reset();
  await change('Sede activa','north'); assert.equal(await evaluate("window.navigationAudit.store.getState().selectedLocationId"),'north');
  await evaluate("(()=>{const a=window.navigationAudit;a.locations.set('workspace-a',[{id:'south',name:'Sede Sur',address:'Ficticia',isMain:true}]);void a.client.invalidateQueries({queryKey:['workspace','workspace-a','locations']})})()");
  await until("window.navigationAudit.store.getState().selectedLocationId==='all'",'Removed location did not reset.');
  assert.equal((await profileCalls('put')).length,0);
  await evaluate("document.querySelector('button[aria-label=\"Notificaciones\"]').click()");
  await until("document.body.textContent.includes('No tienes notificaciones')",'Notifications missing.');
  await evaluate("document.querySelector('button[aria-label=\"Cerrar notificaciones\"]').click()");
  await evaluate("document.querySelector('aside a').focus()");
  await key('Tab','Tab',9,true);
  assert.equal(await evaluate("document.activeElement.getAttribute('href')"),'#workspace-content');
  assert.ok(await evaluate("getComputedStyle(document.activeElement).outlineStyle==='solid'"));
  await key('Enter','Enter',13);
  await until("document.activeElement.id==='workspace-content'",'Skip link did not focus content.');
});

test('drawer 360/768: consulta única, foco atrapado/Escape/restauración, cierre al navegar y sin desbordes', async () => {
  await reset();
  for(const width of [360,768]) {
    await metrics(width,800); const before=(await profileCalls('get')).length;
    await openMenu();
    assert.equal((await profileCalls('get')).length,before); assert.equal(await evaluate("document.body.style.overflow"),'hidden');
    assert.ok(await evaluate("document.activeElement.closest('[role=dialog]')!==null"));
    assert.equal(await evaluate("document.documentElement.scrollWidth<=innerWidth"),true);
    await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.includes('Cerrar sesión')).focus()");
    await key('Tab','Tab',9); assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"),'Cerrar diálogo');
    await key('Tab','Tab',9,true); assert.ok(await evaluate("document.activeElement.textContent.includes('Cerrar sesión')"));
    await capture('drawer-'+width);
    await key('Escape','Escape',27); await until("!document.querySelector('[role=dialog]')",'Escape failed.');
    assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"),'Abrir menú');
    assert.equal(await evaluate("document.body.style.overflow"),'');
    await openMenu(); await link('Catálogo'); await until("!document.querySelector('[role=dialog]')",'Navigation did not close drawer.');
    await until("document.querySelector('[data-current-module]').textContent==='Catálogo'",'Drawer route did not finish navigating.');
    assert.equal(await evaluate("document.querySelector('[data-current-module]').textContent"),'Catálogo');
    assert.equal((await profileCalls('get')).length,before);
    await capture('header-'+width);
  }
});

test('nombre largo y alturas reducidas 360/768/1280: scroll de navegación propio y cuenta/logout visibles', async () => {
  await reset(); const long='Negocio comercial con un nombre muy extenso que debe seguir siendo accesible para todas las personas';
  await evaluate("(()=>{const a=window.navigationAudit;a.client.setQueryData(['workspace','workspace-a','business','profile'],{...a.initialProfile('workspace-a'),commercialName:"+JSON.stringify(long)+"})})()");
  for(const width of [360,768,1280]) {
    await metrics(width,560);
    if(width<1024) await openMenu();
    const region=width<1024?'[role=dialog]':'aside';
    assert.equal(await evaluate("document.documentElement.scrollWidth<=innerWidth"),true);
    assert.ok(await evaluate("[...document.querySelectorAll("+JSON.stringify(region+" [data-business-identity] span[title]")+")].some(e=>e.title==="+JSON.stringify(long)+")"));
    assert.ok(await evaluate("(()=>{const root=document.querySelector("+JSON.stringify(region)+");const b=[...root.querySelectorAll('button')].find(e=>e.textContent.includes('Cerrar sesión'));const r=b.getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight&&r.height>=44})()"));
    assert.ok(await evaluate("(()=>{const nav=document.querySelector("+JSON.stringify(region+" nav[aria-label=\"Navegación principal\"]")+");return nav.scrollHeight>nav.clientHeight&&getComputedStyle(nav).overflowY==='auto'})()"));
    await capture('short-'+width);
    if(width<1024) await key('Escape','Escape',27);
  }
});
