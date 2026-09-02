import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Calendar as CalendarIcon, List, CalendarDays, AlertCircle } from 'lucide-react';
import { getReservations, cancelReservation, completeReservation } from '../services/reservation.service';
import { getLocations, getServices } from '../../business/services/business.service';
import { CreateReservationModal } from '../components/CreateReservationModal';
import { EditReservationModal } from '../components/EditReservationModal';
import { ReservationList } from '../components/ReservationList';
import { useAuthStore } from '../../../core/store/useAuthStore';
import type { ReservationDto } from '../types/reservation.types';

export const ReservationsPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);

  const [selectedDate, setSelectedDate] = useState<string>(() => {
    const today = new Date();
    today.setMinutes(today.getMinutes() - today.getTimezoneOffset());
    return today.toISOString().split('T')[0];
  });
  
  const [viewMode, setViewMode] = useState<'list' | 'calendar'>('list');
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editingRes, setEditingRes] = useState<ReservationDto | null>(null);

  // 🔥 Sprint 5.1/5.2: Migración completa a TanStack Query
  const { data: services = [] } = useQuery({
    queryKey: ['services', workspaceId],
    queryFn: getServices,
    enabled: !!workspaceId,
    staleTime: 1000 * 60 * 10,
  });

  const { data: locations = [] } = useQuery({
    queryKey: ['locations', workspaceId],
    queryFn: getLocations,
    enabled: !!workspaceId,
  });

  const queryLocation = selectedLocationId === 'all' ? 'global' : selectedLocationId;
  const { data: reservations = [], isLoading } = useQuery({
    queryKey: ['reservations', workspaceId, queryLocation, selectedDate],
    queryFn: () => getReservations(queryLocation, selectedDate),
    enabled: !!workspaceId,
  });

  const cancelMutation = useMutation({
    mutationFn: cancelReservation,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reservations'] }),
    onError: (error: any) => alert(`Error al cancelar: ${error.message}`)
  });

  const completeMutation = useMutation({
    mutationFn: completeReservation,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reservations'] }),
    onError: (error: any) => alert(`Error al completar: ${error.message}`)
  });

  const handleCancel = (id: string) => {
    if (confirm('¿Estás seguro de cancelar esta reserva?')) cancelMutation.mutate(id);
  };

  const handleComplete = (id: string) => {
    if (confirm('¿Marcar esta cita como Completada?')) completeMutation.mutate(id);
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

          <div className="relative group">
            <button 
              onClick={() => setIsCreateModalOpen(true)}
              disabled={selectedLocationId === 'all'}
              className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:bg-gray-400 flex items-center transition-colors"
            >
              Nueva Reserva
            </button>
            {selectedLocationId === 'all' && (
              <div className="absolute top-full mt-2 right-0 w-64 bg-gray-900 text-white text-xs rounded p-2 opacity-0 group-hover:opacity-100 transition-opacity z-10 pointer-events-none flex items-start">
                <AlertCircle className="w-4 h-4 mr-2 shrink-0 text-yellow-400" />
                Debes seleccionar una sede específica en el panel lateral para poder crear una reserva.
              </div>
            )}
          </div>
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
        onSuccess={() => queryClient.invalidateQueries({ queryKey: ['reservations'] })} 
        locations={locations} 
        services={services || []} 
      />

      <EditReservationModal 
        isOpen={isEditModalOpen}
        onClose={() => { setIsEditModalOpen(false); setEditingRes(null); }}
        onSuccess={() => queryClient.invalidateQueries({ queryKey: ['reservations'] })}
        reservation={editingRes}
      />
    </div>
  );
};