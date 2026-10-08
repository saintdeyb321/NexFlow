import axios from 'axios';
import type { BusinessHoursDto } from '../../src/features/business/types/business.types';
import { can } from '../../src/core/auth/permissions';
import { useAuthStore } from './hours-store';

export class ApiError extends Error {
  status: number;
  constructor(status: number, message: string) { super(message); this.status = status; }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof ApiError && error.status >= 500
  ? 'Ocurrió un problema inesperado en el servidor.' : error instanceof Error ? error.message : 'Error de prueba';
export const fixture = { loadDelay: 0, saveDelay: 0, failLoad: 0, failSave: 0, ignoreAbort: false };
export const calls: { method: string; path: string; workspace: string; user: string; data: unknown }[] = [];
export const remote = new Map<string, BusinessHoursDto[]>();
const key = (workspace: string, location: string) => JSON.stringify([workspace, location]);
export const closed = Array.from({ length: 7 }, (_, dayOfWeek) => ({ dayOfWeek, openTime: '', closeTime: '', isClosed: true }));
const initial = (workspace: string, location: string): BusinessHoursDto[] => {
  if (location === 'empty') return [];
  if (location === 'closed') return closed;
  if (location === 'partial') return [{ dayOfWeek: 1, openTime: '09:15', closeTime: '18:45', isClosed: false }, closed[0]];
  return closed.map(row => row.dayOfWeek === 1 ? { ...row, openTime: workspace === 'workspace-b' ? '13:30' : location === 'south' ? '10:00' : '09:00', closeTime: '19:00', isClosed: false } : row);
};
export const axiosClient = axios.create({ adapter: async config => {
  const me = structuredClone(useAuthStore.getState().me);
  const workspace = me?.workspace?.id ?? ''; const user = me?.user.id ?? '';
  const path = config.url!; const location = path.split('/')[3]; const method = config.method!;
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  calls.push({ method, path, workspace, user, data: structuredClone(data) });
  const state = { ...fixture }; const rows = structuredClone(remote.get(key(workspace, location)) ?? initial(workspace, location));
  if (state.loadDelay && method === 'get') await new Promise(done => setTimeout(done, state.loadDelay));
  if (state.saveDelay && method === 'put') await new Promise(done => setTimeout(done, state.saveDelay));
  if (config.signal?.aborted && !state.ignoreAbort) throw new axios.CanceledError('Cancelado');
  if (!location || location === 'all' || !/\/business\/locations\/[^/]+\/hours$/.test(path)) throw new ApiError(400, 'Sede inválida.');
  if (!can(me, 'BUSINESS_HOURS', method === 'get' ? 'READ' : 'UPDATE')) throw new ApiError(403, 'No tienes acceso a esta función.');
  let response: BusinessHoursDto[] | undefined;
  if (method === 'get') {
    if (state.failLoad) throw new ApiError(state.failLoad, 'No se pudieron leer los horarios de prueba.');
    response = rows;
  } else if (method === 'put') {
    if (state.failSave) throw new ApiError(state.failSave, 'No se pudo guardar: horario rechazado por la API.');
    if (!Array.isArray(data) || data.length !== 7 || new Set(data.map(row => row.dayOfWeek)).size !== 7)
      throw new ApiError(400, 'Semana inválida.');
    const normalized = (data as BusinessHoursDto[]).map(row => row.isClosed ? { ...row, openTime: '', closeTime: '' } : row);
    if (normalized.some(row => !row.isClosed && (!/^([01]\d|2[0-3]):[0-5]\d$/.test(row.openTime) || !/^([01]\d|2[0-3]):[0-5]\d$/.test(row.closeTime) || row.openTime >= row.closeTime)))
      throw new ApiError(400, 'Horario inválido.');
    remote.set(key(workspace, location), structuredClone(normalized));
  } else throw new ApiError(405, 'Método no admitido.');
  return { data: structuredClone(response), status: method === 'put' ? 204 : 200, statusText: 'OK', config, headers: {} };
} });
