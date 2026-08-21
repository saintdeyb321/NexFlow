import { useState, useEffect } from 'react';
import { Calendar as CalendarIcon, Clock, Phone, CheckCircle, XCircle, AlertCircle } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getReservations, updateReservationStatus } from '../services/reservation.service';
import type { ReservationDto } from '../types/reservation.types';

export const ReservationsPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;

  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    if (workspaceId) {
      loadReservations();
    }
  }, [workspaceId]);

  const loadReservations = async () => {
    try {
      const data = await getReservations(workspaceId!);
      // Ordenar por fecha más próxima
      const sorted = (data || []).sort((a, b) => new Date(a.startTime).getTime() - new Date(b.startTime).getTime());
      setReservations(sorted);
    } catch (error) {
      console.error("Error cargando reservas:", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleStatusChange = async (id: string, newStatus: string) => {
    try {
      await updateReservationStatus(workspaceId!, id, newStatus);
      setReservations(reservations.map(r => r.id === id ? { ...r, status: newStatus } : r));
    } catch (error) {
      alert("Error al actualizar el estado");
    }
  };

  // Formateador de fechas para que se vea legible ("Hoy a las 14:30")
  const formatDateTime = (dateString: string) => {
    const date = new Date(dateString);
    return new Intl.DateTimeFormat('es-PE', {
      weekday: 'short', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
    }).format(date);
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando agenda...</div>;

  return (
    <div className="max-w-5xl">
      <div className="mb-8 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <CalendarIcon className="w-6 h-6 mr-3 text-blue-600" />
            Agenda de Reservas
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Citas agendadas por la IA o manualmente.
          </p>
        </div>
        <button onClick={loadReservations} className="text-sm text-blue-600 hover:underline">
          Actualizar agenda
        </button>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        {reservations.length === 0 ? (
          <div className="p-12 text-center text-gray-500 flex flex-col items-center">
            <CalendarIcon className="w-12 h-12 text-gray-300 mb-3" />
            <p className="font-medium text-gray-900">Agenda libre</p>
            <p className="text-sm mt-1">Aún no hay reservas para este entorno de trabajo.</p>
          </div>
        ) : (
          <ul className="divide-y divide-gray-200">
            {reservations.map((res) => (
              <li key={res.id} className="p-5 hover:bg-gray-50 transition-colors">
                <div className="flex items-center justify-between">
                  {/* Info Izquierda */}
                  <div className="flex items-start space-x-4">
                    <div className={`p-3 rounded-full ${res.status === 'CONFIRMED' ? 'bg-green-100 text-green-600' : res.status === 'CANCELLED' ? 'bg-red-100 text-red-600' : 'bg-yellow-100 text-yellow-600'}`}>
                      {res.status === 'CONFIRMED' ? <CheckCircle className="w-6 h-6" /> : res.status === 'CANCELLED' ? <XCircle className="w-6 h-6" /> : <AlertCircle className="w-6 h-6" />}
                    </div>
                    
                    <div>
                      <h4 className="text-lg font-semibold text-gray-900">
                        {/* En la V2 cruzaremos esto con el nombre real del servicio */}
                        Servicio ID: <span className="text-sm font-normal text-gray-500">{res.serviceId.substring(0,8)}...</span>
                      </h4>
                      
                      <div className="mt-1 flex items-center space-x-4 text-sm text-gray-500">
                        <span className="flex items-center text-blue-600 font-medium">
                          <Clock className="w-4 h-4 mr-1" /> {formatDateTime(res.startTime)}
                        </span>
                        <span className="flex items-center">
                          <Phone className="w-4 h-4 mr-1" /> {res.customerIdentifier}
                        </span>
                      </div>
                    </div>
                  </div>

                  {/* Botones Derecha */}
                  <div className="flex space-x-2">
                    {res.status !== 'CONFIRMED' && (
                      <button 
                        onClick={() => handleStatusChange(res.id, 'CONFIRMED')}
                        className="px-3 py-1.5 bg-green-50 text-green-700 text-sm font-medium rounded-lg hover:bg-green-100"
                      >
                        Confirmar
                      </button>
                    )}
                    {res.status !== 'CANCELLED' && (
                      <button 
                        onClick={() => handleStatusChange(res.id, 'CANCELLED')}
                        className="px-3 py-1.5 bg-red-50 text-red-700 text-sm font-medium rounded-lg hover:bg-red-100"
                      >
                        Cancelar
                      </button>
                    )}
                  </div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
};