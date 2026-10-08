import { useMemo } from 'react';
import { Button } from '../../../components/ui/Button';
import { EmptyState } from '../../../components/ui/Feedback';
import { FormField, Select } from '../../../components/ui/Form';
import { agendaRows, compareReservations, formatCivilDate, localReservationStart } from '../utils/weeklyAgenda';
import type { ReservationDto } from '../types/reservation.types';
import { ReservationCard } from './ReservationCard';
import type { ReservationViewProps } from './ReservationCard';

interface WeeklyAgendaProps extends ReservationViewProps {
  reservations: ReservationDto[];
  days: string[];
  selectedDay: string;
  today: string;
  canCreate: boolean;
  onSelectDay: (date: string) => void;
  onCreate: (date: string) => void;
}
export const WeeklyAgenda = ({ reservations, days, selectedDay, today, canCreate, onSelectDay, onCreate, ...view }: WeeklyAgendaProps) => {
  const rows = useMemo(() => agendaRows(reservations, view.timeZone, days), [reservations, view.timeZone, days]);
  const dayReservations = useMemo(() => reservations.filter(reservation => localReservationStart(reservation, view.timeZone).date === selectedDay).sort(compareReservations), [reservations, view.timeZone, selectedDay]);
  return <>
    <div className="hidden lg:block max-h-[70vh] overflow-auto" tabIndex={0} role="region" aria-label="Agenda semanal; desplázate para ver todas las reservas">
      <table className="w-full table-fixed border-collapse min-w-[1120px] text-sm">
        <caption className="sr-only">Agenda de lunes a domingo. Se muestran únicamente horas con reservas; no se representa duración.</caption>
        <thead className="sticky top-0 z-10 bg-surface-soft">
          <tr><th scope="col" className="w-16 px-2 py-4 text-muted">Hora</th>{days.map(day => <th key={day} scope="col" className="border-l border-line px-2 py-3">
            <Button variant="ghost" className="w-full flex-col gap-0 px-1" disabled={!canCreate} onClick={() => onCreate(day)} aria-label={`Nueva reserva para ${formatCivilDate(day, { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}`}>
              <span className="capitalize text-foreground">{formatCivilDate(day, { weekday: 'long' })}</span>
              <span className="text-xs text-muted">{formatCivilDate(day, { day: 'numeric', month: 'short' })}{day === today ? ' · Hoy' : ''}</span>
            </Button>
          </th>)}</tr>
        </thead>
        <tbody>{rows.map(([time, cells]) => <tr key={time} className="border-t border-line">
          <th scope="row" className="align-top py-4 px-2 font-medium text-muted">{time}</th>
          {days.map(day => <td key={day} className="align-top border-l border-line p-2"><div className="space-y-2">{(cells.get(day) ?? []).map(reservation => <ReservationCard key={reservation.id} {...view} reservation={reservation} />)}</div></td>)}
        </tr>)}</tbody>
      </table>
      {rows.length === 0 && <EmptyState title="Sin reservas para mostrar esta semana" description="Prueba otros filtros o pulsa un día para agendar una nueva cita." />}
    </div>
    <div className="lg:hidden p-4 space-y-4">
      <FormField label="Día de la agenda"><Select value={selectedDay} onChange={event => onSelectDay(event.target.value)}>{days.map(day => <option key={day} value={day}>{formatCivilDate(day, { weekday: 'long', day: 'numeric', month: 'short' })}{day === today ? ' · Hoy' : ''}</option>)}</Select></FormField>
      <p className="text-sm text-muted" aria-live="polite">{dayReservations.length} {dayReservations.length === 1 ? 'reserva' : 'reservas'} en este día</p>
      {dayReservations.length === 0 ? <EmptyState title="Sin reservas para mostrar este día" description="Puedes elegir otro día o crear una nueva cita." /> : dayReservations.map(reservation => <ReservationCard key={reservation.id} {...view} reservation={reservation} />)}
      {canCreate && <Button variant="secondary" className="w-full" onClick={() => onCreate(selectedDay)}>Nueva reserva para este día</Button>}
    </div>
  </>;
};
