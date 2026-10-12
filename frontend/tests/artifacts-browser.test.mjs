import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './artifacts-preview.server.mjs';

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
  writeFileSync(join('node_modules/.tmp', `pdf-recovery-${name}.png`), Buffer.from(image.data, 'base64'));
};
const metrics = width => send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
const weeklyReads = () => evaluate("window.reservationAudit.calls.filter(call=>call.path==='/reservations'&&call.method==='get')");

before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'pdf-recovery-browser-'));
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
  await until("document.querySelectorAll('h3').length===2", 'Artifact components did not load.');
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
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'pdf-recovery-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});

const panel = scope => `[...document.querySelectorAll('h3')].find(e=>e.textContent==='Folleto ${scope}')?.closest('.mb-5')`;
const panelClick = async (scope, text) => evaluate(`(()=>{const p=${panel(scope)};const b=[...p.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(text)});if(!b||b.disabled)throw Error('Action blocked');b.click()})()`);
const reset = async (options = {}) => {
  await evaluate("(()=>{const a=window.artifactAudit;const options="+JSON.stringify(options)+";a.states.clear();a.quota.clear();a.calls.length=0;Object.assign(a.fixture,{outcome:'accepted',getError:false,delay:0});const me=structuredClone(a.identity);me.workspace.id='pdf-workspace';me.user.id='pdf-user';if(options.product)a.states.set(a.key(me.workspace.id,'PRODUCT'),{...a.initial(),...options.product});a.setIdentity(me)})()");
  await until("document.querySelectorAll('h3').length===2&&[...document.querySelectorAll('button')].some(b=>b.textContent.trim()==='Crear folleto')", 'Artifact page not ready.');
};
const generate = async (scope = 'PRODUCT', label = 'Crear folleto') => {
  await panelClick(scope, label); await until("document.querySelector('[role=dialog] form')", 'Creation dialog missing.');
  await click('Generar folleto');
};
const setTerminal = async code => {
  await evaluate("(()=>{const a=window.artifactAudit;const k=a.key('pdf-workspace','PRODUCT');a.states.set(k,{...a.states.get(k),status:'FAILED',failureCode:"+JSON.stringify(code)+"});void a.client.invalidateQueries({queryKey:['workspace','pdf-workspace','artifacts','PRODUCT'],exact:true})})()");
  await until(`${panel('PRODUCT')}?.textContent.includes('Reintentar')`, 'Terminal state did not enable retry.');
};

test('doble clic genera una sola solicitud; cancelación conserva cuota y habilita reintento', async () => {
  await reset(); await panelClick('PRODUCT','Crear folleto');
  await until("document.querySelector('[role=dialog] form')", 'Modal missing.');
  await evaluate("(()=>{const b=[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Generar folleto');b.click();b.click()})()");
  await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')&&!document.querySelector('[role=dialog]')`, 'Accepted job lacked cancel action.');
  assert.equal(await evaluate("window.artifactAudit.calls.filter(c=>c.path.endsWith('/generate')).length"),1);
  assert.equal(await evaluate("window.artifactAudit.quota.get('pdf-workspace')"),1);
  await panelClick('PRODUCT','Cancelar generación');
  await until(`${panel('PRODUCT')}?.textContent.includes('Generación cancelada')`, 'Cancellation not confirmed.');
  assert.equal(await evaluate("window.artifactAudit.quota.get('pdf-workspace')"),1);
  await generate('PRODUCT','Reintentar');
  await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Retry did not start.');
  assert.equal(await evaluate("window.artifactAudit.quota.get('pdf-workspace')"),2);
});

for (const [code, message] of [['Catalog.GenerationTimeout','plazo máximo'],['Catalog.GenerationDeliveryFailed','entregar la solicitud']]) test(`FAILED ${code} muestra error y recupera botones sin perder PDF anterior`, async () => {
  await reset({product:{status:'FAILED',pdfUrl:'https://storage.example.test/previous.pdf',failureCode:code}});
  await until(`${panel('PRODUCT')}?.textContent.includes(${JSON.stringify(message)})`, 'Failure reason missing.');
  assert.equal(await evaluate(`${panel('PRODUCT')}.textContent.includes('Ver versión anterior')`),true);
  await panelClick('PRODUCT','Reintentar'); await click('Generar folleto'); await click('Sí, crear nueva versión');
  await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Regeneration not accepted.');
  assert.equal(await evaluate("window.artifactAudit.states.get('pdf-workspace:PRODUCT').pdfUrl"),'https://storage.example.test/previous.pdf');
});

test('respuesta HTTP incierta consulta estado y no permite duplicar el trabajo aceptado', async () => {
  await reset(); await evaluate("window.artifactAudit.fixture.outcome='uncertain'");
  await generate();
  await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Uncertain accepted request was not reconciled.');
  assert.equal(await evaluate(`${panel('PRODUCT')}.textContent.includes('Reintentar')`),false);
  const calls = await evaluate('window.artifactAudit.calls');
  const post = calls.findIndex(c=>c.path.endsWith('/generate'));
  assert.ok(calls.slice(post+1).some(c=>c.method==='get'&&c.scope==='PRODUCT'));
  assert.equal(calls.filter(c=>c.path.endsWith('/generate')).length,1);
});

test('si GET también falla no habilita reintento; consultar estado recupera cancelación', async () => {
  await reset(); await evaluate("Object.assign(window.artifactAudit.fixture,{outcome:'uncertain',getError:true})");
  await generate();
  await until("document.body.textContent.includes('No pudimos confirmar el estado del folleto')", 'Unknown backend state was hidden.');
  assert.equal(await evaluate("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='Generar folleto')"),false);
  await evaluate("window.artifactAudit.fixture.getError=false"); await click('Reintentar');
  await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Authoritative status did not recover.');
  assert.equal(await evaluate("window.artifactAudit.calls.filter(c=>c.path.endsWith('/generate')).length"),1);
});

test('cancelar PRODUCT no altera SERVICE; revocar permisos elimina la operación', async () => {
  await reset(); await generate('PRODUCT'); await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Product not active.');
  await generate('SERVICE'); await until(`${panel('SERVICE')}?.textContent.includes('Cancelar generación')`, 'Service not active.');
  await panelClick('PRODUCT','Cancelar generación'); await until(`${panel('PRODUCT')}?.textContent.includes('Generación cancelada')`, 'Product cancel missing.');
  assert.equal(await evaluate("window.artifactAudit.states.get('pdf-workspace:SERVICE').status"),'GENERATING');
  await evaluate("(()=>{const a=window.artifactAudit;const me=structuredClone(a.identity);me.workspace.id='pdf-workspace';me.capabilities.SERVICES=[];a.setIdentity(me)})()");
  await until("!document.body.textContent.includes('Folleto SERVICE')", 'Revoked scope retained controls.');
});

test('estado de otro workspace/cuenta y respuestas tardías no habilitan controles anteriores', async () => {
  await reset(); await generate(); await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Generation not accepted.');
  await evaluate("(()=>{const a=window.artifactAudit;a.fixture.delay=500;void a.client.invalidateQueries({queryKey:['workspace','pdf-workspace','artifacts','PRODUCT'],exact:true});const me=structuredClone(a.identity);me.workspace.id='other-workspace';me.user.id='other-user';a.fixture.delay=0;a.setIdentity(me)})()");
  await until(`${panel('PRODUCT')}?.textContent.includes('Crear folleto')`, 'Other identity did not load its own state.');
  assert.equal(await evaluate("document.body.textContent.includes('Cancelar generación')"),false);
  assert.equal(await evaluate("window.artifactAudit.client.getQueryData(['workspace','pdf-workspace','artifacts','PRODUCT'])===undefined"),true);
});

test('cuota agotada se muestra sin aceptar una cuarta generación', async () => {
  await reset(); await evaluate("window.artifactAudit.quota.set('pdf-workspace',3)"); await generate();
  await until("document.body.textContent.includes('Cuota diaria agotada')", 'Quota error missing.');
  assert.equal(await evaluate("window.artifactAudit.quota.get('pdf-workspace')"),3);
  assert.equal(await evaluate("window.artifactAudit.states.has('pdf-workspace:PRODUCT')"),false);
});

test('callback exitoso confirmado por GET reemplaza estado y termina el bloqueo', async () => {
  await reset(); await generate(); await until(`${panel('PRODUCT')}?.textContent.includes('Cancelar generación')`, 'Generation missing.');
  await evaluate("(()=>{const a=window.artifactAudit;const k='pdf-workspace:PRODUCT';a.states.set(k,{...a.states.get(k),status:'CURRENT',pdfUrl:'https://storage.example.test/ready.pdf'});void a.client.invalidateQueries({queryKey:['workspace','pdf-workspace','artifacts','PRODUCT'],exact:true})})()");
  await until(`${panel('PRODUCT')}?.textContent.includes('listo para compartir')`, 'Backend completion remained blocked.');
  assert.equal(await evaluate(`${panel('PRODUCT')}.textContent.includes('Cancelar generación')`),false);
});
