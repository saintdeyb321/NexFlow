import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './settings-preview.server.mjs';

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
  writeFileSync(join('node_modules/.tmp', `ux05-${name}.png`), Buffer.from(image.data, 'base64'));
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
  profile = mkdtempSync(join(root, 'ux05-browser-'));
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
  await until("window.settingsAudit&&document.querySelector('[role=tablist]')", 'Settings preview did not load.');
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
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'ux05-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});



const mutations = () => evaluate("window.settingsAudit.calls.filter(call=>call.method!=='get')");
const businessCalls = () => evaluate("window.settingsAudit.calls.filter(call=>!call.path.startsWith('/notifications'))");
const selected = () => evaluate("document.querySelector('[role=tab][aria-selected=true]')?.textContent.trim()");
const tab = async label => {
  await evaluate("(()=>{const e=[...document.querySelectorAll('[role=tab]')].find(e=>e.textContent.trim()==="+JSON.stringify(label)+");if(!e)throw Error('Tab missing');e.click()})()");
  await until("document.querySelector('[role=tab][aria-selected=true]')?.textContent.trim()==="+JSON.stringify(label),'Tab not activated.');
};
const reset = async (modules) => {
  await metrics(1280);
  await evaluate("(()=>{document.querySelectorAll('button[aria-label=\"Cerrar notificación\"]').forEach(e=>e.click());const a=window.settingsAudit;Object.assign(a.fixture,{delay:0,failProfile:0,failLocations:0,failHours:0,failStatus:0,failDisconnect:0,disconnectDelay:0,whatsapp:{...a.disconnected},disconnectResponse:{...a.disconnected},locations:structuredClone(a.initialLocations),profile:{...a.profile}});sessionStorage.clear();a.calls.length=0;const me=structuredClone(a.defaultIdentity);const modules="+JSON.stringify(modules??null)+";if(modules){me.entitlements=modules;me.capabilities=Object.fromEntries(modules.map(m=>[m,me.capabilities[m]]))}a.setIdentity(me)})()");
  await until("document.querySelector('[role=tablist]')",'Settings tabs missing.');
  await until("!document.querySelector('[role=tabpanel]').textContent.includes('Cargando')",'Initial content did not load.');
};
const setWa = async state => {
  await reset(); await evaluate("Object.assign(window.settingsAudit.fixture.whatsapp,"+JSON.stringify(state)+")"); await tab('WhatsApp');
  await until("document.querySelector('[data-whatsapp-state]')",'WhatsApp status missing.');
};
const state = () => evaluate("document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState");
const aria = () => evaluate("(()=>{const tabs=[...document.querySelectorAll('[role=tab]')];const panel=document.querySelector('[role=tabpanel]');return tabs.filter(t=>t.getAttribute('aria-selected')==='true').length===1&&tabs.filter(t=>t.tabIndex===0).length===1&&tabs.every(t=>document.getElementById(t.getAttribute('aria-controls'))===panel)&&document.getElementById(panel.getAttribute('aria-labelledby'))===tabs.find(t=>t.getAttribute('aria-selected')==='true')})()");

test('pestañas autorizadas, inicial y asociaciones aria válidas; sin mutaciones ni consulta WhatsApp al montar', async () => {
  await reset(); assert.equal(await selected(),'Perfil'); assert.equal(await aria(),true);
  assert.deepEqual(await evaluate("[...document.querySelectorAll('[role=tab]')].map(e=>e.textContent.trim())"),['Perfil','Sedes','Horarios','WhatsApp']);
  assert.equal((await mutations()).length,0);
  assert.equal((await businessCalls()).filter(call=>call.path.includes('/whatsapp/')).length,0);
  for(const [module,label] of [['LOCATIONS','Sedes'],['BUSINESS_HOURS','Horarios'],['CONVERSATIONS','WhatsApp']]) {
    await reset([module]); assert.equal(await selected(),label); assert.equal(await aria(),true);
    assert.equal(await evaluate("document.querySelectorAll('[role=tab]').length"),1);
    if(module==='BUSINESS_HOURS') assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('Selecciona una sede')"));
    assert.equal((await mutations()).length,0);
  }
});

test('flechas/Home/End solo cambian foco; Enter/Espacio activan, Tab llega al panel y caché evita consultas extra', async () => {
  await reset();
  await evaluate("document.querySelector('[role=tab][aria-selected=true]').focus()");
  const before=(await businessCalls()).length;
  await key('ArrowRight','ArrowRight',39);
  assert.equal(await evaluate("document.activeElement.textContent.trim()"),'Sedes'); assert.equal(await selected(),'Perfil');
  assert.equal((await businessCalls()).length,before);
  await key('End','End',35); assert.equal(await evaluate("document.activeElement.textContent.trim()"),'WhatsApp');
  await key('Home','Home',36); assert.equal(await evaluate("document.activeElement.textContent.trim()"),'Perfil');
  await key('ArrowRight','ArrowRight',39); await key('Enter','Enter',13);
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Sedes del negocio')",'Enter did not activate.');
  assert.equal(await selected(),'Sedes'); assert.equal(await aria(),true);
  await key('ArrowRight','ArrowRight',39); await key(' ','Space',32);
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Selecciona una sede')",'Space did not activate.');
  assert.equal(await selected(),'Horarios');
  await key('Tab','Tab',9); assert.equal(await evaluate("document.activeElement.getAttribute('role')"),'tabpanel');
  await tab('Perfil'); await tab('Sedes'); await tab('Perfil');
  assert.equal((await businessCalls()).filter(c=>c.path==='/business/profile').length,1);
  assert.equal((await businessCalls()).filter(c=>c.path==='/business/locations').length,1);
  assert.equal((await mutations()).length,0);
});

test('permiso que cambia elimina tab activo, selecciona uno permitido y no deja controles inaccesibles', async () => {
  await reset(); await tab('WhatsApp');
  await evaluate("(()=>{const a=window.settingsAudit;const me=structuredClone(a.store.getState().me);me.capabilities.CONVERSATIONS=[];a.setIdentity(me)})()");
  await until("document.querySelectorAll('[role=tab]').length===3",'Removed permission left tab visible.');
  assert.equal(await selected(),'Perfil'); assert.equal(await aria(),true);
  await evaluate("(()=>{const a=window.settingsAudit;const me=structuredClone(a.defaultIdentity);me.entitlements=[];me.capabilities={};a.setIdentity(me)})()");
  await until("document.body.textContent.includes('Sin acceso a configuración')",'No-access state missing.');
  assert.equal(await evaluate("document.querySelector('[role=tablist]')===null"),true);
  assert.equal((await mutations()).length,0);
});

test('perfil: campos etiquetados, validación nativa y edición/cancelación conservan contrato y borrador', async () => {
  await reset(); await click('Editar Perfil');
  assert.equal(await evaluate("document.querySelector('[role=tabpanel] form').getAttribute('aria-busy')"),'false');
  assert.ok(await evaluate("[...document.querySelectorAll('[role=tabpanel] input,[role=tabpanel] textarea')].every(e=>e.labels.length>0)"));
  await change('Nombre Comercial',''); await evaluate("document.querySelector('[role=tabpanel] form').requestSubmit()");
  assert.equal((await mutations()).length,0);
  await change('Nombre Comercial','Cambio local'); await click('Cancelar');
  assert.equal(await evaluate("document.querySelector('[role=tabpanel] input').value"),'Negocio de prueba'); assert.equal((await mutations()).length,0);
  await click('Editar Perfil'); await change('Nombre Comercial','Perfil confirmado'); await click('Guardar Cambios');
  await until("[...document.querySelectorAll('[data-business-identity] span')].some(e=>e.textContent==='Perfil confirmado')",'Profile contract regression.');
  const request=(await mutations())[0]; assert.equal(request.method,'put'); assert.equal(request.path,'/business/profile');
  assert.equal(request.data.timeZone,'America/Lima');
});

test('perfil/sedes/horarios distinguen carga, error con reintento, vacío y solo lectura', async () => {
  await reset(); await evaluate("(()=>{const a=window.settingsAudit;a.fixture.delay=300;a.fixture.failProfile=503;sessionStorage.clear();a.setIdentity(structuredClone(a.defaultIdentity))})()");
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Cargando perfil')",'Profile loading missing.');
  await until("document.querySelector('[role=tabpanel]').textContent.includes('No se pudo cargar')",'Profile error missing.');
  await evaluate("window.settingsAudit.fixture.failProfile=0;window.settingsAudit.fixture.delay=0");
  await evaluate("document.querySelector('[role=tabpanel] button').click()");
  await until("document.querySelector('[role=tabpanel] form')",'Profile retry failed.');
  await tab('Sedes'); await evaluate("(()=>{const a=window.settingsAudit;a.fixture.locations=[];void a.client.invalidateQueries({queryKey:['workspace','workspace-a','locations']})})()");
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Aún no hay sedes registradas')",'Empty locations missing.');
  await evaluate("(()=>{const a=window.settingsAudit;a.fixture.failLocations=503;void a.client.invalidateQueries({queryKey:['workspace','workspace-a','locations']})})()");
  await until("document.querySelector('[role=tabpanel]').textContent.includes('No se pudo cargar')",'Locations error confused with empty.');
  await evaluate("window.settingsAudit.fixture.failLocations=0"); await click('Reintentar');
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Aún no hay sedes registradas')",'Locations retry failed.');
  await reset(); await evaluate("(()=>{const a=window.settingsAudit;const me=structuredClone(a.defaultIdentity);for(const module of Object.keys(me.capabilities))me.capabilities[module]=['READ'];a.setIdentity(me)})()");
  await until("document.querySelector('[role=tabpanel]').textContent.includes('Solo lectura')",'Profile read-only missing.');
  assert.ok(await evaluate("[...document.querySelectorAll('[role=tabpanel] button')].find(e=>e.textContent.includes('Editar Perfil')).disabled"));
  await tab('Sedes'); assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('Solo lectura')"));
  assert.equal((await mutations()).length,0);
});

test('sedes preservadas: formularios existentes, cancelación, permisos y confirmación de eliminación accesible', async () => {
  await reset(); await tab('Sedes');
  assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('Sede Norte')"));
  assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('Referencia ficticia')"));
  await click('Añadir nueva sede'); assert.equal(await evaluate("document.activeElement===document.querySelector('[role=tabpanel] form input')"),true);
  await change('Nombre de la sede','No persistir'); await click('Cancelar'); assert.equal((await mutations()).length,0);
  await evaluate("document.querySelector('button[aria-label=\"Editar sede Sede Norte\"]').click()");
  assert.equal(await evaluate("document.querySelector('[role=tabpanel] form input').value"),'Sede Norte');
  await click('Cancelar');
  await evaluate("document.querySelector('button[aria-label=\"Eliminar sede Sede Sur\"]').focus();document.querySelector('button[aria-label=\"Eliminar sede Sede Sur\"]').click()");
  await until("document.querySelector('[role=dialog]')",'Delete confirmation missing.');
  assert.ok(await evaluate("document.activeElement.textContent.includes('Cancelar')"));
  await key('Escape','Escape',27);
  await until("!document.querySelector('[role=dialog]')",'Delete dialog Escape failed.');
  assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"),'Eliminar sede Sede Sur');
  assert.equal((await mutations()).length,0);
});

test('horarios mantienen propuesta y sede concreta; cambiar tabs/aplicar no hace PUT automático', async () => {
  await reset(); await tab('Horarios');
  assert.equal((await businessCalls()).filter(c=>c.path.endsWith('/hours')).length,0);
  await evaluate("window.settingsAudit.store.getState().setSelectedLocationId('north')");
  await until("document.querySelectorAll('[data-day]').length===7",'Hours not loaded.');
  assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('sugerencia editable')"));
  await click('Aplicar al borrador'); assert.equal((await mutations()).length,0);
  await tab('Perfil'); await tab('Horarios');
  await until("document.querySelectorAll('[data-day]').length===7",'Cached hours not restored.');
  assert.equal((await businessCalls()).filter(c=>c.path.endsWith('/hours')).length,1);
  assert.equal((await mutations()).length,0);
});

test('WhatsApp muestra los ocho estados sin iniciar conexión ni logout al montar/cambiar tabs', async () => {
  for(const status of ['DISCONNECTED','CONNECTING','QR_AVAILABLE','QR_EXPIRED','CONNECTED','RECONNECTING','UNAVAILABLE','DISCONNECT_PENDING']) {
    const linked=['CONNECTED','RECONNECTING','DISCONNECT_PENDING'].includes(status);
    await setWa({status,isLinked:linked,requiresLogout:status==='DISCONNECT_PENDING',canConnect:status==='DISCONNECTED'||status==='QR_EXPIRED',qrBase64:status==='QR_AVAILABLE'?'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 10 10%22%3E%3Cpath d=%22M0 0h10v10H0z%22/%3E%3C/svg%3E':null,qrExpiresAt:status==='QR_AVAILABLE'?new Date(Date.now()+20_000).toISOString():null});
    assert.equal(await state(),status);
    assert.equal((await mutations()).length,0);
    if(status==='QR_AVAILABLE') assert.ok(await evaluate("document.querySelector('img[alt=\"Código QR para vincular WhatsApp\"]')!==null"));
    if(linked) assert.equal(await evaluate("[...document.querySelectorAll('[role=tabpanel] button')].some(e=>e.textContent.includes('Conectar WhatsApp'))"),false);
    await tab('Perfil'); await tab('WhatsApp'); assert.equal((await mutations()).length,0);
  }
});

test('WhatsApp conectado no hace polling permanente; QR expirado y READ sin CONFIGURE conservan restricciones', async () => {
  await setWa({status:'CONNECTED',isLinked:true,canConnect:false,requiresLogout:false});
  const before=(await businessCalls()).filter(c=>c.path==='/business/whatsapp/status').length;
  await wait(5400);
  assert.equal((await businessCalls()).filter(c=>c.path==='/business/whatsapp/status').length,before);
  await setWa({status:'QR_AVAILABLE',isLinked:false,canConnect:true,requiresLogout:false,qrBase64:'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22/%3E',qrExpiresAt:new Date(Date.now()-1000).toISOString()});
  assert.equal(await state(),'QR_EXPIRED'); assert.equal(await evaluate("document.querySelector('img[alt=\"Código QR para vincular WhatsApp\"]')===null"),true);
  await evaluate("(()=>{const a=window.settingsAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.CONVERSATIONS=['READ'];a.setIdentity(me)})()");
  await tab('WhatsApp'); await until("document.querySelector('[role=tabpanel]').textContent.includes('Solo lectura')",'WhatsApp configure permission ignored.');
  assert.equal(await evaluate("document.querySelector('img[alt=\"Código QR para vincular WhatsApp\"]')===null"),true);
  assert.equal((await mutations()).length,0);
});

test('desconexión 503 muestra ambigüedad/error real, conserva sesión, confirmación y no genera otro QR', async () => {
  await setWa({status:'CONNECTED',isLinked:true,canConnect:false,requiresLogout:false});
  await evaluate("window.settingsAudit.fixture.failDisconnect=503;window.settingsAudit.fixture.disconnectDelay=400");
  await click('Desconectar explícitamente'); await until("document.querySelector('[role=dialog]')",'Disconnect dialog missing.');
  await evaluate("(()=>{const b=[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.includes('Confirmar desconexión'));b.click();b.click()})()");
  await until("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').disabled",'Disconnect close was not blocked.');
  await key('Escape','Escape',27); assert.ok(await evaluate("document.querySelector('[role=dialog]')!==null"));
  await until("document.querySelector('[role=dialog]').textContent.includes('No se pudo confirmar la desconexión; revisa el estado.')",'503 ambiguity warning missing.');
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('(503)')"));
  assert.equal((await mutations()).filter(c=>c.path.endsWith('/disconnect')).length,1);
  assert.equal((await mutations()).filter(c=>c.path.endsWith('/connect')).length,0);
  assert.equal(await evaluate("document.body.textContent.includes('Desconexión confirmada.')"),false);
  assert.equal(await state(),'CONNECTED');
  await click('Cancelar'); assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('No se pudo confirmar')"));
});

test('respuesta de cierre pendiente no afirma éxito; solo un estado confirmado permite otra vinculación', async () => {
  await setWa({status:'CONNECTED',isLinked:true,canConnect:false,requiresLogout:false});
  await evaluate("Object.assign(window.settingsAudit.fixture.disconnectResponse,{status:'DISCONNECT_PENDING',isLinked:true,canConnect:false,requiresLogout:true})");
  await click('Desconectar explícitamente'); await click('Confirmar desconexión');
  await until("!document.querySelector('[role=dialog]')&&document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='DISCONNECT_PENDING'",'Pending response not represented.');
  assert.ok(await evaluate("document.body.textContent.includes('No se pudo confirmar la desconexión; revisa el estado.')"));
  assert.equal(await evaluate("document.body.textContent.includes('Desconexión confirmada.')"),false);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=tabpanel] button')].some(e=>e.textContent.includes('Conectar WhatsApp'))"),false);
  await evaluate("window.settingsAudit.fixture.whatsapp={...window.settingsAudit.disconnected}");
  await click('Revisar estado'); await until("document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='DISCONNECTED'",'Confirmed review not represented.');
  assert.ok(await evaluate("[...document.querySelectorAll('[role=tabpanel] button')].some(e=>e.textContent.includes('Conectar WhatsApp')&&!e.disabled)"));
  assert.equal((await mutations()).length,1);
});

test('GET WhatsApp 503 conserva datos vinculados y ofrece reintento sin falso desconectado', async () => {
  await setWa({status:'CONNECTED',isLinked:true,canConnect:false,requiresLogout:false});
  await evaluate("window.settingsAudit.fixture.failStatus=503"); await click('Revisar estado');
  await until("document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='UNAVAILABLE'",'GET error became disconnected.');
  assert.ok(await evaluate("document.querySelector('[role=tabpanel]').textContent.includes('No se pudo consultar WhatsApp.')"));
  assert.equal(await evaluate("[...document.querySelectorAll('[role=tabpanel] button')].some(e=>e.textContent.includes('Conectar WhatsApp'))"),false);
  await evaluate("window.settingsAudit.fixture.failStatus=0"); await click('Reintentar');
  await until("document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='CONNECTED'",'Status retry failed.');
  assert.equal((await mutations()).length,0);
});

test('conexión requiere acción explícita y un doble clic genera una sola solicitud; cierre confirmado respeta el contrato', async () => {
  await setWa({status:'DISCONNECTED',isLinked:false,canConnect:true,requiresLogout:false});
  assert.equal((await mutations()).length,0);
  await evaluate("window.settingsAudit.fixture.delay=350;Object.assign(window.settingsAudit.fixture.whatsapp,{status:'CONNECTING',canConnect:false})");
  await evaluate("(()=>{const b=[...document.querySelectorAll('[role=tabpanel] button')].find(e=>e.textContent.includes('Conectar WhatsApp'));b.click();b.click()})()");
  await until("document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='CONNECTING'",'Connect response missing.');
  assert.equal((await mutations()).filter(c=>c.path.endsWith('/connect')).length,1);
  await setWa({status:'CONNECTED',isLinked:true,canConnect:false,requiresLogout:false});
  await click('Desconectar explícitamente'); await click('Confirmar desconexión');
  await until("document.body.textContent.includes('Desconexión confirmada.')&&document.querySelector('[data-whatsapp-state]')?.dataset.whatsappState==='DISCONNECTED'",'Confirmed logout missing.');
  const request=(await mutations())[0]; assert.equal(request.path,'/business/whatsapp/disconnect'); assert.equal(request.method,'post');
  assert.deepEqual(request.data,{confirmed:true});
});

test('responsive 360/768/1280: tabs contenidos, foco visible, contraste de texto/controles y QR dentro del panel', async () => {
  await setWa({status:'QR_AVAILABLE',isLinked:false,canConnect:false,requiresLogout:false,qrBase64:'data:image/svg+xml,%3Csvg xmlns=%22http://www.w3.org/2000/svg%22 viewBox=%220 0 10 10%22%3E%3Cpath d=%22M0 0h10v10H0z%22/%3E%3C/svg%3E',qrExpiresAt:new Date(Date.now()+60_000).toISOString()});
  for(const width of [360,768,1280]) {
    await metrics(width,900);
    assert.equal(await evaluate("document.documentElement.scrollWidth<=innerWidth"),true);
    assert.ok(await evaluate("[...document.querySelectorAll('[role=tab]')].every(e=>e.getBoundingClientRect().height>=44)"));
    await evaluate("document.querySelector('[role=tab][aria-selected=true]').focus()");
    await key('ArrowLeft','ArrowLeft',37);
    assert.ok(await evaluate("getComputedStyle(document.activeElement).outlineStyle==='solid'"));
    assert.ok(await evaluate("(()=>{const r=document.activeElement.getBoundingClientRect();const t=document.querySelector('[role=tablist]').getBoundingClientRect();return r.left>=t.left&&r.right<=t.right})()"));
    assert.equal(await selected(),'WhatsApp');
    await evaluate("document.querySelector('.nf-settings').scrollIntoView({block:'start'})"); await capture('tabs-'+width);
    await evaluate("document.querySelector('img[alt=\"Código QR para vincular WhatsApp\"]').scrollIntoView({block:'center'})");
    assert.ok(await evaluate("(()=>{const r=document.querySelector('img[alt=\"Código QR para vincular WhatsApp\"]').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth})()"));
    await capture('whatsapp-'+width);
  }
  await tab('Perfil'); await click('Editar Perfil');
  await until("document.querySelector('[role=tabpanel] input')&&!document.querySelector('[role=tabpanel] input').disabled",'Profile inputs did not become editable.');
  await until("getComputedStyle(document.querySelector('[role=tabpanel] input')).borderColor==='rgb(71, 85, 105)'",'Editable input border did not reach the settings contrast color.');
  const contrasts = await evaluate("(()=>{const canvas=document.createElement('canvas');canvas.width=canvas.height=1;const ctx=canvas.getContext('2d');const rgb=color=>{ctx.clearRect(0,0,1,1);ctx.fillStyle=color;ctx.fillRect(0,0,1,1);return [...ctx.getImageData(0,0,1,1).data]};const luminance=values=>values.slice(0,3).map(v=>{v/=255;return v<=.04045?v/12.92:((v+.055)/1.055)**2.4}).reduce((sum,v,i)=>sum+v*[.2126,.7152,.0722][i],0);const ratio=(a,b)=>{const x=luminance(a),y=luminance(b);return (Math.max(x,y)+.05)/(Math.min(x,y)+.05)};return [...document.querySelectorAll('.nf-settings label,.nf-settings .text-muted,.nf-settings [role=tab],.nf-settings .nf-button:not(:disabled)')].filter(e=>e.getClientRects().length&&!e.matches(':disabled')).map(e=>{let parent=e;let bg;while(parent){bg=rgb(getComputedStyle(parent).backgroundColor);if(bg[3]===255)break;parent=parent.parentElement}return {text:e.textContent.trim(),ratio:ratio(rgb(getComputedStyle(e).color),bg||[255,255,255,255])}})})()");
  assert.ok(contrasts.length>10); for(const value of contrasts) assert.ok(value.ratio>=4.5,JSON.stringify(value));
  const border = await evaluate("getComputedStyle(document.querySelector('[role=tabpanel] input')).borderColor");
  assert.equal(border,'rgb(71, 85, 105)');
  await evaluate("document.querySelector('[role=tabpanel] input').focus()"); await key('Tab','Tab',9);
  assert.ok(await evaluate("getComputedStyle(document.activeElement).outlineStyle==='solid'"));
  await send('Emulation.setEmulatedMedia',{features:[{name:'prefers-reduced-motion',value:'reduce'}]});
  assert.ok(await evaluate("matchMedia('(prefers-reduced-motion: reduce)').matches"));
  assert.ok(await evaluate("parseFloat(getComputedStyle(document.querySelector('[role=tab]')).transitionDuration)<=.001"));
  await capture('profile-1280');
  await send('Emulation.setEmulatedMedia',{features:[]});
  assert.equal((await mutations()).length,0);
});

test('ConfirmDialog 360/768/1280 conserva tamaño, foco atrapado, Escape y restauración; drawer sin cambios', async () => {
  await reset(); await tab('Sedes');
  for(const width of [360,768,1280]) {
    await metrics(width,720);
    await evaluate("document.querySelector('button[aria-label=\"Eliminar sede Sede Sur\"]').focus();document.querySelector('button[aria-label=\"Eliminar sede Sede Sur\"]').click()");
    await until("document.querySelector('[role=dialog]')",'Dialog missing.');
    assert.ok(await evaluate("(()=>{const r=document.querySelector('[role=dialog]').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight})()"));
    await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent==='Eliminar').focus()");
    await key('Tab','Tab',9); assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"),'Cerrar diálogo');
    await capture('modal-'+width); await key('Escape','Escape',27);
    await until("!document.querySelector('[role=dialog]')",'Dialog Escape failed.');
    assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"),'Eliminar sede Sede Sur');
  }
  await metrics(360,720); await evaluate("document.querySelector('button[aria-label=\"Abrir menú\"]').click()");
  await until("document.querySelector('[role=dialog]')",'Sidebar drawer did not open.');
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Mi cuenta')"));
  await key('Escape','Escape',27); assert.equal((await mutations()).length,0);
});
