import { Search, Pencil, XCircle } from 'lucide-react';
import type { ReservationDto } from '../types/reservation.types';
import type { ServiceDto } from '../../business/types/business.types';

interface ReservationListProps {
  reservations: ReservationDto[];
  services: ServiceDto[];
  onEdit: (res: ReservationDto) => void;
  onCancel: (id: string) => void;
  onComplete: (id: string) => void;
}

export const ReservationList = ({ reservations, services, onEdit, onCancel, onComplete }: ReservationListProps) => {
  
  const normalizeStatus = (status: string | number) => {
    if (status === 0 || status === '0') return 'PENDING';
    if (status === 1 || status === '1') return 'CONFIRMED';
    if (status === 2 || status === '2') return 'COMPLETED';
    if (status === 3 || status === '3') return 'CANCELLED';
    if (status === 4 || status === '4') return 'NOSHOW';
    
    return String(status || '').toUpperCase();
  };

  const getStatusBadge = (rawStatus: string | number) => {
    const status = normalizeStatus(rawStatus);
    switch (status) {
      case 'CONFIRMED': return <span className="px-2 py-1 text-xs font-medium rounded-full bg-green-100 text-green-700">Confirmada</span>;
      case 'PENDING': return <span className="px-2 py-1 text-xs font-medium rounded-full bg-yellow-100 text-yellow-700">Pendiente</span>;
      case 'COMPLETED': return <span className="px-2 py-1 text-xs font-medium rounded-full bg-blue-100 text-blue-700">Completada</span>;
      case 'CANCELLED': return <span className="px-2 py-1 text-xs font-medium rounded-full bg-red-100 text-red-700">Cancelada</span>;
      default: return <span className="px-2 py-1 text-xs font-medium rounded-full bg-gray-100 text-gray-700">{status}</span>;
    }
  };

  if (reservations.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center py-12 text-gray-500">
        <Search className="w-10 h-10 text-gray-300 mb-3" />
        <p>No hay reservas agendadas para esta fecha.</p>
      </div>
    );
  }

  const sortedReservations = [...reservations].sort((a, b) => 
    new Date((a as any).startTime || a.dateTime).getTime() - new Date((b as any).startTime || b.dateTime).getTime()
  );

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
            const timeStr = (res as any).startTime || res.dateTime;
            const localTime = new Date(new Date(timeStr).toLocaleString('en-US', { timeZone: 'America/Lima' }));
            
            const normalizedStatus = normalizeStatus(res.status);
            const isCancelled = normalizedStatus === 'CANCELLED';

            return (
              <tr key={res.id} className={`hover:bg-gray-50 transition-colors ${isCancelled ? 'opacity-60 bg-gray-50/50' : ''}`}>
                <td className="px-6 py-4">
                  <span className="font-semibold text-gray-900">
                    {localTime.toLocaleTimeString('es-PE', { hour: '2-digit', minute: '2-digit' })}
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
                
                {/* 🔥 ÚNICA COLUMNA DE ACCIONES LIMPIA */}
                <td className="px-6 py-4 text-right">
                  {normalizedStatus === 'PENDING' || normalizedStatus === 'CONFIRMED' ? (
                    <div className="flex justify-end gap-2">
                      {/* Botón Completar */}
                      <button onClick={() => onComplete(res.id)} className="p-2 text-green-600 hover:bg-green-50 rounded-lg" title="Finalizar Reserva">
                        <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><polyline points="20 6 9 17 4 12"></polyline></svg>
                      </button>
                      
                      {/* Botón Editar */}
                      <button onClick={() => onEdit(res)} className="p-2 text-blue-600 hover:bg-blue-50 rounded-lg" title="Reagendar">
                        <Pencil className="w-4 h-4" />
                      </button>
                      
                      {/* Botón Cancelar */}
                      <button onClick={() => onCancel(res.id)} className="p-2 text-red-500 hover:bg-red-50 rounded-lg" title="Cancelar">
                        <XCircle className="w-4 h-4" />
                      </button>
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