import { useState, useEffect } from 'react';
import { Calendar as CalendarIcon, Clock, Phone, CheckCircle, XCircle, MapPin } from 'lucide-react';
import { getReservations, cancelReservation } from '../services/reservation.service';
import { getLocations } from '../../business/services/business.service';
import type { ReservationDto } from '../types/reservation.types';
import type { LocationDto } from '../../business/types/business.types';

export const ReservationsPage = () => {
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [locations, setLocations] = useState<LocationDto[]>([]);
  
  // 🔥 CORRECCIÓN (Fallo #22): Forzar que la fecha base sea la local (Perú) y no la UTC del navegador
  const [selectedLocation, setSelectedLocation] = useState<string>('');
  const [selectedDate, setSelectedDate] = useState<string>(() => {
    const today = new Date();
    today.setMinutes(today.getMinutes() - today.getTimezoneOffset());
    return today.toISOString().split('T')[0];
  });
  
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    loadInitialData();
  }, []);

  useEffect(() => {
    if (selectedLocation && selectedLocation !== 'global') {
      loadReservations();
    }
  }, [selectedLocation, selectedDate]);

  const loadInitialData = async () => {
    try {
      const locs = await getLocations();
      setLocations(locs);
      
      if (locs.length > 0) {
        const mainLoc = locs.find(l => l.isMain) || locs[0];
        setSelectedLocation(mainLoc.id ?? 'global');
      } else {
        setSelectedLocation('global'); 
      }
    } catch (error: any) {
      console.error("Error al cargar sedes:", error.message);
    } finally {
      setIsLoading(false);
    }
  };

  const loadReservations = async () => {
    setIsLoading(true);
    try {
      const data = await getReservations(selectedLocation, selectedDate);
      const sorted = (data || []).sort((a, b) => new Date(a.dateTime).getTime() - new Date(b.dateTime).getTime());
      setReservations(sorted);
    } catch (error: any) {
      alert(`Error cargando reservas: ${error.response?.data?.error || error.message || 'Error desconocido'}`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleCancel = async (id: string) => {
    if (!confirm('¿Estás seguro de cancelar esta reserva?')) return;
    
    try {
      await cancelReservation(id);
      setReservations(reservations.map(r => r.id === id ? { ...r, status: 'Cancelled' } : r));
    } catch (error: any) {
      alert(`Error al cancelar: ${error.response?.data?.error || error.message || 'Es posible que ya esté cancelada.'}`);
    }
  };

  // 🔥 CORRECCIÓN: Parseo explícito para la UI (America/Lima)
  const formatTime = (dateString: string) => {
    const date = new Date(dateString);
    return new Intl.DateTimeFormat('es-PE', {
      hour: '2-digit', minute: '2-digit', timeZone: 'America/Lima'
    }).format(date);
  };

  return (
    <div className="max-w-5xl">
      <div className="mb-8 flex flex-col md:flex-row justify-between md:items-end gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <CalendarIcon className="w-6 h-6 mr-3 text-blue-600" /> Agenda de Reservas
          </h1>
          <p className="mt-1 text-sm text-gray-500">Citas agendadas por la IA o manualmente.</p>
        </div>
        
        <div className="flex flex-col sm:flex-row items-center gap-3">
          <div className="flex items-center bg-white border rounded-lg px-3 py-2 shadow-sm">
            <MapPin className="w-4 h-4 text-gray-400 mr-2" />
            <select 
              value={selectedLocation} 
              onChange={(e) => setSelectedLocation(e.target.value)}
              className="bg-transparent text-sm outline-none text-gray-700"
            >
              <option value="global" disabled>Selecciona una sede</option>
              {locations.map(loc => (
                <option key={loc.id} value={loc.id}>{loc.name}</option>
              ))}
            </select>
          </div>
          
          <div className="flex items-center bg-white border rounded-lg px-3 py-2 shadow-sm">
            <input 
              type="date" 
              value={selectedDate}
              onChange={(e) => setSelectedDate(e.target.value)}
              className="bg-transparent text-sm outline-none text-gray-700"
            />
          </div>
        </div>
      </div>

      {isLoading ? (
        <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando agenda...</div>
      ) : (
        <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
          {reservations.length === 0 ? (
            <div className="p-12 text-center text-gray-500 flex flex-col items-center">
              <CalendarIcon className="w-12 h-12 text-gray-300 mb-3" />
              <p className="font-medium text-gray-900">Agenda libre</p>
              <p className="text-sm mt-1">No hay reservas para la fecha y sede seleccionadas.</p>
            </div>
          ) : (
            <ul className="divide-y divide-gray-200">
              {reservations.map((res) => (
                <li key={res.id} className="p-5 hover:bg-gray-50 transition-colors">
                  <div className="flex items-center justify-between">
                    <div className="flex items-start space-x-4">
                      <div className={`p-3 rounded-full ${res.status.toUpperCase() === 'CONFIRMED' ? 'bg-green-100 text-green-600' : 'bg-red-100 text-red-600'}`}>
                        {res.status.toUpperCase() === 'CONFIRMED' ? <CheckCircle className="w-6 h-6" /> : <XCircle className="w-6 h-6" />}
                      </div>
                      
                      <div>
                        <h4 className="text-lg font-semibold text-gray-900">
                          {res.customerName} <span className="text-sm font-normal text-gray-500">(Servicio: {res.serviceId.substring(0,8)})</span>
                        </h4>
                        
                        <div className="mt-1 flex items-center space-x-4 text-sm text-gray-500">
                          <span className="flex items-center text-blue-600 font-medium">
                            <Clock className="w-4 h-4 mr-1" /> {formatTime(res.dateTime)}
                          </span>
                          <span className="flex items-center">
                            <Phone className="w-4 h-4 mr-1" /> {res.customerIdentifier}
                          </span>
                        </div>
                      </div>
                    </div>

                    <div className="flex space-x-2">
                      {res.status.toUpperCase() !== 'CANCELLED' && (
                        <button 
                          onClick={() => handleCancel(res.id)}
                          className="px-3 py-1.5 bg-red-50 text-red-700 text-sm font-medium rounded-lg hover:bg-red-100 transition-colors"
                        >
                          Cancelar Cita
                        </button>
                      )}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </div>
  );
};