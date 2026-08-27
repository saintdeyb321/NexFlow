import { useState, useEffect } from 'react';
import { Calendar as CalendarIcon, Clock, Phone, XCircle, MapPin } from 'lucide-react';
import { getReservations, cancelReservation } from '../services/reservation.service';
import { getLocations } from '../../business/services/business.service';
import type { ReservationDto } from '../types/reservation.types';
import type { LocationDto } from '../../business/types/business.types';

const HOURS = Array.from({ length: 13 }, (_, i) => i + 8); // 08:00 a 20:00

export const ReservationsPage = () => {
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [locations, setLocations] = useState<LocationDto[]>([]);
  
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
      setReservations(data || []);
    } catch (error: any) {
      alert(`Error cargando reservas: ${error.response?.data?.message || error.message}`);
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
      alert(`Error al cancelar: ${error.response?.data?.message || error.message}`);
    }
  };

  return (
    <div className="max-w-5xl">
      <div className="mb-8 flex flex-col md:flex-row justify-between md:items-end gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <CalendarIcon className="w-6 h-6 mr-3 text-blue-600" /> Agenda de Reservas
          </h1>
          <p className="mt-1 text-sm text-gray-500">Vista de calendario (08:00 - 20:00)</p>
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
        <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden pb-8 relative">
          
          {/* 🔥 SOLUCIÓN FALLO #56: Grilla Visual del Calendario */}
          <div className="relative mt-6">
            {HOURS.map((hour) => (
              <div key={hour} className="flex h-[60px] border-b border-gray-100 last:border-0 relative">
                <div className="w-20 text-right pr-4 text-xs font-medium text-gray-500 -mt-2">
                  {hour.toString().padStart(2, '0')}:00
                </div>
                <div className="flex-1 border-l border-gray-100"></div>
              </div>
            ))}

            {/* Bloques de Reserva */}
            {reservations.filter(r => r.status.toUpperCase() !== 'CANCELLED').map((res) => {
              // Tolerancia para el nombre de la variable (por si el backend envía startTime en lugar de dateTime)
              const timeStr = (res as any).startTime || res.dateTime;
              
              // Forzamos la lectura en la zona horaria de Perú
              const localTime = new Date(new Date(timeStr).toLocaleString('en-US', { timeZone: 'America/Lima' }));
              const h = localTime.getHours();
              const m = localTime.getMinutes();
              
              if (h < 8 || h > 20) return null; // Ocultamos las que estén fuera de la vista MVP

              // Calculamos posición exacta: 60px por hora
              const topPos = (h - 8) * 60 + m; 
              const blockHeight = 45; // Altura visual estándar

              return (
                <div 
                  key={res.id}
                  className="absolute left-20 right-6 ml-2 bg-blue-50 border-l-4 border-blue-500 rounded p-2 shadow-sm hover:shadow-md transition-all overflow-hidden group"
                  style={{ top: `${topPos}px`, height: `${blockHeight}px`, zIndex: 10 }}
                >
                  <div className="flex justify-between items-start h-full">
                    <div>
                      <p className="text-sm font-bold text-gray-900 leading-tight truncate">{res.customerName}</p>
                      <p className="text-xs text-blue-700 font-medium mt-0.5 flex items-center">
                        <Clock className="w-3 h-3 mr-1" />
                        {localTime.toLocaleTimeString('es-PE', { hour: '2-digit', minute: '2-digit' })} - <Phone className="w-3 h-3 mx-1"/> {res.customerIdentifier}
                      </p>
                    </div>
                    <button 
                      onClick={() => handleCancel(res.id)}
                      className="opacity-0 group-hover:opacity-100 text-red-500 hover:text-red-700 bg-red-100 p-1.5 rounded transition-all"
                      title="Cancelar Reserva"
                    >
                      <XCircle className="w-4 h-4" />
                    </button>
                  </div>
                </div>
              );
            })}
          </div>
          
          {reservations.length === 0 && (
             <div className="absolute inset-0 flex items-center justify-center pointer-events-none">
               <p className="text-gray-400 font-medium bg-white px-4 py-2 rounded-full border shadow-sm">No hay citas para hoy</p>
             </div>
          )}
        </div>
      )}
    </div>
  );
};