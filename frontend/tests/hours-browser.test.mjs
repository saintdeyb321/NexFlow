import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './hours-preview.server.mjs';

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
  writeFileSync(join('node_modules/.tmp', `ux03-${name}.png`), Buffer.from(image.data, 'base64'));
};
const metrics = width => send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
const key = async (value, code, vk, shiftKey = false) => {
  const modifiers = shiftKey ? 8 : 0;
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: value, code, modifiers, windowsVirtualKeyCode: vk, text: value === 'Enter' ? '\r' : undefined });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: value, code, modifiers, windowsVirtualKeyCode: vk });
};
before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'ux03-browser-'));
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
  await until("window.hoursAudit&&document.querySelector('select')", 'Hours preview did not load.');
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
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'ux03-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});

const puts = () => evaluate("window.hoursAudit.calls.filter(call=>call.method==='put')");
const values = () => evaluate("[...document.querySelectorAll('[data-day]')].map(row=>({dayOfWeek:Number(row.dataset.day),isClosed:!row.querySelector('input[type=checkbox]').checked,openTime:row.querySelectorAll('input[type=time]')[0].value,closeTime:row.querySelectorAll('input[type=time]')[1].value}))");
const toggle = day => evaluate("document.querySelector('input[aria-label='+CSS.escape("+JSON.stringify('Abierto '+day)+")+']').click()");
const radio = text => evaluate("(()=>{const l=[...document.querySelectorAll('label')].find(e=>e.textContent.trim()==="+JSON.stringify(text)+");l.querySelector('input').click()})()");
const ready = async () => until("document.querySelectorAll('[data-day]').length===7", 'Week editor did not load.');
const select = async location => { await change('Sede de prueba', location); if (location !== 'all') await ready(); };
const reset = async (location = 'all') => {
  await evaluate("(()=>{const a=window.hoursAudit;Object.assign(a.fixture,{loadDelay:0,saveDelay:0,failLoad:0,failSave:0,ignoreAbort:false});a.remote.clear();a.calls.length=0;sessionStorage.clear();a.setIdentity(structuredClone(a.defaultIdentity));a.store.getState().setSelectedLocationId("+JSON.stringify(location)+")})()");
  if(location !== 'all') await ready();
};
const doubleSave = () => evaluate("(()=>{const f=document.querySelectorAll('section form')[1];f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))})()");

test('all y cambio de pestaña: sin consulta ni escritura; carga distinta de sugerencia', async () => {
  await reset();
  assert.ok(await evaluate("document.body.textContent.includes('Selecciona una sede')"));
  assert.equal(await evaluate("window.hoursAudit.calls.length"), 0);
  await click('Cambiar pestaña de prueba'); await click('Cambiar pestaña de prueba');
  assert.equal(await evaluate("window.hoursAudit.calls.length"), 0);
  await evaluate("window.hoursAudit.fixture.loadDelay=400");
  await change('Sede de prueba', 'empty');
  await until("document.body.textContent.includes('Cargando horarios de la sede')", 'Loading state missing.');
  assert.equal(await evaluate("document.querySelectorAll('[data-day]').length"), 0);
  await ready();
  assert.ok(await evaluate("document.body.textContent.includes('sugerencia editable')"));
  assert.equal((await values()).filter(row=>!row.isClosed).length, 6);
  assert.equal((await puts()).length, 0);
});

test('GET vacío: sugerencia editable, domingo opcional, descartar sin PUT; guardar explícito 204', async () => {
  await reset('empty');
  const week = await values(); assert.deepEqual(week.map(row=>row.dayOfWeek), [1,2,3,4,5,6,0]);
  assert.ok(week.slice(0,6).every(row=>row.openTime==='08:00'&&row.closeTime==='20:00'&&!row.isClosed));
  assert.equal(week[6].isClosed, true);
  await click('Abrir también el domingo');
  assert.equal((await values())[6].openTime, '08:00'); assert.equal((await puts()).length, 0);
  await change('Apertura Lunes', '09:30');
  await click('Descartar cambios');
  assert.deepEqual(await values(), week); assert.equal((await puts()).length, 0);
  await click('Cambiar pestaña de prueba'); await click('Cambiar pestaña de prueba'); await ready();
  assert.deepEqual(await values(), week); assert.equal((await puts()).length, 0);
  await click('Guardar horarios');
  await until("document.body.textContent.includes('Horarios guardados correctamente.')", 'Successful save not shown.');
  const requests = await puts(); assert.equal(requests.length, 1);
  assert.equal(requests[0].path, '/business/locations/empty/hours'); assert.equal(requests[0].data.length, 7);
  assert.deepEqual(requests[0].data[6], {dayOfWeek:0,isClosed:true,openTime:'',closeTime:''});
  assert.equal(await evaluate("document.body.textContent.includes('Esta sede no tiene horarios configurados')"), false);
  assert.ok(await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Guardar horarios').disabled"));
});

test('semana guardada cerrada: sin preset; abrir/cerrar conserva horas del borrador y descartar restaura', async () => {
  await reset('closed'); const before = await values();
  assert.ok(before.every(row=>row.isClosed&&row.openTime===''&&row.closeTime===''));
  assert.equal(await evaluate("document.body.textContent.includes('sugerencia editable')"), false);
  await toggle('Domingo');
  assert.equal((await values())[6].openTime, '08:00'); assert.equal((await values())[6].closeTime, '20:00');
  await change('Apertura Domingo', '11:15'); await change('Cierre Domingo', '17:45');
  await toggle('Domingo'); await toggle('Domingo');
  assert.equal((await values())[6].openTime, '11:15'); assert.equal((await values())[6].closeTime, '17:45');
  await click('Descartar cambios'); assert.deepEqual(await values(), before);
  assert.equal((await puts()).length, 0);
});

test('GET parcial conserva valores; faltantes cerrados y payload final semanal completo', async () => {
  await reset('partial'); const before = await values();
  assert.ok(await evaluate("document.body.textContent.includes('Hay días sin registrar')"));
  assert.equal(before[0].openTime,'09:15'); assert.equal(before[0].closeTime,'18:45');
  assert.equal(before.filter(row=>!row.isClosed).length,1);
  await toggle('Martes'); await click('Guardar horarios');
  await until("document.body.textContent.includes('Horarios guardados correctamente.')", 'Partial save failed.');
  const payload=(await puts())[0].data;
  assert.equal(payload.length,7); assert.equal(new Set(payload.map(row=>row.dayOfWeek)).size,7);
  assert.deepEqual(payload[0],before[0]); assert.equal(payload[1].openTime,'08:00');
  assert.ok(payload.slice(2).every(row=>row.isClosed&&row.openTime===''&&row.closeTime===''));
});

test('acciones masivas all/lunes-sábado/selección: solo borrador, selección válida y conservación individual', async () => {
  await reset('empty');
  await radio('Toda la semana'); await change('Apertura para aplicar','09:00'); await change('Cierre para aplicar','18:00'); await click('Aplicar al borrador');
  assert.ok((await values()).every(row=>!row.isClosed&&row.openTime==='09:00'&&row.closeTime==='18:00'));
  await radio('Lunes a sábado'); await change('Apertura para aplicar','10:00'); await click('Aplicar al borrador');
  assert.equal((await values())[6].openTime,'09:00');
  await change('Apertura Miércoles','11:30');
  await radio('Solo días seleccionados'); await click('Aplicar al borrador');
  assert.ok(await evaluate("document.body.textContent.includes('Selecciona al menos un día.')"));
  await radio('Lunes'); await radio('Domingo'); await change('Apertura para aplicar','12:00'); await click('Aplicar al borrador');
  const week=await values(); assert.equal(week[0].openTime,'12:00'); assert.equal(week[6].openTime,'12:00');
  assert.equal(week[2].openTime,'11:30'); assert.equal(week[1].openTime,'10:00');
  assert.equal((await puts()).length,0);
  // Enter in the bulk form applies locally, never submits the weekly save form.
  await evaluate("[...document.querySelectorAll('label')].find(e=>e.textContent==='Apertura para aplicar').control.focus()");
  await key('Enter','Enter',13);
  assert.equal((await puts()).length,0);
});

test('validación visible por campo, foco al error y cierre normalizado tras edición', async () => {
  await reset('empty'); await change('Apertura Lunes',''); await click('Guardar horarios');
  assert.equal((await puts()).length,0);
  assert.ok(await evaluate("document.activeElement.getAttribute('aria-invalid')==='true'&&!!document.activeElement.getAttribute('aria-describedby')"));
  await change('Apertura Lunes','20:00'); await change('Cierre Lunes','19:00'); await click('Guardar horarios');
  assert.ok(await evaluate("document.body.textContent.includes('El cierre debe ser posterior')"));
  assert.equal((await puts()).length,0);
  await toggle('Lunes'); await click('Guardar horarios');
  await until("document.body.textContent.includes('Horarios guardados correctamente.')",'Closed day save failed.');
  assert.deepEqual((await puts())[0].data[0],{dayOfWeek:1,isClosed:true,openTime:'',closeTime:''});
});

for (const status of [400,503]) test('guardado '+status+': doble envío bloqueado, error visible, borrador preservado y reintento', async () => {
  await reset('north'); await change('Apertura Lunes','11:00');
  await evaluate("window.hoursAudit.fixture.failSave="+status+";window.hoursAudit.fixture.saveDelay=450");
  await doubleSave();
  await until("window.hoursAudit.calls.some(c=>c.method==='put')",'Save did not start.');
  assert.ok(await evaluate("document.querySelector('[data-day=\"1\"] input').disabled||document.querySelector('[data-day=\"1\"] input').closest('fieldset').disabled"));
  await until("document.body.textContent.includes('Conservamos tus cambios')",'Save error missing.');
  assert.equal((await puts()).length,1); assert.equal((await values())[0].openTime,'11:00');
  assert.equal(await evaluate("document.body.textContent.includes('Horarios guardados correctamente.')"),false);
  assert.ok(await evaluate("document.body.textContent.includes("+JSON.stringify(status===400?'horario rechazado por la API':'Ocurrió un problema inesperado en el servidor.')+")"));
  await evaluate("window.hoursAudit.fixture.failSave=0;window.hoursAudit.fixture.saveDelay=0");
  await click('Guardar horarios'); await until("document.body.textContent.includes('Horarios guardados correctamente.')",'Retry failed.');
  assert.equal((await puts()).length,2); assert.equal((await values())[0].openTime,'11:00');
});

test('GET lento anterior no reemplaza la sede actual aunque el transporte ignore cancelación', async () => {
  await reset(); await evaluate("window.hoursAudit.fixture.loadDelay=600;window.hoursAudit.fixture.ignoreAbort=true");
  await change('Sede de prueba','north');
  await until("window.hoursAudit.calls.some(c=>c.path.includes('/north/'))",'Slow GET did not start.');
  await evaluate("window.hoursAudit.fixture.loadDelay=0"); await select('south');
  assert.equal((await values())[0].openTime,'10:00');
  await wait(750); assert.equal((await values())[0].openTime,'10:00');
  assert.equal((await puts()).length,0);
});

test('borradores por sede se conservan, all avisa y descartar solo afecta a la sede actual', async () => {
  await reset('north'); await change('Apertura Lunes','12:00');
  await select('south'); assert.equal((await values())[0].openTime,'10:00');
  assert.ok(await evaluate("document.body.textContent.includes('cambios sin guardar en otra sede')"));
  await change('Apertura Lunes','14:00'); await select('all');
  assert.ok(await evaluate("document.body.textContent.includes('cambios sin guardar en 2 sedes')"));
  await select('north'); assert.equal((await values())[0].openTime,'12:00');
  await click('Descartar cambios'); assert.equal((await values())[0].openTime,'09:00');
  await select('south'); assert.equal((await values())[0].openTime,'14:00');
  assert.equal((await puts()).length,0);
});

test('cambio de workspace, cuenta y logout: borradores y cargas anteriores no cruzan identidad', async () => {
  await reset('north'); await change('Apertura Lunes','12:00');
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.setIdentity(me)})()");
  await until("document.querySelector('[data-day=\"1\"] input[type=time]')?.value==='13:30'",'New workspace did not load own hours.');
  assert.equal(await evaluate("document.body.textContent.includes('cambios sin guardar en otra sede')"),false);
  await change('Apertura Lunes','14:00');
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.store.getState().me);me.user.id='another-user';a.setIdentity(me)})()");
  await until("document.querySelector('[data-day=\"1\"] input[type=time]')?.value==='13:30'",'New account inherited draft.');
  await evaluate("window.hoursAudit.setIdentity(null)");
  await until("document.body.textContent.includes('No tienes permiso')",'Logout did not remove editor.');
  assert.equal(await evaluate("document.querySelectorAll('[data-day]').length"),0); assert.equal((await puts()).length,0);
  await reset(); await evaluate("window.hoursAudit.fixture.loadDelay=500;window.hoursAudit.fixture.ignoreAbort=true");
  await change('Sede de prueba','north'); await until("window.hoursAudit.calls.length>0",'GET did not start.');
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.fixture.loadDelay=0;a.setIdentity(me)})()");
  await until("document.querySelector('[data-day=\"1\"] input[type=time]')?.value==='13:30'",'Workspace loading switch failed.');
  await wait(650); assert.equal((await values())[0].openTime,'13:30'); assert.equal((await puts()).length,0);
});

test('guardado durante cambio de sede conserva destino e invalida solo sus horas/disponibilidad', async () => {
  await reset('north'); await change('Apertura Lunes','11:45');
  await evaluate("(()=>{const c=window.hoursAudit.client;for(const key of [['workspace','workspace-a','reservations','availability','north','s','2026-10-08'],['workspace','workspace-a','reservations','availability','south','s','2026-10-08'],['workspace','workspace-b','reservations','availability','north','s','2026-10-08'],['workspace','workspace-b','hours','north']])c.setQueryData(key,[]);window.hoursAudit.fixture.saveDelay=500})()");
  await click('Guardar horarios'); await until("window.hoursAudit.calls.some(c=>c.method==='put')",'Save did not start.');
  await select('south'); assert.equal((await values())[0].openTime,'10:00');
  assert.ok(await evaluate("document.body.textContent.includes('Guardando los horarios de la sede anterior')"));
  await wait(700);
  const req=(await puts())[0]; assert.equal(req.path,'/business/locations/north/hours'); assert.equal(req.workspace,'workspace-a');
  assert.equal(await evaluate("window.hoursAudit.client.getQueryState(['workspace','workspace-a','reservations','availability','north','s','2026-10-08']).isInvalidated"),true);
  for (const key of [['workspace','workspace-a','reservations','availability','south','s','2026-10-08'],['workspace','workspace-b','reservations','availability','north','s','2026-10-08'],['workspace','workspace-b','hours','north']])
    assert.equal(await evaluate("window.hoursAudit.client.getQueryState("+JSON.stringify(key)+").isInvalidated"),false);
  assert.equal(await evaluate("document.body.textContent.includes('Horarios guardados correctamente.')"),false);
  await select('north'); assert.equal((await values())[0].openTime,'11:45');
  assert.ok(await evaluate("document.body.textContent.includes('Horarios guardados correctamente.')"));
});

test('save pendiente al cambiar identidad no muestra éxito ni escribe/cacha en workspace nuevo', async () => {
  await reset('north'); await change('Apertura Lunes','12:30'); await evaluate("window.hoursAudit.fixture.saveDelay=600");
  await click('Guardar horarios'); await until("window.hoursAudit.calls.some(c=>c.method==='put')",'Save did not start.');
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';a.setIdentity(me)})()");
  await until("document.querySelector('[data-day=\"1\"] input[type=time]')?.value==='13:30'",'New identity did not load.');
  await wait(750);
  assert.equal((await values())[0].openTime,'13:30');
  assert.equal((await puts()).length,1); assert.equal((await puts())[0].workspace,'workspace-a');
  assert.equal(await evaluate("window.hoursAudit.client.getQueryData(['workspace','workspace-a','hours','north'])===undefined"),true);
  assert.equal(await evaluate("document.body.textContent.includes('Horarios guardados correctamente.')"),false);
});

test('sin READ/UPDATE y GET 403/503: sin propuesta incorrecta ni PUT; recuperación explícita', async () => {
  await reset();
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.BUSINESS_HOURS=[];a.setIdentity(me);a.store.getState().setSelectedLocationId('empty')})()");
  await until("document.body.textContent.includes('No tienes permiso')",'Read permission missing.');
  assert.equal(await evaluate("window.hoursAudit.calls.length"),0); assert.equal(await evaluate("document.querySelectorAll('[data-day]').length"),0);
  await evaluate("(()=>{const a=window.hoursAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.BUSINESS_HOURS=['READ'];a.setIdentity(me)})()");
  await ready(); assert.ok(await evaluate("document.body.textContent.includes('Solo lectura')"));
  assert.equal(await evaluate("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='Guardar horarios')"),false);
  assert.equal(await evaluate("document.querySelectorAll('section form').length"),1);
  for(const status of [403,503]) {
    await reset(); await evaluate("window.hoursAudit.fixture.failLoad="+status); await change('Sede de prueba','empty');
    await until("document.body.textContent.includes('No se pudieron cargar los horarios')",'GET failure hidden.');
    assert.equal(await evaluate("document.querySelectorAll('[data-day]').length"),0);
    assert.equal(await evaluate("document.body.textContent.includes('sugerencia editable')"),false);
    assert.equal((await puts()).length,0);
    await evaluate("window.hoursAudit.fixture.failLoad=0"); await click('Reintentar'); await ready();
  }
});

test('responsive 360/768/1280: etiquetas, teclado, foco visible y controles sin desbordar', async () => {
  await reset('empty');
  for(const width of [360,768,1280]) {
    await metrics(width);
    assert.equal(await evaluate("document.documentElement.scrollWidth<=innerWidth"),true);
    assert.ok(await evaluate("[...document.querySelectorAll('section input')].every(e=>e.labels.length||e.getAttribute('aria-label'))"));
    await evaluate("document.querySelector('section input[type=radio]').focus()");
    await key('Tab','Tab',9);
    assert.ok(await evaluate("(()=>{const s=getComputedStyle(document.activeElement);return s.outlineStyle==='solid'||s.boxShadow!=='none'})()"));
    await evaluate("window.scrollTo(0,0)"); await capture('hours-top-'+width);
    await evaluate("document.querySelector('[data-day=\"1\"]').scrollIntoView({block:'start'})"); await capture('hours-week-'+width);
    await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Guardar horarios').scrollIntoView({block:'center'})");
    assert.ok(await evaluate("(()=>{const b=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Guardar horarios');const r=b.getBoundingClientRect();return r.width>=44&&r.height>=44&&r.left>=0&&r.right<=innerWidth&&b.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2))})()"));
    await capture('hours-footer-'+width);
  }
  assert.equal((await puts()).length,0);
});
