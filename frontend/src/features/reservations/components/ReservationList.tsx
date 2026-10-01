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
      case 'COMPLETED': return <StatusBadge label="Completada" tone="info" />;
      case 'CANCELLED': return <StatusBadge label="Cancelada" tone="error" />;
      default: return <StatusBadge label={status} />;
    }
  };

  if (!reservations || reservations.length === 0) {
    return (
      <EmptyState icon={<Search className="w-10 h-10 text-gray-300" />} title="No hay reservas agendadas para esta fecha." />
    );
  }
  const sortedReservations = [...reservations].filter(r => r != null).sort((a, b) => {
    const dateA = new Date(a.dateTime).getTime();
    const dateB = new Date(b.dateTime).getTime();
    return dateA - dateB;
  });

  return (
    <div className="overflow-x-auto">
      <table className="w-full text-left border-collapse">
        <thead>
          <tr className="bg-gray-50 border-b border-gray-200 text-xs font-semibold text-gray-500 uppercase tracking-wider">
            <th className="px-6 py-4">Hora</th>
            <th className="px-6 py-4">Cliente</th>
            <th className="px-6 py-4 hidden md:table-cell">Contacto</th>
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
                <td className="px-6 py-4">
                  <span className="font-semibold text-gray-900">
                    {localTime.toLocaleTimeString('es-PE', { timeZone, hour: '2-digit', minute: '2-digit' })}
                  </span>
                </td>
                <td className="px-6 py-4">
                  <div className="font-medium text-gray-900">{res.customerName}</div>
                  <div className="text-xs text-gray-500 mt-0.5">
                    {services.find(s => s.id === res.serviceId)?.name || 'Servicio General'}
                  </div>
                </td>
                <td className="px-6 py-4 hidden md:table-cell text-sm text-gray-600">
                  {res.customerIdentifier}
                </td>
                <td className="px-6 py-4">
                  {getStatusBadge(res.status)}
                </td>
                <td className="px-6 py-4 text-right">
                  {normalizedStatus === 'PENDING' || normalizedStatus === 'CONFIRMED' ? (
                    <div className="flex justify-end gap-2">
                      <IconButton variant="ghost" label="Finalizar Reserva" disabled={!canComplete || res.status !== 'Confirmed'} onClick={() => onComplete(res.id!)} className="p-2 text-green-600 hover:bg-green-50 rounded-lg" title="Finalizar Reserva">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>
                      </IconButton>
                      <IconButton variant="ghost" label="Reagendar" disabled={!canEdit || res.status !== 'Confirmed'} onClick={() => onEdit(res)} className="p-2 text-blue-600 hover:bg-blue-50 rounded-lg" title="Reagendar">
                        <Pencil className="w-4 h-4" />
                      </IconButton>
                      <IconButton variant="ghost" label="Cancelar" disabled={!canCancel} onClick={() => onCancel(res.id!)} className="p-2 text-red-500 hover:bg-red-50 rounded-lg" title="Cancelar">
                        <XCircle className="w-4 h-4" />
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
