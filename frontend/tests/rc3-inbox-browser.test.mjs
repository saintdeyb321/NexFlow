import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './rc3-inbox-preview.server.mjs';

// Existing Chromium/CDP test pattern; real InboxPage/hooks/query cache, mocked HTTP and identity.
// Sprint 0 deliberately keeps the two NF04 acceptance tests red until the authorized UX sprint.
let preview, browser, socket, profile, exited, sequence = 0;
const pending = new Map();
const wait = ms => new Promise(done => setTimeout(done, ms));
const send = (method, params = {}) => new Promise((done, reject) => {
  const id = ++sequence;
  const timer = setTimeout(() => { pending.delete(id); reject(new Error(`CDP timeout: ${method}`)); }, 15_000);
  pending.set(id, { done, reject, timer }); socket.send(JSON.stringify({ id, method, params }));
});
const evaluate = async expression => {
  const result = await send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise: true });
  if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? result.exceptionDetails.text);
  return result.result.value;
};
const until = async expression => {
  for (let i = 0; i < 400; i++) { if (await evaluate(`Boolean(${expression})`)) return; await wait(50); }
  throw new Error('Local inbox fixture did not reach the expected state');
};
const healthyPage = async () => {
  await send('Page.navigate', { url: `${preview.url}?reproduction=${++sequence}` });
  await until("window.rc3Inbox && document.body.textContent.includes('Historial ficticio conservado')");
};

before(async () => {
  preview = await startPreview(); const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'rc3-inbox-browser-'));
  const executable = ['C:/Program Files/Google/Chrome/Application/chrome.exe', 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'].find(existsSync);
  assert.ok(executable, 'Installed Chromium required; no dependencies are downloaded.');
  browser = spawn(executable, ['--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`, '--no-first-run', '--no-default-browser-check',
    '--disable-background-networking', '--disable-sync', '--disable-extensions', '--disable-component-update', 'about:blank'], { windowsHide: true, stdio: 'ignore' });
  exited = new Promise(done => browser.once('exit', done)); const activePort = join(profile, 'DevToolsActivePort');
  for (let i = 0; !existsSync(activePort) && i < 160; i++) await wait(50);
  assert.ok(existsSync(activePort), 'Local debugging port unavailable');
  const port = readFileSync(activePort, 'utf8').split('\n')[0];
  const page = await fetch(`http://127.0.0.1:${port}/json/new?about:blank`, { method: 'PUT' }).then(r => r.json());
  socket = new WebSocket(page.webSocketDebuggerUrl);
  await new Promise((done, reject) => { socket.addEventListener('open', done, { once: true }); socket.addEventListener('error', reject, { once: true }); });
  socket.addEventListener('message', event => {
    const message = JSON.parse(event.data);
    if (message.id) {
      const request = pending.get(message.id); if (!request) return;
      clearTimeout(request.timer); pending.delete(message.id);
      if (message.error) request.reject(new Error(message.error.message)); else request.done(message.result);
    } else if (message.method === 'Fetch.requestPaused') {
      const allowed = /^http:\/\/127\.0\.0\.1:/.test(message.params.request.url) || message.params.request.url.startsWith('data:');
      void send(allowed ? 'Fetch.continueRequest' : 'Fetch.failRequest', allowed ? { requestId: message.params.requestId }
        : { requestId: message.params.requestId, errorReason: 'BlockedByClient' }).catch(() => {});
    }
  });
  await send('Page.enable'); await send('Runtime.enable'); await send('Fetch.enable', { patterns: [{ urlPattern: '*' }] });
  await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false });
});

after(async () => {
  if (socket?.readyState === WebSocket.OPEN) { await send('Browser.close').catch(() => {}); socket.close(); }
  if (browser) { await Promise.race([exited, wait(3000)]); if (browser.exitCode === null) { browser.kill(); await Promise.race([exited, wait(3000)]); } }
  await preview?.server.close();
  const allowedRoot = resolve('node_modules/.tmp').replaceAll('\\', '/') + '/rc3-inbox-browser-';
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(allowedRoot))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});

test('control: bandeja e historial reales aparecen con ambas consultas sanas', async () => {
  await healthyPage();
  assert.equal(await evaluate("document.body.textContent.includes('Bandeja de Entrada')"), true);
  assert.deepEqual(await evaluate('window.rc3Inbox.snapshot()'), { listCount: 1, messageCount: 1, listStatus: 'success', messageStatus: 'success' });
});

for (const panel of ['messages', 'conversations']) test(`NF04: fallo parcial de ${panel} conserva el panel no afectado y su contenido`, async t => {
  await healthyPage(); await evaluate(`window.rc3Inbox.fail(${JSON.stringify(panel)})`);
  await until(`window.rc3Inbox.snapshot().${panel === 'messages' ? 'messageStatus' : 'listStatus'} === 'error'`);
  const snapshot = await evaluate('window.rc3Inbox.snapshot()');
  t.diagnostic(JSON.stringify(snapshot)); // Aggregate state only; no customer payloads or credentials.
  assert.equal(snapshot.listCount, 1); assert.equal(snapshot.messageCount, 1);
  const unaffected = panel === 'messages' ? 'Bandeja de Entrada' : 'Historial ficticio conservado';
  assert.equal(await evaluate(`document.body.textContent.includes(${JSON.stringify(unaffected)})`), true,
    'NF04: cached data exists but InboxPage removes the healthy panel after a partial query failure');
});

const click = label => evaluate(`document.querySelector('button[aria-label="${label}"]').click()`);
for (const panel of ['messages', 'conversations']) test(`NF04: recuperar ${panel} conserva ambos paneles y reintenta solo la consulta afectada`, async () => {
  await healthyPage(); await evaluate(`window.rc3Inbox.fail('${panel}')`);
  await until("document.body.textContent.includes('Los datos pueden estar desactualizados')");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado') && document.body.textContent.includes('Bandeja de Entrada')"), true);
  assert.equal(await evaluate("Array.from(document.querySelectorAll('button')).find(b => b.textContent.includes('Asumir Control')).disabled"), true);
  await evaluate("window.rc3Inbox.configure({fail:'none'}); window.rc3Inbox.calls.splice(0)");
  await click(panel === 'messages' ? 'Reintentar mensajes' : 'Reintentar conversaciones');
  await until("!document.body.textContent.includes('Los datos pueden estar desactualizados')");
  assert.deepEqual(await evaluate('window.rc3Inbox.calls.map(c => c.path)'), [panel === 'messages' ? '/conversations/chat-rc3/messages' : '/conversations']);
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), true);
});

test('NF04: ambos errores conservan caché útil y permiten recuperaciones independientes', async () => {
  await healthyPage(); await evaluate("(async () => { window.rc3Inbox.configure({fail:'both'}); await Promise.all([window.rc3Inbox.refetch('messages'),window.rc3Inbox.refetch('conversations')]); })()");
  await until("document.querySelectorAll('button[aria-label^=Reintentar]').length === 2");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado') && document.body.textContent.includes('Bandeja de Entrada')"), true);
  await evaluate("window.rc3Inbox.configure({fail:'messages'})"); await click('Reintentar conversaciones');
  await until("!document.querySelector('button[aria-label=\"Reintentar conversaciones\"]')");
  assert.equal(await evaluate("Boolean(document.querySelector('button[aria-label=\"Reintentar mensajes\"]'))"), true);
});

test('NF04: sin caché muestra errores localizados y recupera la bandeja', async () => {
  await send('Page.navigate', { url: `${preview.url}?emptyFailure=1` });
  await until("document.querySelectorAll('button[aria-label^=Reintentar]').length === 2");
  assert.equal(await evaluate("document.body.textContent.includes('Conversaciones no disponibles')"), true);
  await evaluate("window.rc3Inbox.configure({fail:'none'})"); await click('Reintentar conversaciones'); await click('Reintentar mensajes');
  await until("document.body.textContent.includes('Historial ficticio conservado')");
});

for (const status of [0, 500, 503]) test(`NF04: error transitorio ${status} mantiene historial sin habilitar operaciones`, async () => {
  await healthyPage(); await evaluate(`(async () => { window.rc3Inbox.configure({status:${status}}); await window.rc3Inbox.fail('messages'); })()`);
  await until("document.body.textContent.includes('Los datos pueden estar desactualizados')");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), true);
  assert.equal(await evaluate("Array.from(document.querySelectorAll('button')).find(b => b.textContent.includes('Asumir Control')).disabled"), true);
});

for (const status of [401, 403]) test(`NF04: ${status} oculta caché y exige revalidar ambas consultas antes de mostrarla`, async () => {
  await healthyPage(); await evaluate(`(async () => { window.rc3Inbox.configure({status:${status}}); await window.rc3Inbox.fail('conversations'); })()`);
  await until("document.body.textContent.includes('No se mostrará información almacenada')");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado') || document.body.textContent.includes('Bandeja de Entrada')"), false);
  await evaluate("window.rc3Inbox.configure({fail:'none',hold:'messages'}); Array.from(document.querySelectorAll('button')).find(b => b.textContent === 'Reintentar').click()");
  await until("window.rc3Inbox.snapshot().listStatus === 'success'");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), false);
  await evaluate('window.rc3Inbox.release()');
  await until("document.body.textContent.includes('Historial ficticio conservado')");
});

test('NF04: cambiar de chat durante error de lista conserva selección y navegación', async () => {
  await healthyPage(); await evaluate("(async () => { window.rc3Inbox.configure({extraChat:true}); await window.rc3Inbox.refetch('conversations'); await window.rc3Inbox.fail('conversations'); })()");
  await evaluate("Array.from(document.querySelectorAll('button')).find(b => b.textContent.includes('51888888888')).click()");
  await until("document.body.textContent.includes('Historial segundo chat')");
  assert.equal(await evaluate("document.body.textContent.includes('Los datos pueden estar desactualizados')"), true);
});

test('NF04: workspace nuevo y respuesta tardía no reutilizan historial anterior', async () => {
  await healthyPage(); await evaluate("window.rc3Inbox.configure({hold:'messages'}); void window.rc3Inbox.refetch('messages')");
  await until("typeof window.rc3Inbox.configure === 'function'");
  await evaluate("window.rc3Inbox.configure({hold:null}); window.rc3Inbox.identity('workspace-b')");
  await until("document.body.textContent.includes('Historial exclusivo workspace B')");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), false);
  await evaluate('window.rc3Inbox.release()');
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), false);
});

test('NF04: revocar READ en metadatos oculta datos del mismo workspace', async () => {
  await healthyPage(); await evaluate("window.rc3Inbox.identity('workspace-a','local-user',false)");
  await until("document.body.textContent.includes('Acceso no disponible')");
  assert.equal(await evaluate("document.body.textContent.includes('Historial ficticio conservado')"), false);
});

test('NF04: conserva los cinco estados outbound sin afirmar entrega ni reenviar', async () => {
  await healthyPage(); await evaluate("(async () => { window.rc3Inbox.configure({showStatuses:true}); await window.rc3Inbox.refetch('messages'); })()");
  await until("document.body.textContent.includes('Estado UnknownDelivery')");
  for (const label of ['Pendiente', 'Enviando', 'Enviado', 'Error de envío', 'Entrega sin confirmar'])
    assert.equal(await evaluate(`document.body.textContent.includes('${label}')`), true);
  assert.equal(await evaluate("document.body.textContent.includes('Entregado') || document.body.textContent.includes('Leído')"), false);
});

test('NF04: móvil permite volver a la lista y mantiene el historial tras error parcial', async () => {
  await send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  try {
    await healthyPage(); await evaluate("window.rc3Inbox.fail('messages')");
    await click('Volver a conversaciones');
    await until("Array.from(document.querySelectorAll('h2')).some(el => el.textContent.includes('Bandeja') && el.getBoundingClientRect().width > 0)");
    await evaluate("Array.from(document.querySelectorAll('button')).find(b => b.textContent.includes('51999999999')).click()");
    await until("Array.from(document.querySelectorAll('p')).some(el => el.textContent === 'Historial ficticio conservado' && el.getBoundingClientRect().width > 0)");
  } finally { await send('Emulation.setDeviceMetricsOverride', { width: 1280, height: 1000, deviceScaleFactor: 1, mobile: false }); }
});

test('NF04: pestaña oculta pausa el polling de ambas consultas', async () => {
  await healthyPage(); await evaluate("Object.defineProperty(document,'visibilityState',{configurable:true,value:'hidden'}); document.dispatchEvent(new Event('visibilitychange')); window.rc3Inbox.calls.splice(0)");
  await wait(16_000);
  assert.deepEqual(await evaluate('window.rc3Inbox.calls'), []);
});
