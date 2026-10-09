import axios from 'axios';
import { can } from '../../src/core/auth/permissions';
import { useAuthStore } from './settings-store';
import type { BusinessProfile, LocationDto, WhatsAppStatusResponse } from '../../src/features/business/types/business.types';

export class ApiError extends Error {
  status: number; correlationId?: string;
  constructor(status: number, message: string) { super(message); this.status = status; }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error de prueba';
export const profile: BusinessProfile = { commercialName: 'Negocio de prueba', taxId: 'TEST', contactEmail: 'business@example.invalid', whatsAppNumber: '000000000', description: 'Datos locales ficticios', timeZone: 'America/Lima' };
export const initialLocations: LocationDto[] = [{ id: 'north', name: 'Sede Norte', address: 'Dirección ficticia', reference: 'Referencia ficticia', mapUrl: 'https://example.invalid/map', isMain: true }, { id: 'south', name: 'Sede Sur', address: 'Otra dirección ficticia', isMain: false }];
export const disconnected: WhatsAppStatusResponse = { status: 'DISCONNECTED', isLinked: false, canConnect: true, requiresLogout: false, qrBase64: null, qrExpiresAt: null, message: null };
export const fixture = {
  delay: 0, failProfile: 0, failLocations: 0, failHours: 0, failStatus: 0, failDisconnect: 0, disconnectDelay: 0,
  whatsapp: { ...disconnected }, disconnectResponse: { ...disconnected },
  locations: structuredClone(initialLocations), profile: { ...profile },
};
export const calls: { method: string; path: string; workspace: string; data: unknown; params: unknown }[] = [];
let sequence = 0;
export const axiosClient = axios.create({ adapter: async config => {
  const me = structuredClone(useAuthStore.getState().me); const path = config.url!; const method = config.method!;
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  calls.push({ method, path, workspace: me?.workspace?.id ?? '', data: structuredClone(data), params: structuredClone(config.params) });
  const state = structuredClone(fixture);
  if (state.delay) await new Promise(done => setTimeout(done, state.delay));
  if (method === 'post' && path.endsWith('/disconnect') && state.disconnectDelay) await new Promise(done => setTimeout(done, state.disconnectDelay));
  if (config.signal?.aborted) throw new axios.CanceledError('Cancelado');
  let response: unknown;
  const permission = (module: string, capability: string) => { if (!can(me, module, capability)) throw new ApiError(403, 'Acceso denegado.'); };
  if (path === '/business/profile') {
    permission('BUSINESS_PROFILE', method === 'get' ? 'READ' : 'UPDATE');
    if (state.failProfile) throw new ApiError(state.failProfile, 'No se pudo cargar el perfil de prueba.');
    if (method === 'put') fixture.profile = structuredClone(data); else response = state.profile;
  } else if (path.startsWith('/business/locations') && !path.endsWith('/hours')) {
    permission('LOCATIONS', method === 'get' ? 'READ' : method === 'post' ? 'CREATE' : method === 'delete' ? 'DELETE' : 'UPDATE');
    if (state.failLocations) throw new ApiError(state.failLocations, 'No se pudieron cargar las sedes de prueba.');
    if (method === 'get') response = state.locations;
    else if (method === 'post') fixture.locations.push({ ...data, id: `created-${++sequence}` });
    else if (method === 'put') fixture.locations = fixture.locations.map(row => row.id === path.split('/')[3] ? structuredClone(data) : row);
    else fixture.locations = fixture.locations.filter(row => row.id !== path.split('/')[3]);
  } else if (path.endsWith('/hours')) {
    permission('BUSINESS_HOURS', method === 'get' ? 'READ' : 'UPDATE');
    if (state.failHours) throw new ApiError(state.failHours, 'No se pudieron cargar los horarios de prueba.');
    response = [];
  } else if (path === '/business/whatsapp/status') {
    permission('CONVERSATIONS', 'READ');
    if (state.failStatus) throw new ApiError(state.failStatus, 'No se pudo consultar WhatsApp.');
    response = state.whatsapp;
  } else if (path === '/business/whatsapp/connect') {
    permission('CONVERSATIONS', 'CONFIGURE');
    response = fixture.whatsapp;
  } else if (path === '/business/whatsapp/disconnect') {
    permission('CONVERSATIONS', 'CONFIGURE');
    if (state.failDisconnect) throw new ApiError(state.failDisconnect, 'La dependencia de WhatsApp no pudo confirmar el cierre (503).');
    fixture.whatsapp = state.disconnectResponse; response = state.disconnectResponse;
  } else if (path.startsWith('/notifications')) response = [];
  else throw new ApiError(404, `Ruta no prevista en pruebas: ${path}`);
  return { data: structuredClone(response), status: method === 'put' || method === 'delete' ? 204 : 200, statusText: 'OK', config, headers: {} };
} });
