import axios from 'axios';
import { can } from '../../src/core/auth/permissions';
import type { BusinessProfile, LocationDto } from '../../src/features/business/types/business.types';
import type { WorkspaceSummaryDto } from '../../src/features/admin/types/admin.types';
import { useAuthStore } from './navigation-store';

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error de prueba';
export const fixture = { loadDelay: 0, saveDelay: 0, failLoad: 0, failSave: 0, ignoreAbort: false };
export const calls: { method: string; path: string; workspace: string; user: string; data: unknown }[] = [];
export const remote = new Map<string, BusinessProfile>();
export const locations = new Map<string, LocationDto[]>();
export const adminFixture = { delay: 0, failure: 0, uploadFailure: 0 };
export const adminWorkspaces: WorkspaceSummaryDto[] = [{ id: 'business-a', name: 'Negocio de prueba', ownerEmail: 'owner@example.invalid', status: 1, createdAt: '2026-10-20T12:00:00Z' }];
export const initialProfile = (workspace: string): BusinessProfile => ({
  commercialName: workspace === 'workspace-b' ? 'Comercial Beta' : 'Comercial Alfa', taxId: 'TEST', contactEmail: 'business@example.invalid',
  whatsAppNumber: '000000000', description: 'Perfil ficticio para pruebas', timeZone: 'America/Lima',
});
export const axiosClient = axios.create({ adapter: async config => {
  const me = structuredClone(useAuthStore.getState().me); const workspace = me?.workspace?.id ?? ''; const user = me?.user.id ?? '';
  const path = config.url!; const method = config.method!;
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  calls.push({ method, path, workspace, user, data: path === '/storage/upload' ? null : structuredClone(data) });
  const state = { ...fixture }; const profile = structuredClone(remote.get(workspace) ?? initialProfile(workspace));
  if (path === '/business/profile') {
    if (method === 'get' && state.loadDelay) await new Promise(done => setTimeout(done, state.loadDelay));
    if (method === 'put' && state.saveDelay) await new Promise(done => setTimeout(done, state.saveDelay));
  }
  if (config.signal?.aborted && !state.ignoreAbort) throw new axios.CanceledError('Cancelado');
  let response: unknown;
  if (path.startsWith('/superadmin/') || path === '/storage/upload') {
    const state = { ...adminFixture };
    if (method === 'post' && state.delay) await new Promise(done => setTimeout(done, state.delay));
    if (path === '/storage/upload') {
      if (state.uploadFailure) throw new ApiError(state.uploadFailure, 'Imagen inválida, incompleta o excesiva. Usa JPG, PNG o WebP estático de hasta 5 MB y 16 megapíxeles.');
      response = { secureUrl: 'data:image/png;base64,iVBORw0KGgo=' };
    } else {
      if (!me?.user.isSuperAdmin) throw new ApiError(403, 'No tienes acceso.');
      if (state.failure) throw new ApiError(state.failure, 'No se pudo cambiar el estado.');
      if (method === 'get') response = path === '/superadmin/clients' ? adminWorkspaces
        : path.endsWith('/templates') ? [{ code: 'BASIC', name: 'Básico' }]
          : [{ id: 'module-catalog', code: 'CATALOG', name: 'Catálogo' }];
      else if (path.endsWith('/suspend') || path.endsWith('/reactivate')) {
        if (path.endsWith('/suspend') && !data.reason) throw new ApiError(400, 'Reason es obligatorio en el contrato actual.');
        const item = adminWorkspaces.find(item => item.id === data.workspaceId);
        if (item) item.status = path.endsWith('/suspend') ? 2 : 1;
      } else if (path.endsWith('/delete')) throw new ApiError(503, 'Eliminación temporalmente deshabilitada.');
    }
  } else if (path === '/business/profile') {
    if (!can(me, 'BUSINESS_PROFILE', method === 'put' ? 'UPDATE' : 'READ')) throw new ApiError(403, 'No tienes acceso al perfil.');
    if (method === 'get') {
      if (state.failLoad) throw new ApiError(state.failLoad, 'No se pudo cargar el perfil.');
      response = profile;
    } else if (method === 'put') {
      if (state.failSave) throw new ApiError(state.failSave, 'La API rechazó los cambios del perfil.');
      remote.set(workspace, structuredClone(data));
    } else throw new ApiError(405, 'Método inválido.');
  } else if (path === '/business/locations') {
    if (!can(me, 'LOCATIONS', 'READ')) throw new ApiError(403, 'No tienes acceso a sedes.');
    response = locations.get(workspace) ?? [{ id: 'north', name: 'Sede Norte', address: 'Dirección ficticia', isMain: true }, { id: 'south', name: 'Sede Sur', address: 'Dirección ficticia', isMain: false }];
  } else if (path.startsWith('/notifications')) response = [];
  else throw new ApiError(404, `Ruta de prueba inválida: ${path}`);
  return { data: structuredClone(response), status: method === 'put' ? 204 : 200, statusText: 'OK', config, headers: {} };
} });
