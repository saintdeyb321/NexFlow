import axios from 'axios';
import type { BusinessCategoryDto } from '../../src/features/shared/types/business-offering.types';
import { categoryAllowed, validateCategory, categoryDraft } from '../../src/features/catalog/utils/categoryManagement';
import { can } from '../../src/core/auth/permissions';
import { useAuthStore } from './categories-store';

export class ApiError extends Error {
  status: number; code: string; correlationId?: string;
  constructor(status: number, code: string, message: string) { super(message); this.status = status; this.code = code; }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error de prueba';
export const calls: { method: string; path: string; workspace: string; params: Record<string, string>; data: unknown }[] = [];
export const fixture = { delay: 0, failLoad: false, failSave: false, empty: false };
const initial: BusinessCategoryDto[] = [
  { id: 'used-product', name: 'Bebidas de prueba', description: 'Categoría con referencias', scope: 'PRODUCT', isActive: true, displayOrder: 0 },
  { id: 'service', name: 'Consultas de prueba', description: 'Solo servicios', scope: 'SERVICE', isActive: true, displayOrder: 2 },
  { id: 'shared', name: 'Bienestar de prueba', description: null, scope: 'SHARED', isActive: false, displayOrder: 17 },
];
export const remote = new Map<string, BusinessCategoryDto[]>();
let nextId = 0;
export const axiosClient = axios.create({ adapter: async config => {
  const me = structuredClone(useAuthStore.getState().me);
  const workspace = me.workspace!.id;
  const path = config.url!.split('?')[0];
  const params = { ...config.params } as Record<string, string>;
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  calls.push({ method: config.method!, path, workspace, params, data: structuredClone(data) });
  if (fixture.delay) await new Promise(done => setTimeout(done, fixture.delay));
  if (config.signal?.aborted) throw new axios.CanceledError('Cancelado');
  const rows = remote.get(workspace) ?? structuredClone(initial); remote.set(workspace, rows);
  let response: unknown;
  if (path === '/catalog' || path === '/services') response = [];
  else if (path === '/catalog/categories' && config.method === 'get') {
    if (fixture.failLoad) throw new ApiError(503, 'TEST', 'No se pudieron cargar las categorías de prueba.');
    const module = params.scope === 'SERVICE' ? 'SERVICES' : 'CATALOG';
    if (!can(me, module, 'READ')) throw new ApiError(403, 'TEST', 'Acceso denegado.');
    response = fixture.empty ? [] : rows.filter(category => category.scope === params.scope || category.scope === 'SHARED');
  } else if (path.startsWith('/catalog/categories')) {
    const id = path.split('/')[3];
    const existing = rows.find(category => category.id === id);
    const scope = config.method === 'delete' ? existing!.scope : data.scope;
    const capability = config.method === 'post' ? 'CREATE' : config.method === 'delete' ? 'DELETE' : 'UPDATE';
    if (!categoryAllowed((module, action) => can(me, module, action), scope, capability)) throw new ApiError(403, 'TEST', 'Acceso denegado.');
    if (config.method === 'delete') {
      if (id === 'used-product') throw new ApiError(400, 'TEST', 'No puedes eliminar una categoría que contiene productos o servicios.');
      rows.splice(rows.indexOf(existing!), 1);
    } else {
      if (fixture.failSave) throw new ApiError(400, 'TEST', 'No se pudo guardar la categoría de prueba. Intenta nuevamente.');
      if (Object.keys(validateCategory(categoryDraft(data))).length) throw new ApiError(400, 'TEST', 'Categoría inválida.');
      if (existing && existing.scope !== data.scope) throw new ApiError(400, 'TEST', 'No se permite cambiar el scope de una categoría existente.');
      response = { ...data, id: existing?.id ?? `created-${++nextId}` };
      if (existing) rows.splice(rows.indexOf(existing), 1, response as BusinessCategoryDto); else rows.push(response as BusinessCategoryDto);
    }
  } else throw new ApiError(404, 'TEST', `Ruta de prueba no soportada: ${path}`);
  return { data: structuredClone(response), status: 200, statusText: 'OK', config, headers: {} };
} });
