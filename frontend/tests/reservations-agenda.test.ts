import test from 'node:test';
import assert from 'node:assert/strict';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { QueryClient } from '@tanstack/react-query';
import { getBusinessToday, resolveBusinessTimeZone } from '../src/core/utils/dateTime.ts';
import { queryKeys } from '../src/core/query/queryKeys.ts';
import { stableQueryModule } from '../src/core/query/queryPolicies.ts';
import { can } from '../src/core/auth/permissions.ts';
import { weekOf, addCivilDays, agendaRows, filterReservations, summarizeReservations, servicesForLocation, canRetainWeek, localReservationStart, formatWeek } from '../src/features/reservations/utils/weeklyAgenda.ts';
import { getReservations, getReservationsForWeek } from '../src/features/reservations/services/reservation.service.ts';
import { invalidateReservationViews } from '../src/features/reservations/services/reservation.queries.ts';
import { WeeklyAgenda } from '../src/features/reservations/components/WeeklyAgenda.tsx';
import { WeekNavigation } from '../src/features/reservations/components/WeekNavigation.tsx';
import { ReservationList } from '../src/features/reservations/components/ReservationList.tsx';
import { ReservationActions } from '../src/features/reservations/components/ReservationCard.tsx';
import { calls, rowsFor } from './fixtures/reservations-api.ts';
import { useAuthStore } from './fixtures/reservations-store.ts';
import type { ServiceDto } from '../src/features/services/types/services.types.ts';

const week = weekOf('2026-12-31');
const rows = rowsFor('workspace-a', week.from);
const view = {
  timeZone: 'America/Lima', showLocation: true, serviceNames: new Map([['service-a', 'Consulta'], ['service-b', 'Sesión']]),
  locationNames: new Map([['location-a', 'Sede A'], ['location-b', 'Sede B']]),
  canEdit: true, canCancel: true, canComplete: true, onEdit: () => {}, onCancel: () => {}, onComplete: () => {},
};

test('lunes-domingo cruza mes/año con límite superior exclusivo', () => {
  assert.equal(week.from, '2026-12-28'); assert.equal(week.to, '2027-01-04');
  assert.equal(week.days.length, 7); assert.equal(week.days.at(-1), '2027-01-03');
  assert.match(formatWeek(week.from, week.to), /2026.*2027/);
});
test('navegación histórica, avance/retroceso y salto mantienen fechas civiles', () => {
  assert.equal(weekOf(addCivilDays(week.from, 7)).from, week.to);
  assert.equal(addCivilDays(addCivilDays(week.from, 7), -7), week.from);
  assert.equal(weekOf('2018-07-08').from, '2018-07-02');
  assert.equal(weekOf('2024-02-29').to, '2024-03-04');
  for (const date of ['2026-02-30', '2026-1-1', 'invalid']) assert.throws(() => weekOf(date));
});
test('el día actual proviene de la zona del negocio, no de UTC ni del servidor', () => {
  const instant = new Date('2026-10-05T03:30:00Z');
  assert.equal(getBusinessToday('America/Lima', instant), '2026-10-04');
  assert.equal(getBusinessToday('Asia/Kathmandu', instant), '2026-10-05');
  assert.equal(weekOf(getBusinessToday('America/Lima', instant)).from, '2026-09-28');
  assert.equal(resolveBusinessTimeZone('invalid'), 'America/Lima');
  assert.equal(resolveBusinessTimeZone(null), 'America/Lima');
});
test('DST muestra ambas ocurrencias de una hora y conserva el día sin desplazarlo', () => {
  const repeated = [
    { ...rows[0], id: 'first', dateTime: '2026-11-01T05:30:00Z' },
    { ...rows[0], id: 'second', dateTime: '2026-11-01T06:30:00Z' },
  ];
  const dstWeek = weekOf('2026-11-01');
  const grid = agendaRows(repeated, 'America/New_York', dstWeek.days);
  assert.equal(grid.length, 1); assert.equal(grid[0][0], '01:30');
  assert.deepEqual(grid[0][1].get('2026-11-01')?.map(row => row.id), ['first', 'second']);
  assert.deepEqual(localReservationStart({ ...rows[0], dateTime: '2026-03-08T07:30:00Z' }, 'America/New_York'), { date: '2026-03-08', time: '03:30' });
});
test('filas solo con inicios reales y reservas simultáneas ordenadas por instante/ID', () => {
  const grid = agendaRows(rows, view.timeZone, week.days);
  assert.deepEqual(grid.map(([time]) => time), ['10:00', '12:30', '14:00']);
  assert.deepEqual(grid[0][1].get(week.from)?.map(row => row.id), ['a', 'b']);
  assert.equal(grid[0][1].has(week.days[1]), false);
  assert.deepEqual(agendaRows([], view.timeZone, week.days), []);
  assert.equal(agendaRows([{ ...rows[0], dateTime: `${week.to}T15:00:00Z` }], view.timeZone, week.days).length, 0);
});
test('resumen conserva todos los estados históricos; filtros y búsqueda solo usan la semana recibida', () => {
  assert.deepEqual(summarizeReservations(rows), { Pending: 1, Confirmed: 1, Completed: 1, Cancelled: 1 });
  assert.equal(filterReservations(rows, 'all', '', view.serviceNames).length, 4);
  assert.deepEqual(filterReservations(rows, 'Cancelled', '', view.serviceNames).map(row => row.id), ['d']);
  assert.deepEqual(filterReservations(rows, 'all', 'JOSE', view.serviceNames).map(row => row.id), ['a']);
  assert.deepEqual(filterReservations(rows, 'all', 'sesion', view.serviceNames).map(row => row.id), ['a', 'd']);
  assert.equal(filterReservations([], 'all', 'Ana', view.serviceNames).length, 0);
  assert.equal(rows[2].status, 'Completed');
});
test('muchas reservas simultáneas se conservan sin superposición lógica ni horas vacías', () => {
  const many = Array.from({ length: 300 }, (_, index) => ({ ...rows[0], id: String(299 - index).padStart(3, '0') }));
  const grid = agendaRows(many, view.timeZone, week.days);
  assert.equal(grid.length, 1);
  const cell = grid[0][1].get(week.from)!;
  assert.equal(cell.length, 300); assert.equal(cell[0].id, '000'); assert.equal(cell.at(-1)?.id, '299');
});
test('consulta semanal usa una petición con params y cancelación, preservando llamada diaria', async () => {
  calls.length = 0;
  const controller = new AbortController();
  await getReservationsForWeek('all', week.from, week.to, controller.signal);
  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0].params, { locationId: 'all', from: week.from, to: week.to });
  await getReservations('location-a', '2018-07-02');
  assert.deepEqual(calls[1].params, { locationId: 'location-a', date: '2018-07-02' });
  controller.abort();
  await assert.rejects(() => getReservationsForWeek('all', week.from, week.to, controller.signal), /cancel/i);
  assert.equal(calls.length, 2);
});
test('query semanal deduplica lecturas y mantiene claves separadas por workspace/sede/rango', async () => {
  const client = new QueryClient(); calls.length = 0;
  const key = queryKeys.reservations.week('workspace-a', 'all', week.from, week.to);
  const options = { queryKey: key, queryFn: ({ signal }: { signal: AbortSignal }) => getReservationsForWeek('all', week.from, week.to, signal), staleTime: 10_000 };
  await Promise.all([client.fetchQuery(options), client.fetchQuery(options)]);
  await client.fetchQuery(options);
  assert.equal(calls.length, 1);
  assert.notDeepEqual(key, queryKeys.reservations.week('workspace-b', 'all', week.from, week.to));
  assert.notDeepEqual(key, queryKeys.reservations.week('workspace-a', 'location-a', week.from, week.to));
  assert.equal(stableQueryModule(key), null); // Reservation lists never enter persisted stable-resource cache.
  assert.equal(canRetainWeek(key, queryKeys.reservations.week('workspace-a', 'all', week.to, addCivilDays(week.to, 7))), true);
  assert.equal(canRetainWeek(key, queryKeys.reservations.week('workspace-b', 'all', week.from, week.to)), false);
  assert.equal(canRetainWeek(key, queryKeys.reservations.week('workspace-a', 'location-a', week.from, week.to)), false);
  assert.equal(canRetainWeek(key, queryKeys.reservations.list('workspace-a', 'all', week.from)), false);
  assert.equal(canRetainWeek(key, queryKeys.reservations.week('workspace-a', 'all', week.from, week.to, 'America/New_York')), false);
  client.clear();
});
test('mutaciones invalidan todas las vistas propias y solo disponibilidad de la sede afectada', async () => {
  const client = new QueryClient();
  const own = queryKeys.reservations.week('workspace-a', 'all', week.from, week.to);
  const foreign = queryKeys.reservations.week('workspace-b', 'all', week.from, week.to);
  const availability = queryKeys.reservations.slots('workspace-a', 'location-b', 'service-b', week.from);
  const other = queryKeys.reservations.slots('workspace-a', 'location-a', 'service-a', week.from);
  const services = queryKeys.services.list('workspace-a', 'all');
  for (const key of [own, foreign, availability, other, services]) client.setQueryData(key, []);
  await invalidateReservationViews(client, 'workspace-a', 'location-b');
  assert.equal(client.getQueryState(own)?.isInvalidated, true);
  assert.equal(client.getQueryState(availability)?.isInvalidated, true);
  for (const key of [foreign, other, services]) assert.equal(client.getQueryState(key)?.isInvalidated, false);
  client.clear();
});
test('crear exige sede concreta y servicio reservable perteneciente a ella', () => {
  const services: ServiceDto[] = [
    { id: 'global', categoryId: '', name: 'Consulta', type: 'SERVICE', priceMinorUnits: 0, currency: 'PEN', isActive: true, requiresReservation: true, durationInMinutes: 30, locationScope: 'ALL', locationIds: [] },
    { id: 'local', categoryId: '', name: 'Sesión', type: 'SERVICE', priceMinorUnits: 0, currency: 'PEN', isActive: true, requiresReservation: true, durationInMinutes: 30, locationScope: 'SPECIFIC', locationIds: ['location-b'] },
  ];
  assert.deepEqual(servicesForLocation(services, 'all'), []);
  assert.deepEqual(servicesForLocation(services, 'location-a').map(service => service.id), ['global']);
  assert.deepEqual(servicesForLocation(services, 'location-b').map(service => service.id), ['global', 'local']);
  assert.deepEqual(servicesForLocation(services.map(service => ({ ...service, isActive: false })), 'location-a'), []);
});
test('licencias y capacidades reales controlan lectura y mutaciones independientemente', () => {
  const me = useAuthStore.getState().me;
  assert.equal(can(me, 'RESERVATIONS', 'READ'), true);
  const readOnly = { ...me, capabilities: { ...me.capabilities, RESERVATIONS: ['READ'] } };
  assert.equal(can(readOnly, 'RESERVATIONS', 'READ'), true);
  assert.equal(can(readOnly, 'RESERVATIONS', 'CREATE'), false);
  assert.equal(can({ ...me, entitlements: [] }, 'RESERVATIONS', 'READ'), false);
  assert.equal(can({ ...me, workspace: null }, 'RESERVATIONS', 'READ'), false);
});
test('agenda renderiza siete días, tarjetas apiladas, sedes, selector móvil y sin duración inventada', () => {
  const html = renderToStaticMarkup(createElement(WeeklyAgenda, { ...view, reservations: rows, days: week.days, selectedDay: week.from, today: week.from, canCreate: true, onSelectDay: () => {}, onCreate: () => {} }));
  assert.equal((html.match(/scope="col"/g) ?? []).length, 8);
  assert.equal((html.match(/scope="row"/g) ?? []).length, 3);
  assert.match(html, /lg:hidden/); assert.match(html, /hidden lg:block/);
  assert.match(html, /Día de la agenda/); assert.match(html, /Sede B/);
  assert.match(html, /Nueva reserva para lunes/);
  assert.doesNotMatch(html, /30 min|60 min|endTime/);
});
test('semana vacía no fabrica filas de horas y mantiene navegación/creación accesible', () => {
  const html = renderToStaticMarkup(createElement(WeeklyAgenda, { ...view, reservations: [], days: week.days, selectedDay: week.from, today: week.from, canCreate: true, onSelectDay: () => {}, onCreate: () => {} }));
  assert.doesNotMatch(html, /scope="row"/); assert.match(html, /Sin reservas para mostrar esta semana/);
  const navigation = renderToStaticMarkup(createElement(WeekNavigation, { from: week.from, to: week.to, today: week.from, onChange: () => {} }));
  for (const label of ['Semana anterior', 'Semana siguiente', 'Esta semana', 'Ir a fecha']) assert.ok(navigation.includes(label));
});
test('lista ofrece fecha, hora, servicio, sede y todos los estados de la semana', () => {
  const html = renderToStaticMarkup(createElement(ReservationList, { ...view, reservations: rows }));
  for (const label of ['Fecha / Hora', 'Cliente / Servicio', 'Sede', 'Pendiente', 'Confirmada', 'Completada', 'Cancelada', '2026']) assert.ok(html.includes(label));
  assert.ok(html.indexOf('José Prueba') < html.indexOf('Ana Prueba'));
});
for (const status of ['Confirmed', 'Pending', 'Completed', 'Cancelled'] as const) test(`acciones de ${status} respetan transiciones existentes`, () => {
  const html = renderToStaticMarkup(createElement(ReservationActions, { ...view, reservation: { ...rows[0], status } }));
  assert.equal(html.includes('Reagendar'), status === 'Confirmed');
  assert.equal(html.includes('Completar'), status === 'Confirmed');
  assert.equal(html.includes('Cancelar'), status === 'Confirmed' || status === 'Pending');
  const denied = renderToStaticMarkup(createElement(ReservationActions, { ...view, canEdit: false, canCancel: false, canComplete: false, reservation: { ...rows[0], status } }));
  assert.doesNotMatch(denied, /<button/);
});
