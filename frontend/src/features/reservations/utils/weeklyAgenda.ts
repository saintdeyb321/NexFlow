import { toBusinessLocalInput } from '../../../core/utils/dateTime.ts';
import type { ReservationDto } from '../types/reservation.types';
import type { ServiceDto } from '../../services/types/services.types';

export type StatusFilter = 'all' | ReservationDto['status'];
export const reservationStatusLabels: Record<ReservationDto['status'], string> = {
  Pending: 'Pendiente', Confirmed: 'Confirmada', Completed: 'Completada', Cancelled: 'Cancelada',
};

// UTC here is only a calendar arithmetic anchor, never the business instant of a local midnight.
const civilAnchor = (date: string): Date => {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) throw new RangeError('Fecha civil inválida');
  const [year, month, day] = date.split('-').map(Number);
  const anchor = new Date(0);
  anchor.setUTCFullYear(year, month - 1, day);
  if (year < 1 || anchor.getUTCFullYear() !== year || anchor.getUTCMonth() !== month - 1 || anchor.getUTCDate() !== day)
    throw new RangeError('Fecha civil inválida');
  return anchor;
};
const civilString = (anchor: Date): string => {
  const year = anchor.getUTCFullYear();
  if (year < 1 || year > 9999) throw new RangeError('Fecha fuera del rango admitido');
  return `${String(year).padStart(4, '0')}-${String(anchor.getUTCMonth() + 1).padStart(2, '0')}-${String(anchor.getUTCDate()).padStart(2, '0')}`;
};
export const addCivilDays = (date: string, days: number): string => {
  const anchor = civilAnchor(date);
  anchor.setUTCDate(anchor.getUTCDate() + days);
  return civilString(anchor);
};
export const isCivilDate = (date: string): boolean => {
  try { civilAnchor(date); return true; } catch { return false; }
};
export const weekOf = (date: string) => {
  const anchor = civilAnchor(date);
  const from = addCivilDays(date, -((anchor.getUTCDay() + 6) % 7));
  return { from, to: addCivilDays(from, 7), days: Array.from({ length: 7 }, (_, day) => addCivilDays(from, day)) };
};
export const formatCivilDate = (date: string, options: Intl.DateTimeFormatOptions = { day: 'numeric', month: 'short', year: 'numeric' }): string =>
  new Intl.DateTimeFormat('es-PE', { ...options, timeZone: 'UTC' }).format(civilAnchor(date));
export const formatWeek = (from: string, to: string): string => `${formatCivilDate(from)} – ${formatCivilDate(addCivilDays(to, -1))}`;

export const compareReservations = (a: ReservationDto, b: ReservationDto): number =>
  Date.parse(a.dateTime) - Date.parse(b.dateTime) || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0);
export const localReservationStart = (reservation: ReservationDto, timeZone: string) => {
  const local = toBusinessLocalInput(reservation.dateTime, timeZone);
  return { date: local.split('T')[0], time: local.split('T')[1] };
};
export const agendaRows = (reservations: ReservationDto[], timeZone: string, days: string[]) => {
  const rows = new Map<string, Map<string, ReservationDto[]>>();
  for (const reservation of [...reservations].sort(compareReservations)) {
    const { date, time } = localReservationStart(reservation, timeZone);
    if (!time || !days.includes(date)) continue;
    const row = rows.get(time) ?? new Map<string, ReservationDto[]>();
    const cell = row.get(date) ?? [];
    cell.push(reservation);
    row.set(date, cell);
    rows.set(time, row);
  }
  return [...rows].sort(([a], [b]) => a.localeCompare(b));
};
const searchable = (value: string) => value.normalize('NFD').replace(/\p{Diacritic}/gu, '').toLocaleLowerCase('es');
export const filterReservations = (reservations: ReservationDto[], status: StatusFilter, search: string, serviceNames: ReadonlyMap<string, string>) => {
  const term = searchable(search.trim());
  return reservations.filter(reservation => (status === 'all' || reservation.status === status)
    && (!term || searchable(`${reservation.customerName} ${serviceNames.get(reservation.serviceId) ?? ''}`).includes(term)))
    .sort(compareReservations);
};
export const summarizeReservations = (reservations: ReservationDto[]) => {
  const counts = { Pending: 0, Confirmed: 0, Completed: 0, Cancelled: 0 };
  for (const reservation of reservations) counts[reservation.status]++;
  return counts;
};
export const servicesForLocation = (services: ServiceDto[], locationId: string): ServiceDto[] =>
  !locationId || locationId === 'all' ? [] : services.filter(service => service.isActive && service.requiresReservation
    && (service.durationInMinutes ?? 0) >= 5 && (service.locationScope === 'ALL' || service.locationIds.includes(locationId)));

export const canRetainWeek = (previous: readonly unknown[] | undefined, next: readonly unknown[]): boolean =>
  Boolean(previous && previous.length === 9 && next.length === 9 && typeof next[1] === 'string'
    && next[0] === 'workspace' && next[2] === 'reservations' && next[3] === 'list' && next[5] === 'range'
    && previous[0] === 'workspace' && previous[1] === next[1] && previous[2] === 'reservations'
    && previous[3] === 'list' && previous[4] === next[4] && previous[5] === 'range' && previous[8] === next[8]);
