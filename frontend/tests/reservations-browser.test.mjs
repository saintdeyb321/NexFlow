import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './reservations-preview.server.mjs';

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
const change = async (label, value) => evaluate(`(()=>{const l=[...document.querySelectorAll('label')].find(e=>e.textContent.replace('*','').trim()===${JSON.stringify(label)});const e=document.getElementById(l.htmlFor);Object.getOwnPropertyDescriptor(e.tagName==='SELECT'?HTMLSelectElement.prototype:HTMLInputElement.prototype,'value').set.call(e,${JSON.stringify(value)});e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}))})()`);
const capture = async name => {
  const image = await send('Page.captureScreenshot', { format: 'png' });
  writeFileSync(join('node_modules/.tmp', `ux01b-${name}.png`), Buffer.from(image.data, 'base64'));
};
const metrics = width => send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
const weeklyReads = () => evaluate("window.reservationAudit.calls.filter(call=>call.path==='/reservations'&&call.method==='get')");

before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'ux01b-browser-'));
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
  await until("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length===4", 'Weekly agenda did not load.');
});
after(async () => {
  if (socket?.readyState === WebSocket.OPEN) {
    const closed = new Promise(done => socket.addEventListener('close', done, { once: true }));
    await send('Browser.close').catch(() => {});
    await Promise.race([closed, wait(3000)]); socket.close();
  }
  if (browser) {
    await Promise.race([exit, wait(3000)]);
    if (browser.exitCode === null) { browser.kill(); await Promise.race([exit, wait(3000)]); }
  }
  await preview?.server.close();
  // Only this newly created profile inside the test cache is removed.
  const root = resolve('node_modules/.tmp') + '/';
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'ux01b-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});

test('desktop: siete columnas, tres horas reales y una sola lectura semanal agregada', async () => {
  assert.equal(await evaluate("document.querySelectorAll('th[scope=col]').length"), 8);
  assert.equal(await evaluate("document.querySelectorAll('th[scope=row]').length"), 3);
  const reads = await weeklyReads(); assert.equal(reads.length, 1); assert.equal(reads[0].params.locationId, 'all');
  assert.ok(reads[0].params.from && reads[0].params.to); assert.equal(reads[0].params.date, undefined);
  await capture('desktop');
});
test('responsive 360/tablet: selector de día, tarjetas verticales y sin overflow de página', async () => {
  const from = (await weeklyReads())[0].params.from;
  for (const width of [360, 768]) {
    await metrics(width); await change('Día de la agenda', from);
    await until("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length===4", 'Selected mobile day did not show all cards.');
    assert.equal(await evaluate('document.documentElement.scrollWidth<=window.innerWidth'), true);
    assert.equal(await evaluate("[...document.querySelectorAll('table')].some(e=>e.getClientRects().length)"), false);
    await capture(String(width));
    if (width === 360) {
      await evaluate("[...document.querySelectorAll('label')].find(e=>e.textContent==='Día de la agenda').scrollIntoView({block:'start'})");
      await capture('360-agenda'); await evaluate('window.scrollTo(0,0)');
    }
  }
  await metrics(1280);
});
test('teclado: foco visible y activación por Enter en navegación semanal', async () => {
  await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Esta semana').focus()");
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Tab', code: 'Tab', windowsVirtualKeyCode: 9 });
  assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"), 'Semana siguiente');
  assert.equal(await evaluate("getComputedStyle(document.activeElement).outlineStyle==='solid'"), true);
  const previous = (await weeklyReads()).length;
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: 'Enter', code: 'Enter', text: '\r', unmodifiedText: '\r', windowsVirtualKeyCode: 13, nativeVirtualKeyCode: 13 });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: 'Enter', code: 'Enter', windowsVirtualKeyCode: 13 });
  await until(`window.reservationAudit.calls.filter(c=>c.path==='/reservations'&&c.method==='get').length===${previous + 1}`, 'Keyboard navigation did not request next week.');
});
test('salto de fecha cruza año y conserva semana anterior identificada durante carga', async () => {
  await evaluate('window.reservationAudit.fixture.delay=700');
  await change('Ir a fecha', '2030-12-31');
  await until("document.body.textContent.includes('Se muestra todavía')", 'Previous week was not identified while loading.');
  assert.equal(await evaluate("[...document.querySelectorAll('button[aria-label^=\"Nueva reserva para\"]')].every(e=>e.disabled)"), true);
  await until("!document.body.textContent.includes('Se muestra todavía')&&document.body.textContent.includes('2031')", 'New week did not replace previous week.');
  const reads = await weeklyReads();
  assert.deepEqual(reads.at(-1).params, { locationId: 'all', from: '2030-12-30', to: '2031-01-06', timeZone: 'America/Lima' });
  await evaluate('window.reservationAudit.fixture.delay=0');
});
test('filtros/lista/búsqueda conservan resumen completo y estados históricos', async () => {
  await change('Estado', 'Cancelled'); await click('Lista');
  await until("document.querySelectorAll('tbody tr').length===1", 'Cancelled filter did not update list.');
  assert.ok(await evaluate("document.querySelector('tbody').textContent.includes('Diego Prueba')"));
  assert.ok(await evaluate("document.querySelector('[aria-label=\"Resumen de la semana mostrada\"]').textContent.includes('Total semanal4')"));
  await change('Estado', 'all'); await change('Buscar en esta semana', 'jose');
  await until("document.querySelectorAll('tbody tr').length===1&&document.querySelector('tbody').textContent.includes('José Prueba')", 'Weekly accent-insensitive search failed.');
  assert.equal(await evaluate("document.querySelector('tbody').textContent.includes('Reagendar')"), false);
  await change('Buscar en esta semana', ''); await click('Semana');
});
test('día histórico preseleccionado no consulta slots ni permite crear una reserva vencida', async () => {
  await change('Ir a fecha', '2018-07-04');
  await until("document.body.textContent.includes('2018')&&!document.body.textContent.includes('Se muestra todavía')", 'Historical week did not load.');
  const prior = await evaluate("window.reservationAudit.calls.filter(c=>c.path==='/reservations/availability').length");
  await evaluate("document.querySelector('button[aria-label^=\"Nueva reserva para lunes\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Historical create dialog did not open.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value"), '2018-07-02');
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('pertenece al historial')"));
  await change('Sede', 'location-a'); await change('Servicio', 'service-a');
  assert.equal(await evaluate("window.reservationAudit.calls.filter(c=>c.path==='/reservations/availability').length"), prior);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cita').disabled"), true);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click()");
  await change('Ir a fecha', '2030-12-31');
  await until("document.body.textContent.includes('2031')&&!document.body.textContent.includes('Se muestra todavía')", 'Future week did not return.');
});
test('crear desde un día: sede concreta, servicio elegible, slot real y refresco semanal', async () => {
  await evaluate("document.querySelector('button[aria-label^=\"Nueva reserva para lunes\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Create dialog did not open.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value"), '2030-12-30');
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] select')][0].querySelector('option[value=all]')===null"), true);
  await change('Sede', 'location-a'); await change('Servicio', 'service-a');
  await until("document.querySelector('[role=dialog]').textContent.includes('10:00')", 'Real available slot did not load.');
  const timeButton = await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.includes('10:00')).textContent.trim()");
  await click(timeButton);
  await change('Nombre del Cliente', 'Cliente Prueba UI'); await change('Teléfono (WhatsApp)', '+51900000000');
  const prior = (await weeklyReads()).length;
  await click('Confirmar Cita');
  await until("!document.querySelector('[role=dialog]')&&document.body.textContent.includes('Cliente Prueba UI')", 'Created reservation did not refresh weekly view.');
  const post = await evaluate("window.reservationAudit.calls.find(c=>c.path==='/reservations'&&c.method==='post')");
  assert.equal(post.data.locationId, 'location-a'); assert.equal(post.data.dateTime, '2030-12-30T15:00:00Z');
  assert.equal((await weeklyReads()).length, prior + 1);
  assert.ok(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/reservations/availability'&&c.params.locationId==='location-a'&&c.params.date==='2030-12-30')"));
});
test('reagendar preserva contrato local y refresca agenda, lista y resumen', async () => {
  const prior = (await weeklyReads()).length;
  await evaluate("document.querySelector('button[aria-label=\"Reagendar reserva de Ana Prueba\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Edit dialog did not open.');
  await change('Nueva Fecha', '2030-12-31'); await change('Nueva Hora (HH:mm)', '11:15');
  await click('Confirmar Cambio');
  await until(`window.reservationAudit.calls.filter(c=>c.path==='/reservations'&&c.method==='get').length===${prior + 1}&&!document.querySelector('[role=dialog]')`, 'Edit did not refresh week.');
  assert.ok(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/reservations/b'&&c.method==='put'&&c.data.newDateTime==='2030-12-31T11:15:00')"));
  assert.ok(await evaluate("document.body.textContent.includes('11:15')"));
});
test('cancelar y completar usan la sede del artefacto, confirmación y refresco del resumen', async () => {
  const prior = (await weeklyReads()).length;
  await evaluate("document.querySelector('button[aria-label=\"Cancelar reserva de Ana Prueba\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Cancel confirmation missing.');
  await click('Sí, cancelar');
  await until(`window.reservationAudit.calls.filter(c=>c.path==='/reservations'&&c.method==='get').length===${prior + 1}&&!document.querySelector('[role=dialog]')`, 'Cancel did not refresh week.');
  await evaluate("document.querySelector('button[aria-label=\"Completar reserva de Cliente Prueba UI\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Complete confirmation missing.'); await click('Sí, completar');
  await until(`window.reservationAudit.calls.filter(c=>c.path==='/reservations'&&c.method==='get').length===${prior + 2}&&!document.querySelector('[role=dialog]')`, 'Complete did not refresh week.');
});
test('workspace/sede cambian sin retener datos ajenos; permisos bloquean mutaciones', async () => {
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.delay=400;a.store.setState({me:{...a.store.getState().me,workspace:{id:'workspace-b',name:'B',status:'Active'}},selectedLocationId:'location-b'})})()");
  await until("document.body.textContent.includes('Cargando')&&!document.body.textContent.includes('Cliente Prueba UI')", 'Old workspace data remained visible.');
  await until("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length===2", 'Location filter did not load own results.');
  const reads = await weeklyReads(); assert.equal(reads.at(-1).params.locationId, 'location-b');
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.delay=0;a.store.setState({me:{...a.store.getState().me,capabilities:{...a.store.getState().me.capabilities,RESERVATIONS:['READ']}}})})()");
  await until("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Nueva reserva')?.disabled", 'Create permission was bypassed.');
  assert.equal(await evaluate("document.querySelectorAll('button[aria-label^=\"Cancelar reserva\"]').length"), 0);
});
test('vacío y error son distintos; recuperación conserva el contrato sin llamadas diarias', async () => {
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.empty=true;void a.client.invalidateQueries({queryKey:['workspace','workspace-b','reservations','list']})})()");
  await until("document.body.textContent.includes('Sin reservas para mostrar esta semana')", 'Empty week state missing.');
  assert.equal(await evaluate("document.querySelectorAll('th[scope=row]').length"), 0);
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.fail=true;void a.client.invalidateQueries({queryKey:['workspace','workspace-b','reservations','list']})})()");
  await until("document.body.textContent.includes('temporalmente indisponible')", 'Dependency error was hidden.');
  await evaluate('window.reservationAudit.fixture.fail=false;window.reservationAudit.fixture.empty=false');
  await click('Reintentar'); await until("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length===2", 'Retry did not recover.');
  assert.equal((await weeklyReads()).every(read => read.params.from && read.params.to && !read.params.date), true);
});

const resetAgenda = async (options = {}) => {
  await metrics(1280);
  await evaluate("(()=>{const a=window.reservationAudit;const options="+JSON.stringify(options)+";a.setShowProfile(false);sessionStorage.clear();a.profiles.clear();a.zones.clear();Object.assign(a.fixture,{delay:0,fail:false,empty:false,timeZone:'America/Lima',contextDelay:0,contextStatus:0,contextResponse:undefined,rows:null},options);const me=structuredClone(a.defaultIdentity);me.workspace.id=options.workspace??'workspace-zone-a';me.user.id=options.user??'zone-user';me.capabilities.BUSINESS_PROFILE=[];if(options.permissions)me.capabilities.RESERVATIONS=options.permissions;if(options.entitlements)me.entitlements=options.entitlements;a.calls.length=0;a.setIdentity(me)})()");
};
const agendaReady = () => until("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length===4", 'Agenda did not load after context.');
const contextReads = () => evaluate("window.reservationAudit.calls.filter(c=>c.path==='/reservations/context')");

test('UX-06B0: Nueva reserva conserva el borrador al recuperar foco con la misma zona', async () => {
  await resetAgenda(); await agendaReady();
  await change('Ir a fecha', '2030-12-31');
  await until("document.querySelector('input[type=date]').value==='2030-12-30'&&!document.body.textContent.includes('Se muestra todavía')", 'Chosen week did not load.');
  await click('Nueva reserva');
  await until("document.querySelector('[role=dialog]')", 'Create dialog missing.');
  await change('Sede', 'location-a'); await change('Servicio', 'service-a');
  await change('Fecha', '2030-12-31');
  await change('Nombre del Cliente', 'Borrador seguro'); await change('Teléfono (WhatsApp)', '+51912345678');
  await until("document.querySelector('[role=dialog]').textContent.includes('10:00')", 'Available slot missing.');
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.contextDelay=800;a.focusManager.setFocused(false);a.focusManager.setFocused(true)})()");
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Focus revalidation missing.');
  assert.equal(await evaluate("document.querySelector('[role=dialog]')!==null"), true, 'Focus revalidation unmounted the create draft.');
  assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cita').disabled"), true);
  assert.equal(await evaluate("document.querySelector('[role=dialog]').textContent.includes('10:00')"), false);
  await until("!document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Context did not finish revalidating.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=text]:not(:disabled)').value"), 'Borrador seguro');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=tel]').value"), '+51912345678');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value"), '2030-12-31');
  assert.equal(await evaluate("document.querySelector('input[type=date]').value"), '2030-12-30');
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click();window.reservationAudit.focusManager.setFocused(undefined)");
});

test('UX-06B0: Reagendar conserva fecha y hora al recuperar foco con la misma zona', async () => {
  await resetAgenda(); await agendaReady();
  await evaluate("document.querySelector('button[aria-label=\"Reagendar reserva de Ana Prueba\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Reschedule dialog missing.');
  await change('Nueva Fecha', '2030-12-31'); await change('Nueva Hora (HH:mm)', '11:15');
  await evaluate("(()=>{const a=window.reservationAudit;a.fixture.contextDelay=800;a.focusManager.setFocused(false);a.focusManager.setFocused(true)})()");
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Focus revalidation missing.');
  assert.equal(await evaluate("document.querySelector('[role=dialog]')!==null"), true, 'Focus revalidation unmounted the reschedule draft.');
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cambio').disabled"), true);
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=time]').getClientRects().length"), 0);
  await until("!document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Context did not finish revalidating.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value"), '2030-12-31');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=time]').value"), '11:15');
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click();window.reservationAudit.focusManager.setFocused(undefined)");
});

const fillCreateDraft = async () => {
  await click('Nueva reserva'); await until("document.querySelector('[role=dialog]')", 'Create draft missing.');
  await change('Sede', 'location-a'); await change('Servicio', 'service-a'); await change('Fecha', '2030-12-31');
  await change('Nombre del Cliente', 'Cliente conservado'); await change('Teléfono (WhatsApp)', '+51912345678');
  await until("document.querySelector('[role=dialog]').textContent.includes('10:00')", 'Draft availability missing.');
  await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.includes('10:00')).click()");
};
const revalidateContext = options => evaluate("(()=>{const a=window.reservationAudit;Object.assign(a.fixture,{contextDelay:400},"+JSON.stringify(options??{})+");void a.client.invalidateQueries({queryKey:['workspace','workspace-zone-a','reservations','context'],exact:true})})()");

test('UX-06B0: una zona nueva conserva cliente, recalcula fecha y descarta el slot seleccionado', async () => {
  await resetAgenda(); await agendaReady(); await fillCreateDraft();
  await revalidateContext({ timeZone: 'America/New_York' });
  await until("document.querySelector('[role=dialog]').textContent.includes('La zona horaria cambió')", 'Changed zone was not reviewed in the open draft.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=text]:not(:disabled)').value"), 'Cliente conservado');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=tel]').value"), '+51912345678');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value===new Intl.DateTimeFormat('en-CA',{timeZone:'America/New_York',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date())"), true);
  assert.equal(await evaluate("document.querySelectorAll('[role=dialog] button[aria-pressed=true]').length"), 0);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cita').disabled"), true);
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click()");
});

test('UX-06B0: Reagendar recalcula fecha y exige elegir una hora nueva cuando cambia la zona', async () => {
  await resetAgenda(); await agendaReady();
  await evaluate("document.querySelector('button[aria-label=\"Reagendar reserva de Ana Prueba\"]').click()");
  await until("document.querySelector('[role=dialog]')", 'Reschedule draft missing.');
  await change('Nueva Fecha', '2030-12-31'); await change('Nueva Hora (HH:mm)', '11:15');
  await revalidateContext({ timeZone: 'America/New_York' });
  await until("document.querySelector('[role=dialog]').textContent.includes('La zona horaria cambió')", 'Reschedule zone change missing.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=time]').value"), '');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value!== '2030-12-31'"), true);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cambio').disabled"), true);
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click()");
});

test('UX-06B0: 503 conserva el borrador bloqueado y recupera sin enviar operaciones', async () => {
  await resetAgenda(); await agendaReady(); await fillCreateDraft();
  await revalidateContext({ contextStatus: 503 });
  await until("document.querySelector('[role=dialog]')?.textContent.includes('No se pudo cargar la zona horaria')", 'Transient error discarded the draft.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=tel]').value"), '+51912345678');
  assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
  assert.equal(await evaluate("document.querySelector('[role=dialog]').textContent.includes('10:00')"), false);
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Confirmar Cita').disabled"), true);
  // Even programmatic submit cannot use the hidden, unverified slot.
  await evaluate("document.querySelector('[role=dialog] form').dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))");
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("Object.assign(window.reservationAudit.fixture,{contextStatus:0,contextDelay:0});[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Reintentar').click()");
  await until("!document.body.textContent.includes('Cargando zona horaria de la agenda')&&!document.body.textContent.includes('No se pudo cargar la zona horaria')", 'Transient retry did not finish.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=text]:not(:disabled)').value"), 'Cliente conservado');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=date]').value"), '2030-12-31');
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click()");
});

for (const status of [401, 403]) test(`UX-06B0: revalidación ${status} descarta el borrador y exige contexto autorizado nuevo`, async () => {
  await resetAgenda(); await agendaReady(); await fillCreateDraft();
  await revalidateContext({ contextStatus: status });
  await until("document.body.textContent.includes('No se pudo cargar la zona horaria')&&!document.querySelector('[role=dialog]')", 'Unauthorized draft remained mounted.');
  assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
  await evaluate("Object.assign(window.reservationAudit.fixture,{contextStatus:0,contextDelay:0})"); await click('Reintentar'); await agendaReady();
  assert.equal(await evaluate("document.querySelector('[role=dialog]')===null"), true);
  await click('Nueva reserva'); await until("document.querySelector('[role=dialog]')", 'New authorized form missing.');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=tel]').value"), '');
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
  await evaluate("document.querySelector('[role=dialog] button[aria-label=\"Cerrar diálogo\"]').click()");
});

for (const identityChange of ['logout', 'workspace', 'user', 'permissions']) test(`UX-06B0: ${identityChange} durante revalidación elimina el borrador y descarta la respuesta tardía`, async () => {
  await resetAgenda(); await agendaReady(); await fillCreateDraft();
  await revalidateContext({ contextDelay: 800 });
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Pending revalidation missing.');
  const previous = (await contextReads()).length;
  await evaluate("(()=>{const a=window.reservationAudit;const kind="+JSON.stringify(identityChange)+";const me=structuredClone(a.store.getState().me);a.fixture.contextDelay=0;if(kind==='workspace')me.workspace.id='zone-after-change';if(kind==='user')me.user.id='user-after-change';if(kind==='workspace'||kind==='user')a.zones.set(me.workspace.id,'Asia/Kathmandu');if(kind==='permissions')me.capabilities.RESERVATIONS=[];a.setIdentity(kind==='logout'?null:me)})()");
  assert.equal(await evaluate("document.querySelector('[role=dialog]')===null"), true);
  if (identityChange === 'workspace' || identityChange === 'user') await agendaReady();
  else await until("document.body.textContent.includes('No tienes permiso para consultar reservas')", 'Revoked identity still displayed agenda.');
  await until(`window.reservationAudit.calls.filter(c=>c.path==='/reservations/context').slice(0,${previous}).every(c=>c.responded)`, 'Previous context did not finish its delayed response.');
  await evaluate('new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))');
  assert.equal(await evaluate("document.querySelector('[role=dialog]')===null"), true);
  assert.equal(await evaluate("document.body.textContent.includes('Cliente conservado')"), false);
  assert.equal(await evaluate(identityChange === 'user' ? "window.reservationAudit.client.getQueryData(['workspace','workspace-zone-a','reservations','context']).timeZone==='Asia/Kathmandu'" : "window.reservationAudit.client.getQueryData(['workspace','workspace-zone-a','reservations','context'])===undefined"), true);
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.method==='post'||c.method==='put')"), false);
});

test('RESERVATIONS autorizado sin perfil carga semana, fechas y horas exactas sin leer perfil', async () => {
  await resetAgenda({ contextDelay: 400 });
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Context loading missing.');
  assert.equal((await weeklyReads()).length, 0);
  await agendaReady();
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/business/profile')"), false);
  assert.equal((await contextReads()).length, 1); assert.equal((await weeklyReads()).length, 1);
  assert.equal(await evaluate("[...document.querySelectorAll('th[scope=row]')].map(e=>e.textContent).join(',')"), '10:00,12:30,14:00');
  assert.equal(await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Nueva reserva').disabled"), false);
  await change('Ir a fecha', '2030-12-31');
  await until("document.body.textContent.includes('2031')&&!document.body.textContent.includes('Se muestra todavía')", 'Cross-year week missing.');
  assert.deepEqual((await weeklyReads()).at(-1).params, { locationId: 'all', from: '2030-12-30', to: '2031-01-06', timeZone: 'America/Lima' });
  await click('Lista'); await click('Semana'); assert.equal((await contextReads()).length, 1);
  await capture('timezone-permission-resolved');
});

test('sin RESERVATIONS/READ no consulta ni contexto ni semana, aunque exista caché previa', async () => {
  await resetAgenda({ permissions: [] });
  await until("document.body.textContent.includes('No tienes permiso para consultar reservas')", 'Reservation denial missing.');
  assert.equal((await contextReads()).length, 0); assert.equal((await weeklyReads()).length, 0);
  assert.equal(await evaluate("document.querySelectorAll('article,input[type=date]').length"), 0);
});

test('solo RESERVATIONS/READ carga agenda sin dependencias de permisos de otros módulos', async () => {
  await resetAgenda({ permissions: ['READ'], entitlements: ['RESERVATIONS'] }); await agendaReady();
  assert.equal(await evaluate("window.reservationAudit.calls.every(c=>c.path==='/reservations/context'||c.path==='/reservations')"), true);
  assert.equal(await evaluate("document.querySelector('article[aria-label=\"Reserva de Ana Prueba\"] time').textContent"), '10:00');
  assert.equal(await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Nueva reserva').disabled"), true);
});

for (const status of [401, 403, 503]) test(`contexto ${status} bloquea semana y horas; reintento recupera sin fallback`, async () => {
  await resetAgenda({ contextStatus: status });
  await until("document.body.textContent.includes('No se pudo cargar la zona horaria de la agenda')", 'Context error missing.');
  assert.equal((await weeklyReads()).length, 0);
  assert.equal(await evaluate("document.querySelectorAll('article,th[scope=row],input[type=date]').length"), 0);
  assert.equal(await evaluate("document.body.textContent.includes('Zona horaria:')"), false);
  await evaluate('window.reservationAudit.fixture.contextStatus=0'); await click('Reintentar'); await agendaReady();
  assert.equal((await contextReads()).length, 2); assert.equal((await weeklyReads()).length, 1);
});

test('zona ausente, inválida o Windows sin conversión no muestra horas aproximadas; recuperación explícita', async () => {
  for (const response of [null, {}, { timeZone: '' }, { timeZone: 'not/a-zone' }, { timeZone: 'Eastern Standard Time' }, { timeZone: '+03:00' }]) {
    await resetAgenda({ contextResponse: response });
    await until("document.body.textContent.includes('La zona horaria de la agenda no es compatible')", 'Invalid zone was not rejected.');
    assert.equal((await weeklyReads()).length, 0);
    assert.equal(await evaluate("document.querySelectorAll('article,th[scope=row],input[type=date]').length"), 0);
    await evaluate('window.reservationAudit.fixture.contextResponse=undefined'); await click('Reintentar'); await agendaReady();
  }
});

test('relectura fallida con contexto cacheado oculta horas anteriores y recupera una zona nueva', async () => {
  for (const failure of [403, 503, 'invalid']) {
    await resetAgenda(); await agendaReady();
    const before = (await weeklyReads()).length;
    await evaluate("(()=>{const a=window.reservationAudit;a.fixture.contextDelay=200;"+(failure === 'invalid' ? "a.fixture.contextResponse={timeZone:'not/a-zone'};" : "a.fixture.contextStatus="+failure+";")+"void a.client.invalidateQueries({queryKey:['workspace','workspace-zone-a','reservations','context'],exact:true})})()");
    await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Cached zone remained visible during refresh.');
    assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
    await until("document.body.textContent.includes('No se pudo cargar la zona horaria de la agenda')", 'Cached context failure was hidden.');
    assert.equal((await weeklyReads()).length, before);
    assert.equal(await evaluate("document.body.textContent.includes('Zona horaria:')"), false);
    await evaluate("Object.assign(window.reservationAudit.fixture,{contextStatus:0,contextResponse:undefined,contextDelay:0,timeZone:'America/New_York'})");
    await click('Reintentar'); await agendaReady();
    assert.ok(await evaluate("document.body.textContent.includes('Zona horaria: America/New_York')"));
    assert.equal((await weeklyReads()).length, before + 1);
    assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/business/profile')"), false);
  }
});

test('contexto y formularios no cruzan workspace/cuenta/permisos ni respuestas anteriores', async () => {
  await resetAgenda({ workspace: 'zone-race-a', contextDelay: 650 });
  await until("window.reservationAudit.calls.some(c=>c.path==='/reservations/context')", 'Slow context missing.');
  await evaluate("(()=>{const a=window.reservationAudit;const me=structuredClone(a.store.getState().me);me.workspace.id='zone-race-b';a.zones.set('zone-race-b','Asia/Kathmandu');a.fixture.contextDelay=0;a.setIdentity(me)})()");
  await agendaReady(); await wait(750);
  assert.ok(await evaluate("document.body.textContent.includes('Zona horaria: Asia/Kathmandu')"));
  assert.equal(await evaluate("document.querySelector('article[aria-label=\"Reserva de Ana Prueba\"] time').textContent"), '20:45');
  assert.equal(await evaluate("window.reservationAudit.client.getQueryData(['workspace','zone-race-a','reservations','context'])===undefined"), true);
  assert.equal((await weeklyReads()).every(c=>c.workspace==='zone-race-b'), true);
  await click('Nueva reserva'); await until("document.querySelector('[role=dialog]')", 'Create form missing.');
  await evaluate("(()=>{const a=window.reservationAudit;const me=structuredClone(a.store.getState().me);me.user.id='zone-other-user';a.fixture.contextDelay=350;a.zones.set('zone-race-b','America/New_York');a.setIdentity(me)})()");
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Account context did not reload.');
  assert.equal(await evaluate("document.querySelector('[role=dialog]')===null"), true);
  assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
  await agendaReady(); assert.ok(await evaluate("document.body.textContent.includes('Zona horaria: America/New_York')"));
  await evaluate("(()=>{const a=window.reservationAudit;const me=structuredClone(a.store.getState().me);me.capabilities.RESERVATIONS=[];a.calls.length=0;a.setIdentity(me)})()");
  await until("document.body.textContent.includes('No tienes permiso para consultar reservas')", 'Revoked read remained visible.');
  assert.equal((await contextReads()).length, 0); assert.equal((await weeklyReads()).length, 0);
  await evaluate("(()=>{const a=window.reservationAudit;const me=structuredClone(a.store.getState().me);me.capabilities.RESERVATIONS=['READ'];a.fixture.contextDelay=0;a.setIdentity(me)})()");
  await agendaReady();
  assert.equal(await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Nueva reserva').disabled"), true);
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/business/profile')"), false);
});

test('render DST New York conserva ambas 01:30 y semana octubre/noviembre', async () => {
  await resetAgenda({ timeZone: 'America/New_York' }); await agendaReady();
  await evaluate("(()=>{const a=window.reservationAudit;const rows=a.rowsFor('workspace-zone-a','2026-10-26').slice(0,2);rows[0].dateTime='2026-11-01T05:30:00Z';rows[1].dateTime='2026-11-01T06:30:00Z';a.fixture.rows=rows})()");
  await change('Ir a fecha', '2026-11-01');
  await until("document.querySelectorAll('th[scope=row]').length===1&&document.querySelector('th[scope=row]').textContent==='01:30'", 'Repeated DST hour missing.');
  assert.deepEqual((await weeklyReads()).at(-1).params, { locationId: 'all', from: '2026-10-26', to: '2026-11-02', timeZone: 'America/New_York' });
  assert.equal(await evaluate("document.querySelectorAll('article time').length"), 2);
  assert.equal(await evaluate("[...document.querySelectorAll('article time')].map(e=>e.dateTime).join(',')"), '2026-11-01T05:30:00Z,2026-11-01T06:30:00Z');
  await metrics(360); await change('Día de la agenda', '2026-11-01');
  assert.equal(await evaluate("[...document.querySelectorAll('article')].filter(e=>e.getClientRects().length).length"), 2);
  await metrics(1280);
});

test('PUT autorizado del perfil refresca contexto y slots propios, sin invalidar otro workspace', async () => {
  await resetAgenda(); await agendaReady();
  await evaluate("(()=>{const a=window.reservationAudit;a.client.setQueryData(['workspace','zone-foreign','reservations','context'],{timeZone:'Asia/Kathmandu'});a.client.setQueryData(['workspace','workspace-zone-a','reservations','availability','location-a','service-a','2030-12-30','America/Lima'],[]);a.profiles.set('workspace-zone-a',{...a.initialProfile('workspace-zone-a'),timeZone:'America/Lima'});const me=structuredClone(a.store.getState().me);me.capabilities.BUSINESS_PROFILE=['READ','UPDATE'];a.store.setState({me});a.setShowProfile(true)})()");
  await until("[...document.querySelectorAll('[data-test-profile] button')].some(e=>e.textContent.trim()==='Editar Perfil'&&!e.disabled)", 'Authorized profile missing.');
  await click('Editar Perfil'); await change('Nombre Comercial', 'Nombre confirmado');
  const prior = (await contextReads()).length; await click('Guardar Cambios');
  await until("(()=>{const a=window.reservationAudit;const reads=a.calls.filter(c=>c.path==='/reservations/context');return reads.length==="+(prior+1)+"&&reads.at(-1).responded&&a.client.getQueryState(['workspace','workspace-zone-a','reservations','context']).fetchStatus==='idle'&&document.body.textContent.includes('Zona horaria: America/Lima')})()", 'Profile save did not finish refreshing the Peru zone.');
  assert.equal((await contextReads()).length, prior + 1);
  assert.equal(await evaluate("window.reservationAudit.calls.filter(c=>c.path==='/business/profile'&&c.method==='put').length"), 1);
  assert.equal(await evaluate("window.reservationAudit.client.getQueryState(['workspace','zone-foreign','reservations','context']).isInvalidated"), false);
  assert.equal(await evaluate("window.reservationAudit.client.getQueryState(['workspace','workspace-zone-a','reservations','availability','location-a','service-a','2030-12-30','America/Lima']).isInvalidated"), true);
  await evaluate('window.reservationAudit.setShowProfile(false)');
});

test('perfil legacy fuera de Perú rechaza escritura sin perder edición ni invalidar Reservas', async () => {
  await resetAgenda(); await agendaReady();
  await evaluate("(()=>{const a=window.reservationAudit;a.client.setQueryData(['workspace','workspace-zone-a','reservations','availability','location-a','service-a','2030-12-30','America/Lima'],[]);a.profiles.set('workspace-zone-a',{...a.initialProfile('workspace-zone-a'),timeZone:'America/New_York'});const me=structuredClone(a.store.getState().me);me.capabilities.BUSINESS_PROFILE=['READ','UPDATE'];a.store.setState({me});a.setShowProfile(true)})()");
  await until("[...document.querySelectorAll('[data-test-profile] button')].some(e=>e.textContent.trim()==='Editar Perfil'&&!e.disabled)", 'Legacy profile missing.');
  await click('Editar Perfil'); await change('Nombre Comercial', 'Borrador conservado');
  const prior = (await contextReads()).length; await click('Guardar Cambios');
  await until("document.body.textContent.includes('NexFlow V1 utiliza exclusivamente la zona horaria America/Lima.')", 'Foreign zone write was not rejected.');
  assert.equal(await evaluate("[...document.querySelectorAll('[data-test-profile] button')].some(e=>e.textContent.trim()==='Guardar Cambios'&&!e.disabled)"), true);
  assert.equal(await evaluate("document.querySelector('[data-test-profile] input').value"), 'Borrador conservado');
  assert.equal(await evaluate("window.reservationAudit.profiles.get('workspace-zone-a').timeZone"), 'America/New_York');
  assert.notEqual(await evaluate("window.reservationAudit.profiles.get('workspace-zone-a').commercialName"), 'Borrador conservado');
  assert.equal((await contextReads()).length, prior);
  assert.equal(await evaluate("window.reservationAudit.calls.filter(c=>c.path==='/business/profile'&&c.method==='put').length"), 1);
  assert.equal(await evaluate("window.reservationAudit.client.getQueryState(['workspace','workspace-zone-a','reservations','context']).isInvalidated"), false);
  assert.equal(await evaluate("window.reservationAudit.client.getQueryState(['workspace','workspace-zone-a','reservations','availability','location-a','service-a','2030-12-30','America/Lima']).isInvalidated"), false);
  await evaluate('window.reservationAudit.setShowProfile(false)');
});

test('cambio de zona desde otra sesión se detecta antes de mostrar una semana nueva', async () => {
  await resetAgenda(); await agendaReady();
  await evaluate("(()=>{const a=window.reservationAudit;a.profiles.set('workspace-zone-a',{...a.initialProfile('workspace-zone-a'),timeZone:'America/New_York'})})()");
  await change('Ir a fecha', '2030-07-02');
  await until("document.body.textContent.includes('Zona horaria: America/New_York')", 'A new weekly read used stale context after an external profile change.');
  await agendaReady();
  assert.equal(await evaluate("document.querySelector('article[aria-label=\"Reserva de Ana Prueba\"] time').textContent"), '11:00');
  assert.equal(await evaluate("document.querySelector('input[type=date]').value"), '2030-07-01');
  assert.equal((await weeklyReads()).at(-1).params.from, '2030-07-01');
  assert.equal((await weeklyReads()).at(-1).params.timeZone, 'America/New_York');
});

test('volver a una sesión abierta revalida zona externa antes de recuperar la agenda', async () => {
  await resetAgenda(); await agendaReady();
  const before = (await contextReads()).length;
  await evaluate("(()=>{const a=window.reservationAudit;a.focusManager.setFocused(false);a.profiles.set('workspace-zone-a',{...a.initialProfile('workspace-zone-a'),timeZone:'America/New_York'});a.fixture.contextDelay=200;a.focusManager.setFocused(true)})()");
  await until("document.body.textContent.includes('Cargando zona horaria de la agenda')", 'Focus did not revalidate stale zone.');
  assert.equal(await evaluate("document.querySelectorAll('article').length"), 0);
  await until("document.body.textContent.includes('Zona horaria: America/New_York')", 'External zone remained cached after focus.');
  await agendaReady();
  assert.equal((await contextReads()).length, before + 1);
  assert.equal(await evaluate("window.reservationAudit.calls.some(c=>c.path==='/business/profile')"), false);
  await evaluate('window.reservationAudit.focusManager.setFocused(undefined)');
});
