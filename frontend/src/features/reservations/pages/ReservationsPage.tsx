import { useState, useEffect } from 'react';
import { Calendar as CalendarIcon, MapPin, List, CalendarDays } from 'lucide-react';
import { getReservations, cancelReservation, completeReservation } from '../services/reservation.service';
import { getLocations } from '../../business/services/business.service';
import { CreateReservationModal } from '../components/CreateReservationModal';
import { EditReservationModal } from '../components/EditReservationModal';
import { ReservationList } from '../components/ReservationList';
import { useCacheStore } from '../../../core/store/useCacheStore';
import type { ReservationDto } from '../types/reservation.types';
import type { LocationDto } from '../../business/types/business.types';

export const ReservationsPage = () => {
  // 🔥 CORRECCIÓN: Usamos fetchServices en lugar del viejo fetchData
  const { services, fetchServices } = useCacheStore();
  
  const [reservations, setReservations] = useState<ReservationDto[]>([]);
  const [locations, setLocations] = useState<LocationDto[]>([]);
  
  const [selectedLocation, setSelectedLocation] = useState<string>('');
  const [selectedDate, setSelectedDate] = useState<string>(() => {
    const today = new Date();
    today.setMinutes(today.getMinutes() - today.getTimezoneOffset());
    return today.toISOString().split('T')[0];
  });
  
  const [isLoading, setIsLoading] = useState(true);
  const [viewMode, setViewMode] = useState<'list' | 'calendar'>('list');

  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editingRes, setEditingRes] = useState<ReservationDto | null>(null);

  useEffect(() => {
    fetchServices(); // 🔥 Cargamos los servicios al caché global
    loadInitialData();
  }, [fetchServices]);

  useEffect(() => {
    if (selectedLocation && selectedLocation !== 'global') loadReservations();
  }, [selectedLocation, selectedDate]);

  const loadInitialData = async () => {
    try {
      const locs = await getLocations();
      setLocations(locs);
      if (locs.length > 0) setSelectedLocation(locs.find(l => l.isMain)?.id || locs[0].id || 'global');
    } catch (error) {
      console.error("Error al cargar sedes", error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadReservations = async () => {
    setIsLoading(true);
    try {
      const data = await getReservations(selectedLocation, selectedDate);
      setReservations(data || []);
    } catch (error) {
      console.error("Error cargando reservas", error);
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
      alert(`Error al cancelar: ${error.message}`);
    }
  };

  const handleComplete = async (id: string) => {
    if (!confirm('¿Marcar esta cita como Completada?')) return;
    try {
      await completeReservation(id);
      setReservations(reservations.map(r => r.id === id ? { ...r, status: 'Completed' } : r));
    } catch (error: any) {
      alert(`Error al completar: ${error.message}`);
    }
  };

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-6 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <CalendarIcon className="w-6 h-6 mr-3 text-blue-600" /> Gestión de Reservas
          </h1>
          <p className="mt-1 text-sm text-gray-500">Administra, reagenda y cancela citas manualmente.</p>
        </div>
        
        <div className="flex flex-wrap items-center gap-3">
          <div className="flex items-center bg-white border border-gray-200 rounded-lg px-3 py-2 shadow-sm">
            <MapPin className="w-4 h-4 text-gray-400 mr-2" />
            <select 
              value={selectedLocation} 
              onChange={(e) => setSelectedLocation(e.target.value)}
              className="bg-transparent text-sm outline-none text-gray-700"
            >
              <option value="global" disabled>Selecciona una sede</option>
              {locations.map(loc => <option key={loc.id} value={loc.id}>{loc.name}</option>)}
            </select>
          </div>
          
          <div className="flex items-center bg-white border border-gray-200 rounded-lg px-3 py-2 shadow-sm">
            <input 
              type="date" 
              value={selectedDate}
              onChange={(e) => setSelectedDate(e.target.value)}
              className="bg-transparent text-sm outline-none text-gray-700"
            />
          </div>

          <div className="flex bg-gray-100 p-1 rounded-lg">
            <button onClick={() => setViewMode('list')} className={`p-1.5 rounded-md ${viewMode === 'list' ? 'bg-white shadow-sm text-blue-600' : 'text-gray-500'}`}><List className="w-4 h-4" /></button>
            <button onClick={() => setViewMode('calendar')} className={`p-1.5 rounded-md ${viewMode === 'calendar' ? 'bg-white shadow-sm text-blue-600' : 'text-gray-500'}`}><CalendarDays className="w-4 h-4" /></button>
          </div>

          <button 
            onClick={() => setIsCreateModalOpen(true)}
            className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700"
          >
            Nueva Reserva
          </button>
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        {isLoading ? (
          <div className="flex flex-col items-center justify-center h-64 text-gray-400">
            <div className="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin mb-4"></div>
            <p className="text-sm">Cargando agenda...</p>
          </div>
        ) : viewMode === 'list' ? (
          <ReservationList 
            reservations={reservations} 
            services={services || []} 
            onEdit={(res) => { setEditingRes(res); setIsEditModalOpen(true); }} 
            onCancel={handleCancel} 
            onComplete={handleComplete}
          />
        ) : (
          <div className="flex flex-col items-center justify-center h-96 text-gray-400 bg-gray-50">
            <CalendarDays className="w-16 h-16 text-gray-300 mb-4" />
            <h3 className="text-lg font-medium text-gray-600">Vista de Calendario Inteligente</h3>
            <p className="text-sm mt-2 max-w-md text-center">Espacio reservado para FullCalendar.</p>
          </div>
        )}
      </div>

      <CreateReservationModal 
        isOpen={isCreateModalOpen} 
        onClose={() => setIsCreateModalOpen(false)} 
        onSuccess={loadReservations} 
        locations={locations} 
        services={services || []} 
      />

      <EditReservationModal 
        isOpen={isEditModalOpen}
        onClose={() => { setIsEditModalOpen(false); setEditingRes(null); }}
        onSuccess={loadReservations}
        reservation={editingRes}
      />
    </div>
  );
};