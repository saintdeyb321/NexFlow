import test from 'node:test';
import assert from 'node:assert/strict';
import { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../src/core/query/queryKeys.ts';
import { categoryAllowed, categoryDraft, categoryPayload, categoryUse, filterCategories, invalidateCategoryViews, validateCategory } from '../src/features/catalog/utils/categoryManagement.ts';
import type { BusinessCategoryDto } from '../src/features/shared/types/business-offering.types.ts';

const shared: BusinessCategoryDto = { id: 'shared', name: 'Bienestar', description: null, scope: 'SHARED', isActive: false, displayOrder: 17 };
const product: BusinessCategoryDto = { id: 'product', name: 'Bebidas', description: 'Frías', scope: 'PRODUCT', isActive: true, displayOrder: -2 };
const service: BusinessCategoryDto = { id: 'service', name: 'Consultas', description: '', scope: 'SERVICE', isActive: true, displayOrder: 3 };

for (const context of ['PRODUCT', 'SERVICE'] as const) test(`creación desde ${context} conserva el contexto y defaults del backend`, () => {
  const draft = categoryDraft(); draft.name = '  Nueva categoría  ';
  assert.deepEqual(validateCategory(draft), {});
  assert.deepEqual(categoryPayload(draft, context), { name: 'Nueva categoría', description: null, scope: context, isActive: true, displayOrder: 0 });
  draft.shared = true; assert.equal(categoryPayload(draft, context).scope, 'SHARED');
  draft.shared = false; assert.equal(categoryPayload(draft, context).scope, context);
});
for (const capability of ['CREATE', 'UPDATE', 'DELETE'] as const) test(`categorías compartidas exigen ${capability} en ambos módulos`, () => {
  for (const [catalog, services] of [[false, false], [true, false], [false, true], [true, true]]) {
    const can = (module: string, action: string) => action === capability && (module === 'CATALOG' ? catalog : services);
    assert.equal(categoryAllowed(can, 'SHARED', capability), catalog && services);
    assert.equal(categoryAllowed(can, 'PRODUCT', capability), catalog);
    assert.equal(categoryAllowed(can, 'SERVICE', capability), services);
  }
});
test('editar conserva scope, activo, orden y descripción nullable, sin mutar el objeto remoto', () => {
  for (const existing of [shared, product, service]) {
    const snapshot = structuredClone(existing);
    const draft = categoryDraft(existing); draft.shared = !draft.shared; draft.name = '  Actualizada  ';
    const payload = categoryPayload(draft, existing.scope === 'PRODUCT' ? 'SERVICE' : 'PRODUCT', existing);
    assert.equal(payload.scope, existing.scope); assert.equal(payload.id, existing.id);
    assert.equal(payload.isActive, existing.isActive); assert.equal(payload.displayOrder, existing.displayOrder);
    assert.equal(payload.description, existing.description); assert.equal(payload.name, 'Actualizada');
    assert.deepEqual(existing, snapshot);
  }
});
test('cancelar o empezar otro borrador no cambia estado remoto ni conserva campos previos', () => {
  const snapshot = structuredClone(shared);
  const draft = categoryDraft(shared); draft.name = 'Sin guardar'; draft.displayOrder = '99'; draft.isActive = true;
  assert.deepEqual(shared, snapshot);
  assert.deepEqual(categoryDraft(), { name: '', description: '', isActive: true, displayOrder: '0', shared: false });
});
test('nombre vacío/blancos y orden inválido producen errores por campo', () => {
  for (const name of ['', ' ', '\t\r\n']) assert.ok(validateCategory({ ...categoryDraft(), name }).name);
  for (const order of ['', ' ', '1.5', '1e2', 'NaN', 'Infinity', '2147483648', '-2147483649'])
    assert.ok(validateCategory({ ...categoryDraft(), name: 'Válida', displayOrder: order }).displayOrder);
  for (const order of ['0', '-2', ' 17 ', '2147483647', '-2147483648'])
    assert.deepEqual(validateCategory({ ...categoryDraft(), name: 'Válida', displayOrder: order }), {});
});
test('descripción opcional, multilinea y activo se envían con el DTO existente', () => {
  const payload = categoryPayload({ ...categoryDraft(), name: 'Nombre', description: '  Línea uno\nLínea dos  ', isActive: false }, 'SERVICE');
  assert.equal(payload.description, 'Línea uno\nLínea dos'); assert.equal(payload.isActive, false);
  assert.deepEqual(Object.keys(payload).sort(), ['description', 'displayOrder', 'isActive', 'name', 'scope']);
});
test('listado conserva activas/inactivas y filtra sin reordenar ni modificar registros remotos', () => {
  const rows = [shared, product, service]; const snapshot = structuredClone(rows);
  assert.deepEqual(filterCategories(rows, '', 'all').map(category => category.id), ['product', 'service', 'shared']);
  assert.deepEqual(filterCategories(rows, '', 'inactive').map(category => category.id), ['shared']);
  assert.deepEqual(filterCategories(rows, 'FRIAS', 'active').map(category => category.id), ['product']);
  assert.deepEqual(filterCategories(rows, 'unknown', 'all'), []); assert.deepEqual(rows, snapshot);
  assert.equal(categoryUse.PRODUCT, 'Para productos'); assert.equal(categoryUse.SHARED, 'Para productos y servicios');
});
for (const scope of ['PRODUCT', 'SERVICE', 'SHARED'] as const) test(`invalidación ${scope} alcanza listas y artefactos propios relevantes, sin afectar otros tenants/módulos`, async () => {
  const client = new QueryClient();
  const ownCategories = ['PRODUCT', 'SERVICE', 'SHARED'].map(value => queryKeys.catalog.categories('workspace-a', value as BusinessCategoryDto['scope']));
  const artifacts = ['PRODUCT', 'SERVICE'].map(value => queryKeys.artifacts.byScope('workspace-a', value as 'PRODUCT' | 'SERVICE'));
  const foreign = queryKeys.catalog.categories('workspace-b', 'PRODUCT');
  const products = queryKeys.catalog.products('workspace-a', 'location-a');
  const reservations = queryKeys.reservations.list('workspace-a', 'location-a', '2026-10-08');
  for (const key of [...ownCategories, ...artifacts, foreign, products, reservations]) client.setQueryData(key, []);
  await invalidateCategoryViews(client, 'workspace-a', scope);
  for (const key of ownCategories) assert.equal(client.getQueryState(key)?.isInvalidated, scope === 'SHARED' || key[4] === scope);
  for (const key of artifacts) assert.equal(client.getQueryState(key)?.isInvalidated, scope === 'SHARED' || key[3] === scope);
  for (const key of [foreign, products, reservations]) assert.equal(client.getQueryState(key)?.isInvalidated, false);
  client.clear();
});
