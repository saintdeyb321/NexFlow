import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './evolution-navigation-preview.server.mjs';

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
  for (let step = 0; step < 800; step++) { if (await evaluate(`Boolean(${expression})`)) return; await wait(50); }
  throw new Error(message);
};
const click = async text => evaluate(`(()=>{const e=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()===${JSON.stringify(text)}&&e.getClientRects().length);if(!e||e.disabled)throw Error('Button unavailable');e.click()})()`);
const metrics = width => send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });

before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'evolution-nav-browser-'));
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
  await until("window.evolutionNavigation&&document.body.textContent.includes('Bandeja de entrada')", 'Router did not load.');
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
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'evolution-nav-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});


const go = async (path, title) => {
  await evaluate("window.evolutionNavigation=undefined");
  await send('Page.navigate',{url:new URL(path,preview.url).href});
  await until("window.evolutionNavigation&&document.body.textContent.includes("+JSON.stringify(title)+")",'Route did not finish navigation.');
};
test('notificación histórica /conversations abre /inbox y conserva conversación y fragmento',async()=>{
  await go('/inbox','Bandeja de entrada');
  await until("window.evolutionNavigation.calls.some(c=>c.method==='get')",'Notification request missing.');
  await evaluate("document.querySelector('button[aria-label^=Notificaciones]').click()");
  await until("document.body.textContent.includes('Revisa el mensaje')",'Notification missing.');
  await evaluate("[...document.querySelectorAll('button')].find(b=>b.textContent.includes('Revisa el mensaje')).click()");
  await until("location.pathname==='/inbox'&&location.search==='?conversation=chat-a'&&location.hash==='#latest'",'Legacy notification did not redirect.');
  assert.equal(await evaluate("window.evolutionNavigation.calls.filter(c=>c.path==='/notifications/notice-a/read'&&c.method==='put').length"),1);
  assert.equal(await evaluate("window.evolutionNavigation.calls.every(c=>c.workspace==='workspace-a')"),true);
});
test('enlace histórico directo preserva búsqueda y fragmento',async()=>{
  await go('/conversations?conversation=direct-chat#tail','Bandeja de entrada');
  assert.equal(await evaluate("location.pathname+location.search+location.hash"),'/inbox?conversation=direct-chat#tail');
});
test('notificación nueva /inbox navega sin redirección adicional',async()=>{
  await go('/inbox?notice=new','Bandeja de entrada');
  await evaluate("window.evolutionNavigation.fixture.actionUrl='/inbox?conversation=new-chat';window.evolutionNavigation.refreshNotifications()");
  await until("window.evolutionNavigation.calls.filter(c=>c.method==='get').length>=2",'Refresh missing.');
  await evaluate("document.querySelector('button[aria-label^=Notificaciones]').click()");
  await until("document.body.textContent.includes('Revisa el mensaje')",'Notification missing.');
  await evaluate("[...document.querySelectorAll('button')].find(b=>b.textContent.includes('Revisa el mensaje')).click()");
  await until("location.search==='?conversation=new-chat'",'Canonical notification did not navigate.');
});
test('redirección histórica conserva autenticación y acceso al módulo',async()=>{
  await go('/conversations?anonymous=1','Iniciar sesión');
  assert.equal(await evaluate('location.pathname'),'/login');
  await go('/conversations?withoutModule=1','Inicio');
  assert.equal(await evaluate('location.pathname'),'/');
});
test('notificaciones sin permiso READ no consultan ni ofrecen enlaces del workspace',async()=>{
  await go('/inbox?withoutRead=1','Bandeja de entrada');
  await evaluate("document.querySelector('button[aria-label^=Notificaciones]').click()");
  await until("document.body.textContent.includes('No tienes notificaciones')",'Unread permission was bypassed.');
  assert.equal(await evaluate('window.evolutionNavigation.calls.length'),0);
});
test('ruta inexistente ofrece retorno seguro al inicio sin pantalla técnica',async()=>{
  await go('/missing-route','No encontramos esta página');
  assert.equal(await evaluate("document.body.textContent.includes('Unexpected Application Error')"),false);
  await evaluate("[...document.querySelectorAll('a')].find(a=>a.textContent==='Volver al inicio').click()");
  await until("document.body.textContent.includes('Inicio')&&location.pathname==='/'",'Friendly error link failed.');
});
test('error de pantalla queda contenido sin exponer detalles internos',async()=>{
  await go('/inbox?simulateError=1','No pudimos abrir esta página');
  assert.equal(await evaluate("document.body.textContent.includes('PRIVATE_INTERNAL_ERROR_MUST_NOT_BE_SHOWN')"),false);
  assert.equal(await evaluate("[...document.querySelectorAll('button')].some(b=>b.textContent==='Intentar nuevamente')"),true);
});

