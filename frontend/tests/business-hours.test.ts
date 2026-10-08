import test from 'node:test';
import assert from 'node:assert/strict';
import { QueryClient } from '@tanstack/react-query';
import { queryKeys } from '../src/core/query/queryKeys.ts';
import { applyHours, hoursPayload, hoursWeek, invalidateHoursViews, sameHours, setDayOpen, validateHours, validateTimes, WEEK_DAYS } from '../src/features/business/utils/businessHours.ts';
import type { BusinessHoursDto } from '../src/features/business/types/business.types.ts';

const closed: BusinessHoursDto[] = WEEK_DAYS.map(day => ({ dayOfWeek: day.id, openTime: '', closeTime: '', isClosed: true }));
test('respuesta vacía propone seis días, domingo cerrado; solo crea objetos locales', () => {
  const saved: BusinessHoursDto[] = [];
  const week = hoursWeek(saved);
  assert.deepEqual(saved, []);
  assert.deepEqual(week.map(hour => hour.dayOfWeek), [1, 2, 3, 4, 5, 6, 0]);
  assert.equal(week.filter(hour => !hour.isClosed).length, 6);
  for (const hour of week.slice(0, 6)) assert.deepEqual(hour, { dayOfWeek: hour.dayOfWeek, openTime: '08:00', closeTime: '20:00', isClosed: false });
  assert.deepEqual(week[6], closed[6]);
});
test('siete días cerrados guardados no reciben ninguna sugerencia', () => {
  assert.deepEqual(hoursWeek(closed), closed);
  assert.deepEqual(hoursPayload(hoursWeek(closed)), closed);
});
test('semana parcial conserva exactamente los registros existentes, sin mutar ni inventar horas abiertas', () => {
  const saved = [{ dayOfWeek: 3, openTime: '10:15', closeTime: '14:45', isClosed: false }, closed[6]];
  const before = structuredClone(saved); const week = hoursWeek(saved);
  assert.deepEqual(week[2], saved[0]); assert.deepEqual(week[6], saved[1]);
  assert.equal(week.filter(hour => !hour.isClosed).length, 1);
  assert.deepEqual(saved, before); assert.notEqual(week[2], saved[0]);
});
test('respuesta ambigua o inválida falla sin reemplazar registros por presets', () => {
  for (const rows of [[closed[0], closed[0]], [{ ...closed[0], dayOfWeek: 7 }], [{ ...closed[0], dayOfWeek: 1.5 }], null])
    assert.throws(() => hoursWeek(rows as BusinessHoursDto[]));
});
for (const selection of ['all', 'weekdays', 'selected'] as const) test(`aplicación masiva ${selection} modifica solamente los días destinatarios`, () => {
  const original = hoursWeek([]); original[2] = { ...original[2], openTime: '11:30', closeTime: '16:45' };
  const before = structuredClone(original); const next = applyHours(original, selection, [1, 0], '09:15', '17:30');
  const selected = selection === 'all' ? [0, 1, 2, 3, 4, 5, 6] : selection === 'weekdays' ? [1, 2, 3, 4, 5, 6] : [1, 0];
  for (const hour of next) {
    if (selected.includes(hour.dayOfWeek)) assert.deepEqual(hour, { dayOfWeek: hour.dayOfWeek, openTime: '09:15', closeTime: '17:30', isClosed: false });
    else assert.deepEqual(hour, original.find(row => row.dayOfWeek === hour.dayOfWeek));
  }
  assert.deepEqual(original, before);
});
test('abrir día cerrado ofrece horas válidas; cerrar/reabrir conserva las horas locales y normaliza únicamente el payload', () => {
  let week = setDayOpen(hoursWeek(closed), 0, true);
  assert.deepEqual(week[6], { dayOfWeek: 0, isClosed: false, openTime: '08:00', closeTime: '20:00' });
  week[6] = { ...week[6], openTime: '11:00', closeTime: '18:15' };
  week = setDayOpen(week, 0, false);
  assert.equal(week[6].openTime, '11:00'); assert.equal(week[6].closeTime, '18:15');
  assert.deepEqual(hoursPayload(week)[6], closed[6]);
  week = setDayOpen(week, 0, true);
  assert.equal(week[6].openTime, '11:00'); assert.equal(week[6].closeTime, '18:15');
});
test('validación estricta HH:mm, apertura anterior a cierre, selección y semana completa única', () => {
  for (const [open, close] of [['8:00', '20:00'], ['08:00:00', '20:00'], ['24:00', '20:00'], ['08:60', '20:00'], ['20:00', '20:00'], ['21:00', '20:00'], ['', '20:00']])
    assert.ok(Object.keys(validateTimes(open, close)).length);
  assert.deepEqual(validateTimes('00:00', '23:59'), {});
  assert.throws(() => applyHours(closed, 'selected', [], '08:00', '20:00'));
  assert.throws(() => applyHours(closed, 'all', [], '20:00', '08:00'));
  const week = hoursWeek([]);
  for (const invalid of [week.slice(0, 6), [...week.slice(0, 6), week[0]], [...week.slice(0, 6), { ...week[6], dayOfWeek: 7 }]]) assert.throws(() => hoursPayload(invalid));
  week[0] = { ...week[0], openTime: '' };
  assert.ok(validateHours(week)[1]?.openTime); assert.throws(() => hoursPayload(week));
});
test('payload semanal exacto, orden lunes-domingo, campos DTO y horarios cerrados vacíos', () => {
  const week = hoursWeek([]); week[6] = { ...week[6], openTime: '09:00', closeTime: '18:00' };
  const payload = hoursPayload([...week].reverse());
  assert.equal(payload.length, 7); assert.equal(new Set(payload.map(row => row.dayOfWeek)).size, 7);
  assert.deepEqual(payload.map(row => row.dayOfWeek), [1, 2, 3, 4, 5, 6, 0]);
  assert.deepEqual(payload[6], closed[6]);
  for (const row of payload) assert.deepEqual(Object.keys(row).sort(), ['closeTime', 'dayOfWeek', 'isClosed', 'openTime']);
  assert.equal(week[6].openTime, '09:00');
});
test('descartar recupera borrador de servidor/propuesta sin alterar originales', () => {
  for (const original of [[], closed] as BusinessHoursDto[][]) {
    const baseline = hoursWeek(original); const changed = applyHours(baseline, 'all', [], '09:00', '19:00');
    assert.equal(sameHours(changed, baseline), false);
    assert.equal(sameHours(hoursWeek(original), baseline), true);
  }
});
test('invalidación alcanza solo horas exactas y disponibilidad de workspace/sede guardados', async () => {
  const client = new QueryClient();
  const target = [queryKeys.hours.byLocation('a', 'x'), queryKeys.reservations.slots('a', 'x', 'service', '2026-10-08')];
  const untouched = [queryKeys.hours.byLocation('a', 'y'), queryKeys.hours.byLocation('b', 'x'), queryKeys.reservations.slots('a', 'y', 'service', '2026-10-08'), queryKeys.reservations.slots('b', 'x', 'service', '2026-10-08'), queryKeys.reservations.list('a', 'x', '2026-10-08')];
  for (const key of [...target, ...untouched]) client.setQueryData(key, []);
  await invalidateHoursViews(client, 'a', 'x');
  for (const key of target) assert.equal(client.getQueryState(key)?.isInvalidated, true);
  for (const key of untouched) assert.equal(client.getQueryState(key)?.isInvalidated, false);
  client.clear();
});
