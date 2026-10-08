import { Check, Pencil, XCircle } from 'lucide-react';
import { IconButton } from '../../../components/ui/Button';
import { StatusBadge } from '../../../components/ui/Feedback';
import type { ReservationDto } from '../types/reservation.types';
import { localReservationStart, reservationStatusLabels } from '../utils/weeklyAgenda';

export interface ReservationViewProps {
  timeZone: string;
  serviceNames: ReadonlyMap<string, string>;
  locationNames: ReadonlyMap<string, string>;
  showLocation: boolean;
  canEdit: boolean;
  canCancel: boolean;
  canComplete: boolean;
  onEdit: (reservation: ReservationDto) => void;
  onCancel: (reservation: ReservationDto) => void;
  onComplete: (reservation: ReservationDto) => void;
}

export const ReservationStatus = ({ status }: { status: ReservationDto['status'] }) =>
  <StatusBadge label={reservationStatusLabels[status]} tone={status === 'Pending' ? 'warning' : status === 'Cancelled' ? 'neutral' : 'success'} />;

export const ReservationActions = ({ reservation, canEdit, canCancel, canComplete, onEdit, onCancel, onComplete }: ReservationViewProps & { reservation: ReservationDto }) => {
  const confirmed = reservation.status === 'Confirmed';
  const active = confirmed || reservation.status === 'Pending';
  if (!active || !(canCancel || (confirmed && (canEdit || canComplete)))) return <span className="text-xs text-muted">Sin acciones</span>;
  return <div className="flex flex-wrap items-center gap-1">
    {confirmed && canComplete && <IconButton label={`Completar reserva de ${reservation.customerName}`} title="Completar" onClick={() => onComplete(reservation)}><Check className="w-4 h-4" /></IconButton>}
    {confirmed && canEdit && <IconButton label={`Reagendar reserva de ${reservation.customerName}`} title="Reagendar" onClick={() => onEdit(reservation)}><Pencil className="w-4 h-4" /></IconButton>}
    {canCancel && <IconButton label={`Cancelar reserva de ${reservation.customerName}`} title="Cancelar" className="text-danger" onClick={() => onCancel(reservation)}><XCircle className="w-4 h-4" /></IconButton>}
  </div>;
};

export const ReservationCard = ({ reservation, ...view }: ReservationViewProps & { reservation: ReservationDto }) =>
  <article aria-label={`Reserva de ${reservation.customerName}`} className="rounded-xl border border-line bg-surface p-3 shadow-sm space-y-2 text-sm [overflow-wrap:anywhere]">
    <time dateTime={reservation.dateTime} className="font-semibold text-primary">{localReservationStart(reservation, view.timeZone).time}</time>
    <p className="font-semibold text-foreground">{reservation.customerName}</p>
    <p className="text-xs text-muted">{view.serviceNames.get(reservation.serviceId) ?? 'Servicio no disponible'}</p>
    {view.showLocation && <p className="text-xs text-muted">{view.locationNames.get(reservation.locationId) ?? `Sede: ${reservation.locationId}`}</p>}
    <ReservationStatus status={reservation.status} />
    <ReservationActions {...view} reservation={reservation} />
  </article>;
