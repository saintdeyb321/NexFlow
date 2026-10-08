import test from 'node:test';
import assert from 'node:assert/strict';
import { commercialIdentity, currentNavigationLabel, matchesNavigationRoute, MODULE_REGISTRY, navigationGroups } from '../src/layouts/workspaceNavigation.ts';

test('los tres grupos presentan únicamente rutas existentes en el orden operativo acordado', () => {
  const groups = navigationGroups(() => true, true);
  assert.deepEqual(groups.map(group => group.label), ['Operación', 'Gestión', 'Administración']);
  assert.deepEqual(groups.map(group => group.items.map(item => item.route)), [
    ['/', '/inbox', '/reservations', '/orders', '/requests'], ['/services', '/catalog', '/faqs'], ['/settings', '/superadmin'],
  ]);
  const routes = groups.flatMap(group => group.items.map(item => item.route));
  assert.equal(new Set(routes).size, routes.length);
  assert.equal(MODULE_REGISTRY.FAQ.label, 'Base de conocimiento');
});
test('Dashboard conserva CONVERSATIONS/READ; entitlements y capacidades son autoridad del predicado', () => {
  for (const allowed of ['CONVERSATIONS', 'CATALOG', 'SERVICES', 'RESERVATIONS', 'REQUESTS', 'ORDERS', 'FAQ']) {
    const calls: string[][] = [];
    const groups = navigationGroups((module, capability) => { calls.push([module, capability]); return module === allowed && capability === 'READ'; }, false);
    const routes = groups.flatMap(group => group.items.map(item => item.route));
    assert.equal(routes.includes('/'), allowed === 'CONVERSATIONS');
    assert.ok(routes.includes(MODULE_REGISTRY[allowed].route));
    assert.ok(calls.every(([, capability]) => capability === 'READ'));
    assert.equal(routes.includes('/superadmin'), false);
  }
});
test('sin enlaces permitidos no se muestran grupos vacíos; consola global conserva enlace especial', () => {
  assert.deepEqual(navigationGroups(() => false, false), []);
  const global = navigationGroups(() => false, true);
  assert.equal(global.length, 1); assert.equal(global[0].label, 'Administración');
  assert.deepEqual(global[0].items.map(item => item.route), ['/superadmin']);
});
test('Negocio preserva las condiciones de acceso existentes sin exigir permiso nuevo', () => {
  for (const allowed of ['BUSINESS_PROFILE', 'LOCATIONS', 'BUSINESS_HOURS', 'CONVERSATIONS'])
    assert.ok(navigationGroups(module => module === allowed, false).flatMap(group => group.items).some(item => item.route === '/settings'));
  assert.equal(navigationGroups(module => module === 'CATALOG', false).flatMap(group => group.items).some(item => item.route === '/settings'), false);
});
test('rutas exactas y subrutas no colisionan por prefijos; raíz y consola conservan su etiqueta', () => {
  for (const item of Object.values(MODULE_REGISTRY)) {
    assert.equal(matchesNavigationRoute(item.route, item.route), true);
    assert.equal(matchesNavigationRoute(`${item.route}/detail`, item.route), true);
    assert.equal(matchesNavigationRoute(`${item.route}-other`, item.route), false);
    assert.equal(currentNavigationLabel(`${item.route}/detail`), item.label);
  }
  assert.equal(currentNavigationLabel('/'), 'Dashboard');
  assert.equal(matchesNavigationRoute('/superadmin', '/'), false);
  assert.equal(currentNavigationLabel('/superadmin/users'), 'Consola SuperAdmin');
  assert.equal(currentNavigationLabel('/settings/profile'), 'Negocio');
  assert.equal(currentNavigationLabel('/superadministrator'), 'NexFlow');
  assert.equal(currentNavigationLabel('/catalogue'), 'NexFlow');
});
test('nombre comercial proviene exclusivamente del perfil exitoso, con fallback exacto y sin confundir loading/error', () => {
  for (const name of ['', '   ', null, undefined]) assert.deepEqual(commercialIdentity({global:false,authorized:true,status:'success',name}), {label:'Por definir',status:'ready'});
  assert.equal(commercialIdentity({global:false,authorized:true,status:'success',name:'  Negocio real  '}).label,'Negocio real');
  assert.deepEqual(commercialIdentity({global:false,authorized:true,status:'pending',name:'Perfil anterior'}),{label:'Cargando negocio…',status:'loading'});
  assert.deepEqual(commercialIdentity({global:false,authorized:true,status:'error',name:'Perfil anterior'}),{label:'Negocio no disponible',status:'error'});
});
test('sin autorización y consola global nunca exponen el nombre comercial aunque exista caché', () => {
  for(const status of ['pending','error','success'] as const) {
    assert.equal(commercialIdentity({global:false,authorized:false,status,name:'Dato ajeno'}).label,'Por definir');
    assert.equal(commercialIdentity({global:true,authorized:true,status,name:'Dato ajeno'}).label,'Administración Global');
  }
});
