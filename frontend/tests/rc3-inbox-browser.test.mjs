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
