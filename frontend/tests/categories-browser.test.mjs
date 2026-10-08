import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync, readFileSync, mkdirSync, mkdtempSync, writeFileSync } from 'node:fs';
import { rm } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { startPreview } from './categories-preview.server.mjs';

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
  writeFileSync(join('node_modules/.tmp', `ux02-${name}.png`), Buffer.from(image.data, 'base64'));
};
const metrics = width => send('Emulation.setDeviceMetricsOverride', { width, height: 1000, deviceScaleFactor: 1, mobile: false });
const mutations = () => evaluate("window.categoryAudit.calls.filter(call=>call.path.startsWith('/catalog/categories')&&call.method!=='get')");
const modalTitle = () => evaluate("(()=>{const d=[...document.querySelectorAll('[role=dialog]')].at(-1);return d?document.getElementById(d.getAttribute('aria-labelledby')).textContent:''})()");
const rowAction = (action, name) => evaluate(`(()=>{const e=document.querySelector('button[aria-label='+CSS.escape(${JSON.stringify(action + ' categoría ' + name)})+']');if(!e||e.disabled)throw Error('Row action unavailable');e.focus();e.click()})()`);
const key = async (value, code, vk, shiftKey = false) => {
  const modifiers = shiftKey ? 8 : 0;
  await send('Input.dispatchKeyEvent', { type: 'keyDown', key: value, code, modifiers, windowsVirtualKeyCode: vk, text: value === 'Enter' ? '\r' : undefined });
  await send('Input.dispatchKeyEvent', { type: 'keyUp', key: value, code, modifiers, windowsVirtualKeyCode: vk });
};
const closeManager = async () => {
  await evaluate("(()=>{const d=[...document.querySelectorAll('[role=dialog]')].at(-1);if(d){const e=d.querySelector('button[aria-label=\\\"Cerrar diálogo\\\"]');if(e.disabled)throw Error('Dialog busy');e.click()}})()");
  await until("!document.querySelector('[role=dialog]')", 'Category manager did not close.');
};
const openManager = async (page = 'productos') => {
  await click(page === 'productos' ? 'Ver productos' : 'Ver servicios');
  await until("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='Categorías'&&!e.disabled)", 'Categories entry did not load.');
  await evaluate("[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Categorías').focus()");
  await click('Categorías');
  await until("document.querySelector('[role=dialog]')&&!document.querySelector('[role=dialog]').textContent.includes('Cargando categorías')", 'Category list did not load.');
};

before(async () => {
  preview = await startPreview();
  const root = resolve('node_modules/.tmp'); mkdirSync(root, { recursive: true });
  profile = mkdtempSync(join(root, 'ux02-browser-'));
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
  await until("window.categoryAudit&&[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='Categorías')", 'Product category entry did not load.');
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
  if (profile && resolve(profile).replaceAll('\\', '/').startsWith(root.replaceAll('\\', '/') + 'ux02-browser-'))
    await rm(profile, { recursive: true, force: true, maxRetries: 10, retryDelay: 500 });
});

test('Productos: entrada real, listado activo/inactivo, texto claro y caché reutilizada', async () => {
  const before = await evaluate("window.categoryAudit.calls.filter(c=>c.path==='/catalog/categories'&&c.method==='get').length");
  await openManager();
  assert.equal(await modalTitle(), 'Categorías de productos');
  assert.equal(await evaluate("document.querySelector('[aria-label=\\\"Categorías registradas\\\"]').children.length"), 2);
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Inactiva')"));
  assert.equal(await evaluate("window.categoryAudit.calls.filter(c=>c.path==='/catalog/categories'&&c.method==='get').length"), before);
  assert.equal(await evaluate("/\\b(scope|PRODUCT|SERVICE|SHARED)\\b/.test(document.querySelector('[role=dialog]').textContent)"), false);
  await change('Estado', 'inactive');
  assert.equal(await evaluate("document.querySelector('[aria-label=\\\"Categorías registradas\\\"]').children.length"), 1);
  await change('Estado', 'all'); await change('Buscar categorías', 'BEbiDAS');
  assert.equal(await evaluate("document.querySelector('[aria-label=\\\"Categorías registradas\\\"]').children.length"), 1);
  await change('Buscar categorías', ''); await capture('product-list');
});

test('validación asociada, cancelación y creación predeterminada desde productos', async () => {
  await click('Nueva categoría');
  assert.equal(await modalTitle(), 'Nueva categoría para productos');
  assert.equal(await evaluate("document.activeElement===document.querySelector('[role=dialog] input')"), true);
  const before = (await mutations()).length;
  await change('Nombre', '   '); await click('Crear categoría');
  assert.equal((await mutations()).length, before);
  assert.ok(await evaluate("document.querySelector('input[aria-invalid=true]')?.getAttribute('aria-describedby')"));
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Escribe un nombre')"));
  await click('Cancelar'); await click('Nueva categoría');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input').value"), '');
  await change('Nombre', '  Producto de prueba  '); await change('Descripción', '  Descripción de prueba  ');
  await click('Crear categoría');
  await until("document.querySelector('[role=dialog]').textContent.includes('Producto de prueba')&&!document.querySelector('[role=dialog] form')", 'Successful create did not return to list.');
  const post = (await mutations()).at(-1);
  assert.equal(post.method, 'post'); assert.equal(post.data.scope, 'PRODUCT');
  assert.equal(post.data.name, 'Producto de prueba'); assert.equal(post.data.description, 'Descripción de prueba');
  assert.equal(post.data.displayOrder, 0); assert.equal(post.data.isActive, true);
  await click('Nueva categoría');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input').value"), '');
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=number]').value"), '0');
  await click('Cancelar'); await closeManager();
});

test('Servicios: creación usa SERVICE, descripción opcional y estado inactivo', async () => {
  await openManager('servicios'); await click('Nueva categoría');
  assert.equal(await modalTitle(), 'Nueva categoría para servicios');
  await change('Nombre', 'Servicio de prueba');
  await evaluate("[...document.querySelectorAll('[role=dialog] label')].find(e=>e.textContent.startsWith('Activa')).querySelector('input').click()");
  await click('Crear categoría');
  await until("!document.querySelector('[role=dialog] form')", 'Service category did not save.');
  const post = (await mutations()).at(-1);
  assert.equal(post.data.scope, 'SERVICE'); assert.equal(post.data.description, null); assert.equal(post.data.isActive, false);
  await closeManager();
});

test('compartir/desmarcar respeta contexto; SHARED actualiza ambos contextos y sus artefactos', async () => {
  await openManager('productos'); await click('Nueva categoría');
  await change('Nombre', 'Compartida de prueba');
  const toggle = "[...document.querySelectorAll('[role=dialog] label')].find(e=>e.textContent.includes('Usar también en productos y servicios')).querySelector('input').click()";
  await evaluate(toggle);
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Para productos y servicios')"));
  await evaluate(toggle);
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Para productos')"));
  await evaluate(toggle);
  await click('Crear categoría'); await until("!document.querySelector('[role=dialog] form')", 'Shared category did not save.');
  assert.equal((await mutations()).at(-1).data.scope, 'SHARED');
  assert.ok(await evaluate("window.categoryAudit.client.getQueryState(['workspace','workspace-a','catalog','categories','SERVICE']).isInvalidated"));
  await closeManager(); await openManager('servicios');
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Compartida de prueba')"));
  await closeManager();
});

test('CREATE parcial oculta opción compartida; UPDATE combinado conserva SHARED y orden al editar', async () => {
  await evaluate("(()=>{const a=window.categoryAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.SERVICES=['READ','UPDATE','DELETE'];a.setIdentity(me)})()");
  await openManager();
  await click('Nueva categoría');
  assert.equal(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Usar también')"), false);
  await click('Cancelar');
  await rowAction('Editar', 'Bienestar de prueba');
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Para productos y servicios')"));
  assert.equal(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Usar también')"), false);
  assert.equal(await evaluate("document.querySelector('[role=dialog] input[type=number]').value"), '17');
  await change('Nombre', 'Bienestar editado');
  await click('Guardar cambios'); await until("!document.querySelector('[role=dialog] form')", 'Shared edit did not save.');
  const put = (await mutations()).at(-1);
  assert.equal(put.method, 'put'); assert.equal(put.data.scope, 'SHARED'); assert.equal(put.data.displayOrder, 17);
  assert.equal(put.data.isActive, false); assert.equal(put.data.description, null);
  await closeManager();
  await evaluate("window.categoryAudit.setIdentity(structuredClone(window.categoryAudit.defaultIdentity))");
});

test('orden inválido visible; cancelar edición no persiste ni cambia remoto', async () => {
  await openManager(); await rowAction('Editar', 'Bienestar editado');
  const before = (await mutations()).length;
  await evaluate("document.querySelector('[role=dialog] details').open=true");
  await change('Orden de visualización', '1.5');
  await click('Guardar cambios');
  assert.equal((await mutations()).length, before);
  assert.ok(await evaluate("document.querySelector('input[type=number]').getAttribute('aria-invalid')==='true'"));
  await change('Orden de visualización', '18'); await change('Descripción', 'Cambio no guardado');
  await click('Cancelar');
  assert.equal((await mutations()).length, before);
  assert.ok(await evaluate("window.categoryAudit.remote.get('workspace-a').find(c=>c.id==='shared').description===null"));
});

test('error de guardado conserva borrador; doble clic/envío queda bloqueado hasta respuesta', async () => {
  await click('Nueva categoría'); await change('Nombre', 'Reintento de prueba');
  await change('Descripción', 'Borrador conservado');
  await evaluate('window.categoryAudit.fixture.failSave=true;window.categoryAudit.fixture.delay=400');
  const before = (await mutations()).length;
  await evaluate("(()=>{const f=document.querySelector('[role=dialog] form');f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}));f.dispatchEvent(new Event('submit',{bubbles:true,cancelable:true}))})()");
  await until("document.querySelector('[role=dialog] button[aria-label=\\\"Cerrar diálogo\\\"]').disabled", 'Save did not lock close.');
  await key('Escape', 'Escape', 27);
  assert.equal(await evaluate("document.querySelectorAll('[role=dialog]').length"), 1);
  await until("document.querySelector('[role=dialog]').textContent.includes('No se pudo guardar')", 'Save error was hidden.');
  assert.equal((await mutations()).length, before + 1);
  assert.equal(await evaluate("document.querySelector('[role=dialog] input').value"), 'Reintento de prueba');
  assert.equal(await evaluate("document.querySelector('[role=dialog] textarea').value"), 'Borrador conservado');
  await evaluate('window.categoryAudit.fixture.failSave=false;window.categoryAudit.fixture.delay=0');
  await click('Crear categoría'); await until("!document.querySelector('[role=dialog] form')", 'Retry did not save.');
  assert.equal((await mutations()).length, before + 2);
});

test('eliminación confirmada: error real por referencias conserva categoría y permite cancelar', async () => {
  const before = (await mutations()).length;
  await rowAction('Eliminar', 'Bebidas de prueba');
  assert.equal(await evaluate("document.querySelectorAll('[role=dialog]').length"), 2);
  assert.ok(await evaluate("document.activeElement.textContent.includes('Conservar categoría')"));
  await key('Escape', 'Escape', 27);
  await until("document.querySelectorAll('[role=dialog]').length===1", 'Escape did not close only the nested confirmation.');
  assert.equal((await mutations()).length, before);
  assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"), 'Eliminar categoría Bebidas de prueba');
  await rowAction('Eliminar', 'Bebidas de prueba'); await click('Eliminar categoría');
  await until("[...document.querySelectorAll('[role=dialog]')].at(-1).textContent.includes('No puedes eliminar una categoría que contiene productos o servicios.')", 'Reference error was hidden.');
  assert.ok(await evaluate("window.categoryAudit.remote.get('workspace-a').some(c=>c.id==='used-product')"));
  await click('Conservar categoría'); await until("document.querySelectorAll('[role=dialog]').length===1", 'Cancel after error failed.');
});

test('eliminación válida, doble confirmación bloqueada y actualización de la lista', async () => {
  await rowAction('Eliminar', 'Producto de prueba');
  const before = (await mutations()).length;
  await evaluate('window.categoryAudit.fixture.delay=400');
  await evaluate("(()=>{const b=[...document.querySelectorAll('button')].find(e=>e.textContent.trim()==='Eliminar categoría');b.click();b.click()})()");
  await until("document.querySelectorAll('[role=dialog]').length===1&&!document.querySelector('[role=dialog]').textContent.includes('Producto de prueba')", 'Delete did not refresh list.');
  assert.equal((await mutations()).length, before + 1);
  await evaluate('window.categoryAudit.fixture.delay=0');
});

test('responsive 360/768/1280 y teclado: foco visible, Tab atrapado, scroll y botones alcanzables', async () => {
  await click('Nueva categoría');
  await evaluate("document.querySelector('[role=dialog] summary').focus()");
  await key('Enter', 'Enter', 13);
  assert.equal(await evaluate("document.querySelector('[role=dialog] details').open"), true);
  await key('Enter', 'Enter', 13);
  assert.equal(await evaluate("document.querySelector('[role=dialog] details').open"), false);
  for (const width of [360, 768, 1280]) {
    await metrics(width);
    await evaluate("document.querySelector('[role=dialog] input').focus()");
    await key('Tab', 'Tab', 9);
    assert.equal(await evaluate("document.activeElement.closest('[role=dialog]')!==null"), true);
    assert.equal(await evaluate("(()=>{const s=getComputedStyle(document.activeElement);return s.outlineStyle==='solid'||s.boxShadow!=='none'})()"), true);
    assert.equal(await evaluate('document.documentElement.scrollWidth<=window.innerWidth'), true);
    assert.ok(await evaluate("(()=>{const r=document.querySelector('[role=dialog]').getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight})()"));
    await capture('form-' + width);
    await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Crear categoría').scrollIntoView({block:'nearest'})");
    assert.ok(await evaluate("(()=>{const b=[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Crear categoría');const r=b.getBoundingClientRect();return r.left>=0&&r.right<=innerWidth&&r.top>=0&&r.bottom<=innerHeight})()"));
    assert.ok(await evaluate("(()=>{const b=[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Crear categoría');const r=b.getBoundingClientRect();return b.contains(document.elementFromPoint(r.left+r.width/2,r.top+r.height/2))})()"));
    await capture('footer-' + width);
  }
  // Wrap from last to first and back within the same dialog.
  await evaluate("[...document.querySelectorAll('[role=dialog] button')].find(e=>e.textContent.trim()==='Crear categoría').focus()");
  await key('Tab', 'Tab', 9);
  assert.equal(await evaluate("document.activeElement.getAttribute('aria-label')"), 'Cerrar diálogo');
  await key('Tab', 'Tab', 9, true);
  assert.equal(await evaluate("document.activeElement.textContent.trim()"), 'Crear categoría');
  await click('Cancelar'); await closeManager();
  assert.equal(await evaluate("document.activeElement.textContent.trim()"), 'Categorías');
});

test('solo lectura o permiso combinado ausente nunca permite acciones compartidas', async () => {
  await evaluate("(()=>{const a=window.categoryAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.SERVICES=['READ'];a.setIdentity(me)})()");
  await openManager();
  assert.equal(await evaluate("document.querySelector('button[aria-label=\\\"Editar categoría Bienestar editado\\\"]')===null"), true);
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('requiere el permiso correspondiente')"));
  await closeManager();
  await evaluate("(()=>{const a=window.categoryAudit;const me=structuredClone(a.defaultIdentity);me.capabilities.CATALOG=['READ'];me.capabilities.SERVICES=['READ'];a.setIdentity(me)})()");
  await openManager();
  assert.equal(await evaluate("[...document.querySelectorAll('[role=dialog] button')].some(e=>e.textContent.trim()==='Nueva categoría')"), false);
  assert.equal(await evaluate("document.querySelectorAll('button[aria-label^=\\\"Editar categoría\\\"]').length"), 0);
  assert.ok(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Solo lectura')"));
  await closeManager();
  await evaluate("window.categoryAudit.setIdentity(structuredClone(window.categoryAudit.defaultIdentity))");
});

test('cambio de workspace descarta borrador y no contamina caché ni callbacks pendientes', async () => {
  await openManager(); await click('Nueva categoría'); await change('Nombre', 'Solo workspace A');
  await evaluate('window.categoryAudit.fixture.delay=500'); await click('Crear categoría');
  await evaluate("(()=>{const a=window.categoryAudit;const me=structuredClone(a.defaultIdentity);me.workspace.id='workspace-b';me.workspace.name='Workspace B';a.setIdentity(me)})()");
  await until("document.querySelector('[role=dialog]')&&!document.querySelector('[role=dialog] form')", 'Workspace switch did not discard old editor.');
  await until("window.categoryAudit.calls.some(c=>c.workspace==='workspace-b'&&c.path==='/catalog/categories')", 'New tenant did not query own categories.');
  await evaluate('window.categoryAudit.fixture.delay=0');
  assert.equal(await evaluate("document.querySelector('[role=dialog]').textContent.includes('Solo workspace A')"), false);
  assert.ok(await evaluate("window.categoryAudit.calls.filter(c=>c.path==='/catalog/categories'&&c.method==='post'&&c.data.name==='Solo workspace A').every(c=>c.workspace==='workspace-a')"));
  await click('Nueva categoría'); assert.equal(await evaluate("document.querySelector('[role=dialog] input').value"), '');
  await click('Cancelar'); await closeManager();
});

test('carga, fallo con recuperación y vacío son estados distintos en el gestor', async () => {
  await click('Ver servicios');
  await evaluate("window.categoryAudit.setIdentity(structuredClone(window.categoryAudit.store.getState().me));window.categoryAudit.fixture.failLoad=true;window.categoryAudit.fixture.delay=300");
  await until("[...document.querySelectorAll('button')].some(e=>e.textContent.trim()==='Categorías'&&!e.disabled)", 'Services page did not reload.');
  await click('Categorías');
  await until("document.querySelector('[role=dialog]').textContent.includes('Cargando categorías')", 'Loading state missing.');
  await until("document.querySelector('[role=dialog]').textContent.includes('No se pudieron cargar las categorías')", 'Load failure missing.');
  await evaluate('window.categoryAudit.fixture.failLoad=false;window.categoryAudit.fixture.delay=0');
  await click('Reintentar'); await until("document.querySelector('[aria-label=\\\"Categorías registradas\\\"]')", 'Retry failed.');
  await evaluate("window.categoryAudit.fixture.empty=true;void window.categoryAudit.client.invalidateQueries({queryKey:['workspace','workspace-b','catalog','categories','SERVICE']})");
  await until("document.querySelector('[role=dialog]').textContent.includes('Aún no hay categorías')", 'Empty state missing.');
  await evaluate('window.categoryAudit.fixture.empty=false'); await closeManager();
});
