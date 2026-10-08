import { EmptyState } from '../../../components/ui/Feedback';
import type { ReservationDto } from '../types/reservation.types';
import { compareReservations, formatCivilDate, localReservationStart } from '../utils/weeklyAgenda';
import { ReservationActions, ReservationStatus } from './ReservationCard';
import type { ReservationViewProps } from './ReservationCard';

export const ReservationList = ({ reservations, ...view }: ReservationViewProps & { reservations: ReservationDto[] }) => {
  if (reservations.length === 0) return <EmptyState title="Sin reservas para mostrar esta semana" description="Prueba otros filtros o una búsqueda diferente." />;
  return <div className="overflow-x-auto max-h-[70vh] overflow-y-auto" tabIndex={0} role="region" aria-label="Lista de reservas de la semana">
    <table className="nf-table nf-responsive-table">
      <caption className="sr-only">Reservas de la semana cargada, ordenadas por inicio e identificador.</caption>
      <thead><tr><th scope="col">Fecha / Hora</th><th scope="col">Cliente / Servicio</th><th scope="col">Contacto</th>{view.showLocation && <th scope="col">Sede</th>}<th scope="col">Estado</th><th scope="col">Acciones</th></tr></thead>
      <tbody>{[...reservations].sort(compareReservations).map(reservation => {
        const local = localReservationStart(reservation, view.timeZone);
        return <tr key={reservation.id}>
          <td data-label="Fecha / Hora"><time dateTime={reservation.dateTime} className="block"><span className="block text-xs text-muted">{formatCivilDate(local.date)}</span><span className="font-semibold">{local.time}</span></time></td>
          <td data-label="Cliente / Servicio"><div><p className="font-medium">{reservation.customerName}</p><p className="text-xs text-muted">{view.serviceNames.get(reservation.serviceId) ?? 'Servicio no disponible'}</p></div></td>
          <td data-label="Contacto">{reservation.customerIdentifier}</td>
          {view.showLocation && <td data-label="Sede">{view.locationNames.get(reservation.locationId) ?? `Sede: ${reservation.locationId}`}</td>}
          <td data-label="Estado"><ReservationStatus status={reservation.status} /></td>
          <td data-label="Acciones"><ReservationActions {...view} reservation={reservation} /></td>
        </tr>;
      })}</tbody>
    </table>
  </div>;
};
