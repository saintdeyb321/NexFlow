import { IconButton } from '../../../components/ui/Button';
import { EmptyState, StatusBadge } from '../../../components/ui/Feedback';
import { Search, Pencil, XCircle } from 'lucide-react';
import type { ReservationDto } from '../types/reservation.types';
import type { ServiceDto } from '../../services/types/services.types';

interface ReservationListProps {
  canEdit: boolean;
  canCancel: boolean;
  canComplete: boolean;
  reservations: ReservationDto[];
  services: ServiceDto[];
  timeZone: string;
  onEdit: (res: ReservationDto) => void;
  onCancel: (id: string) => void;
  onComplete: (id: string) => void;
}

export const ReservationList = ({ canEdit, canCancel, canComplete, reservations, services, timeZone, onEdit, onCancel, onComplete }: ReservationListProps) => {

  const normalizeStatus = (status: ReservationDto['status']) => status.toUpperCase();

  const getStatusBadge = (rawStatus: ReservationDto['status']) => {
    const status = normalizeStatus(rawStatus);
    switch (status) {
      case 'CONFIRMED': return <StatusBadge label="Confirmada" tone="success" />;
      case 'PENDING': return <StatusBadge label="Pendiente" tone="warning" />;
      case 'COMPLETED': return <StatusBadge label="Completada" tone="success" />;
      case 'CANCELLED': return <StatusBadge label="Cancelada" tone="neutral" />;
      default: return <StatusBadge label={status} />;
    }
  };

  if (!reservations || reservations.length === 0) {
    return (
      <EmptyState icon={<Search aria-hidden="true" className="w-10 h-10 text-gray-300" />} title="No hay reservas agendadas para esta fecha." />
    );
  }
  const sortedReservations = [...reservations].filter(r => r != null).sort((a, b) => {
    const dateA = new Date(a.dateTime).getTime();
    const dateB = new Date(b.dateTime).getTime();
    return dateA - dateB;
  });

  return (
    <div className="overflow-x-auto">
      <table className="nf-table nf-responsive-table">
        <thead>
          <tr className="bg-surface-soft border-b border-line text-xs font-semibold text-muted uppercase tracking-wider">
            <th className="px-6 py-4">Hora</th>
            <th className="px-6 py-4">Cliente</th>
            <th className="px-6 py-4">Contacto</th>
            <th className="px-6 py-4">Estado</th>
            <th className="px-6 py-4 text-right">Acciones</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-gray-100">
          {sortedReservations.map((res) => {
            const localTime = new Date(res.dateTime);

            const normalizedStatus = normalizeStatus(res.status);
            const isCancelled = normalizedStatus === 'CANCELLED';

            return (
              <tr key={res.id} className={`hover:bg-gray-50 transition-colors ${isCancelled ? 'opacity-60 bg-gray-50/50' : ''}`}>
                <td data-label="Hora" className="px-6 py-4">
                  <span className="font-semibold text-foreground">
                    {localTime.toLocaleTimeString('es-PE', { timeZone, hour: '2-digit', minute: '2-digit' })}
                  </span>
                </td>
                <td data-label="Cliente / Servicio" className="px-6 py-4">
                  <div><div className="font-medium text-foreground">{res.customerName}</div>
                  <div className="text-xs text-muted mt-0.5">
                    {services.find(s => s.id === res.serviceId)?.name || 'Servicio General'}
                  </div></div>
                </td>
                <td data-label="Contacto" className="px-6 py-4 text-sm text-muted">
                  {res.customerIdentifier}
                </td>
                <td data-label="Estado" className="px-6 py-4">
                  {getStatusBadge(res.status)}
                </td>
                <td data-label="Acciones" className="px-6 py-4 text-right">
                  {normalizedStatus === 'PENDING' || normalizedStatus === 'CONFIRMED' ? (
                    <div className="flex justify-end gap-2">
                      <IconButton variant="ghost" label="Finalizar Reserva" disabled={!canComplete || res.status !== 'Confirmed'} onClick={() => onComplete(res.id!)} className="" title="Finalizar Reserva">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>
                      </IconButton>
                      <IconButton variant="ghost" label="Reagendar" disabled={!canEdit || res.status !== 'Confirmed'} onClick={() => onEdit(res)} className="" title="Reagendar">
                        <Pencil aria-hidden="true" className="w-4 h-4" />
                      </IconButton>
                      <IconButton variant="ghost" label="Cancelar" disabled={!canCancel} onClick={() => onCancel(res.id!)} className="text-danger" title="Cancelar">
                        <XCircle aria-hidden="true" className="w-4 h-4" />
                      </IconButton>
                    </div>
                  ) : (
                    <span className="text-gray-400 text-sm italic">Sin acciones</span>
                  )}
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
};
