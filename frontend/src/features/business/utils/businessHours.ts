import type { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys.ts';
import type { BusinessHoursDto } from '../types/business.types';

export const WEEK_DAYS = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' },
] as const;
export const DEFAULT_OPEN = '08:00';
export const DEFAULT_CLOSE = '20:00';
export type HoursErrors = Partial<Record<number, { openTime?: string; closeTime?: string }>>;
export type HoursSelection = 'all' | 'weekdays' | 'selected';
const validTime = (time: string) => /^([01]\d|2[0-3]):[0-5]\d$/.test(time);

// An empty response is different from a deliberately closed (or partial) schedule.
export const hoursWeek = (saved: BusinessHoursDto[]): BusinessHoursDto[] => {
  if (!Array.isArray(saved) || saved.some(hour => !hour || !Number.isInteger(hour.dayOfWeek)
    || hour.dayOfWeek < 0 || hour.dayOfWeek > 6 || typeof hour.openTime !== 'string'
    || typeof hour.closeTime !== 'string' || typeof hour.isClosed !== 'boolean')
    || new Set(saved.map(hour => hour.dayOfWeek)).size !== saved.length)
    throw new Error('Los horarios recibidos no forman una semana válida. Vuelve a cargar la sede.');
  return WEEK_DAYS.map(day => {
    const existing = saved.find(hour => hour.dayOfWeek === day.id);
    if (existing) return { ...existing };
    const suggestedOpen = saved.length === 0 && day.id !== 0;
    return { dayOfWeek: day.id, openTime: suggestedOpen ? DEFAULT_OPEN : '', closeTime: suggestedOpen ? DEFAULT_CLOSE : '', isClosed: !suggestedOpen };
  });
};
export const validateTimes = (openTime: string, closeTime: string) => {
  const errors: { openTime?: string; closeTime?: string } = {};
  if (!validTime(openTime)) errors.openTime = 'Introduce una apertura válida (HH:mm).';
  if (!validTime(closeTime)) errors.closeTime = 'Introduce un cierre válido (HH:mm).';
  if (!errors.openTime && !errors.closeTime && openTime >= closeTime) errors.closeTime = 'El cierre debe ser posterior a la apertura.';
  return errors;
};
export const validateHours = (hours: BusinessHoursDto[]): HoursErrors => Object.fromEntries(hours
  .filter(hour => !hour.isClosed)
  .map(hour => [hour.dayOfWeek, validateTimes(hour.openTime, hour.closeTime)] as const)
  .filter(([, errors]) => Object.keys(errors).length));
export const hoursPayload = (hours: BusinessHoursDto[]): BusinessHoursDto[] => {
  if (hours.length !== 7 || new Set(hours.map(hour => hour.dayOfWeek)).size !== 7
    || hours.some(hour => !Number.isInteger(hour.dayOfWeek) || hour.dayOfWeek < 0 || hour.dayOfWeek > 6)
    || Object.keys(validateHours(hours)).length)
    throw new Error('Revisa los siete días y sus horas antes de guardar.');
  return WEEK_DAYS.map(day => {
    const hour = hours.find(row => row.dayOfWeek === day.id)!;
    return { dayOfWeek: day.id, isClosed: hour.isClosed, openTime: hour.isClosed ? '' : hour.openTime, closeTime: hour.isClosed ? '' : hour.closeTime };
  });
};
export const setDayOpen = (hours: BusinessHoursDto[], day: number, open: boolean) => hours.map(hour => hour.dayOfWeek === day
  ? { ...hour, isClosed: !open, openTime: open ? hour.openTime || DEFAULT_OPEN : hour.openTime, closeTime: open ? hour.closeTime || DEFAULT_CLOSE : hour.closeTime } : hour);
export const applyHours = (hours: BusinessHoursDto[], selection: HoursSelection, selectedDays: number[], openTime: string, closeTime: string) => {
  const targets = selection === 'all' ? WEEK_DAYS.map(day => day.id) : selection === 'weekdays' ? [1, 2, 3, 4, 5, 6] : selectedDays;
  if (!targets.length || Object.keys(validateTimes(openTime, closeTime)).length) throw new Error('Selecciona días y un horario válido.');
  return hours.map(hour => targets.includes(hour.dayOfWeek) ? { ...hour, isClosed: false, openTime, closeTime } : hour);
};
export const sameHours = (a: BusinessHoursDto[], b: BusinessHoursDto[]) => a.length === b.length && a.every((hour, index) => {
  const other = b[index];
  return hour.dayOfWeek === other.dayOfWeek && hour.isClosed === other.isClosed && hour.openTime === other.openTime && hour.closeTime === other.closeTime;
});
export const invalidateHoursViews = (client: QueryClient, workspaceId: string, locationId: string) => Promise.all([
  client.invalidateQueries({ queryKey: queryKeys.hours.byLocation(workspaceId, locationId), exact: true }),
  client.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, locationId) }),
]);
