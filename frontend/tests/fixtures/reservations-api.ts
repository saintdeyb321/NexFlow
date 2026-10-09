import axios from 'axios';
import type { ReservationDto } from '../../src/features/reservations/types/reservation.types';
import { useAuthStore } from './reservations-store.ts';
import { can } from '../../src/core/auth/permissions.ts';
import type { BusinessProfile } from '../../src/features/business/types/business.types';

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string, public correlationId?: string) { super(message); }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error de prueba';
export const calls: { path: string; method: string; params: Record<string, string>; data: unknown; workspace: string; user: string }[] = [];
export const fixture = { delay: 0, fail: false, empty: false, timeZone: 'America/Lima', readOnly: false, contextDelay: 0, contextStatus: 0, contextResponse: undefined as unknown, rows: null as ReservationDto[] | null };
export const profiles = new Map<string, BusinessProfile>();
export const zones = new Map<string, string>();
export const initialProfile = (workspace: string): BusinessProfile => ({ commercialName: `Negocio ${workspace}`, taxId: 'TEST', contactEmail: 'business@example.invalid', whatsAppNumber: '000000000', description: '', timeZone: fixture.timeZone });
export const rowsFor = (workspaceId: string, date: string): ReservationDto[] => [
  { id: 'b', workspaceId, locationId: 'location-a', serviceId: 'service-a', customerName: 'Ana Prueba', customerIdentifier: 'TEST', dateTime: `${date}T15:00:00Z`, status: 'Confirmed' },
  { id: 'a', workspaceId, locationId: 'location-b', serviceId: 'service-b', customerName: 'José Prueba', customerIdentifier: 'TEST', dateTime: `${date}T15:00:00Z`, status: 'Pending' },
  { id: 'c', workspaceId, locationId: 'location-a', serviceId: 'service-a', customerName: 'Carla Prueba', customerIdentifier: 'TEST', dateTime: `${date}T17:30:00Z`, status: 'Completed' },
  { id: 'd', workspaceId, locationId: 'location-b', serviceId: 'service-b', customerName: 'Diego Prueba', customerIdentifier: 'TEST', dateTime: `${date}T19:00:00Z`, status: 'Cancelled' },
];
const services = [
  { id: 'service-a', name: 'Consulta de prueba', type: 'SERVICE', isActive: true, requiresReservation: true, durationInMinutes: 30, locationScope: 'ALL', locationIds: [] },
  { id: 'service-b', name: 'Sesión de prueba', type: 'SERVICE', isActive: true, requiresReservation: true, durationInMinutes: 60, locationScope: 'SPECIFIC', locationIds: ['location-b'] },
];
const changed = new Map<string, ReservationDto[]>();
export const axiosClient = axios.create({ adapter: async config => {
  const me = structuredClone(useAuthStore.getState().me);
  const workspace = me?.workspace?.id ?? '';
  const state = { ...fixture };
  const path = config.url!.split('?')[0];
  const params = { ...Object.fromEntries(new URLSearchParams(config.url!.split('?')[1] ?? '')), ...config.params } as Record<string, string>;
  const body = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  calls.push({ path, method: config.method!, params, data: body, workspace, user: me?.user.id ?? '' });
  const zone = profiles.get(workspace)?.timeZone ?? zones.get(workspace) ?? state.timeZone;
  const delay = path === '/reservations/context' ? state.contextDelay : state.delay;
  if (delay) await new Promise(resolve => setTimeout(resolve, delay));
  if (state.fail && path === '/reservations') throw new ApiError(503, 'TEST', 'Dependencia de prueba temporalmente indisponible.');
  let data: unknown;
  if (path === '/reservations/context') {
    if (!can(me, 'RESERVATIONS', 'READ')) throw new ApiError(403, 'Security.CapabilityDenied', 'No tienes permiso para consultar reservas.');
    if (state.contextStatus) throw new ApiError(state.contextStatus, 'TEST', `Error de contexto ${state.contextStatus}.`);
    data = state.contextResponse === undefined ? { timeZone: zone } : state.contextResponse;
  } else if (path === '/business/profile') {
    if (!can(me, 'BUSINESS_PROFILE', config.method === 'put' ? 'UPDATE' : 'READ')) throw new ApiError(403, 'TEST', 'Perfil denegado.');
    if (config.method === 'put') profiles.set(workspace, structuredClone(body));
    else data = structuredClone(profiles.get(workspace) ?? initialProfile(workspace));
  }
  else if (path === '/business/locations') data = [{ id: 'location-a', name: 'Sede A de prueba', isMain: true }, { id: 'location-b', name: 'Sede B de prueba', isMain: false }];
  else if (path === '/services') data = services;
  else if (path === '/reservations/availability') data = [{ startTime: `${params.date}T15:00:00Z`, endTime: `${params.date}T15:30:00Z`, isAvailable: true }, { startTime: `${params.date}T16:00:00Z`, endTime: `${params.date}T16:30:00Z`, isAvailable: false }];
  else if (path === '/reservations' && config.method === 'get') {
    if (!can(me, 'RESERVATIONS', 'READ')) throw new ApiError(403, 'Security.CapabilityDenied', 'Reservas denegadas.');
    if (params.timeZone && params.timeZone !== zone) throw new ApiError(409, 'Reservation.TimeZoneChanged', 'La zona horaria de la agenda cambió.');
    const key = `${workspace}:${params.from ?? params.date}`;
    const rows = state.rows ?? changed.get(key) ?? rowsFor(workspace, params.from ?? params.date);
    changed.set(key, rows);
    data = state.empty ? [] : rows.filter(row => params.locationId === 'all' || row.locationId === params.locationId);
  } else if (path === '/reservations' && config.method === 'post') {
    data = { ...body, id: 'created', workspaceId: workspace, status: 'Confirmed' };
    for (const [key, rows] of changed) if (key.startsWith(`${workspace}:`)) rows.push(data as ReservationDto);
  } else {
    const id = path.split('/')[2];
    let updated: ReservationDto | undefined;
    for (const [key, rows] of changed) if (key.startsWith(`${workspace}:`)) for (const row of rows) if (row.id === id) {
      if (config.method === 'delete') row.status = 'Cancelled';
      else if (path.endsWith('/status')) row.status = 'Completed';
      else row.dateTime = body.newDateTime.endsWith('Z') ? body.newDateTime : new Date(`${body.newDateTime}-05:00`).toISOString();
      updated = row;
    }
    data = updated;
  }
  return { data: structuredClone(data), status: 200, statusText: 'OK', config, headers: {} };
} });
