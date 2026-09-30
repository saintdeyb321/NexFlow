import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Calendar as CalendarIcon, AlertCircle, MapPin } from 'lucide-react';
import { getReservations, cancelReservation, completeReservation } from '../services/reservation.service';
import { getLocations, getBusinessProfile } from '../../business/services/business.service';
import { getServices } from '../../services/services/services.service';
import { CreateReservationModal } from '../components/CreateReservationModal';
import { EditReservationModal } from '../components/EditReservationModal';
import { ReservationList } from '../components/ReservationList';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessToday } from '../../../core/utils/dateTime';
import type { ReservationDto } from '../types/reservation.types';
import type { ServiceDto } from '../../services/types/services.types';
import type { LocationDto } from '../../business/types/business.types';

export const ReservationsPage = () => {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);
  
  const { data: profile, error: profileError } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.business.profile(workspaceId), queryFn: ({ signal }) => getBusinessProfile(signal),
    enabled: Boolean(workspaceId) && can('BUSINESS_PROFILE', 'READ'),
  });
  const timeZone = profile?.timeZone;

  const [chosenDate, setSelectedDate] = useState('');
  const selectedDate = chosenDate || (timeZone ? getBusinessToday(timeZone) : '');
  
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [isEditModalOpen, setIsEditModalOpen] = useState(false);
  const [editingRes, setEditingRes] = useState<ReservationDto | null>(null);

  // 🔥 SPRINT 11: Estado para confirmaciones y notificaciones sin alert()
  const [notification, setNotification] = useState<{ msg: string, type: 'success' | 'error' } | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{ action: 'cancel' | 'complete', id: string, locationId: string } | null>(null);

  const isValidLocationSelected = Boolean(selectedLocationId && selectedLocationId !== 'all');

  const { data: services = [] } = useQuery<ServiceDto[]>({
    ...queryPolicies.stable,
    queryKey: queryKeys.services.list(workspaceId, selectedLocationId),
    queryFn: ({ signal }) => getServices(selectedLocationId, signal),
    enabled: Boolean(workspaceId) && isValidLocationSelected && can('SERVICES', 'READ'),

  });

  const { data: locations = [] } = useQuery<LocationDto[]>({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: Boolean(workspaceId) && can('LOCATIONS', 'READ'),
  });

  const { data: reservations = [], isLoading } = useQuery<ReservationDto[]>({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.reservations.list(workspaceId, selectedLocationId, selectedDate),
    queryFn: ({ signal }) => getReservations(selectedLocationId, selectedDate, signal),
    enabled: Boolean(workspaceId && selectedDate) && isValidLocationSelected && can('RESERVATIONS', 'READ'),
  });

  const cancelMutation = useSessionMutation({
    mutationFn: ({ id }: { id: string; locationId: string }) => cancelReservation(id),
    onSuccess: (_data, target) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, target.locationId) });
      setNotification({ msg: 'Reserva cancelada exitosamente.', type: 'success' });
      setConfirmDialog(null);
    },
    onError: (error: unknown) => {
      setNotification({ msg: getApiErrorPresentation(error), type: 'error' });
      setConfirmDialog(null);
    }
  });

  const completeMutation = useSessionMutation({
    mutationFn: ({ id }: { id: string; locationId: string }) => completeReservation(id),
    onSuccess: (_data, target) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, target.locationId) });
      setNotification({ msg: 'Reserva marcada como completada.', type: 'success' });
      setConfirmDialog(null);
    },
    onError: (error: unknown) => {
      setNotification({ msg: getApiErrorPresentation(error), type: 'error' });
      setConfirmDialog(null);
    }
  });

  const executeAction = () => {
    if (confirmDialog?.action === 'cancel' && can('RESERVATIONS', 'CANCEL')) cancelMutation.mutate(confirmDialog);
    if (confirmDialog?.action === 'complete' && can('RESERVATIONS', 'COMPLETE')) completeMutation.mutate(confirmDialog);
  };

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">
      
      {/* 🔥 Banner de notificaciones */}
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

      {/* 🔥 Modal de Confirmación Customizado */}
      {confirmDialog && (
        <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50">
          <div className="bg-white p-6 rounded-xl shadow-lg w-96 text-center animate-in zoom-in-95">
            <h3 className="text-lg font-bold text-gray-900 mb-2">
              {confirmDialog.action === 'cancel' ? '¿Cancelar reserva?' : '¿Completar reserva?'}
            </h3>
            <p className="text-sm text-gray-500 mb-6">
              {confirmDialog.action === 'cancel' ? 'El cliente perderá su espacio agendado.' : 'Esta acción marcará la cita como finalizada.'}
            </p>
            <div className="flex justify-center gap-3">
              <button onClick={() => setConfirmDialog(null)} className="px-4 py-2 bg-gray-100 rounded-lg text-gray-700 font-medium hover:bg-gray-200 transition-colors">No, volver</button>
              <button 
                onClick={executeAction} 
                disabled={cancelMutation.isPending || completeMutation.isPending || !can('RESERVATIONS', confirmDialog.action === 'cancel' ? 'CANCEL' : 'COMPLETE')}
                className={`px-4 py-2 text-white font-medium rounded-lg disabled:opacity-50 transition-colors ${confirmDialog.action === 'cancel' ? 'bg-red-600 hover:bg-red-700' : 'bg-green-600 hover:bg-green-700'}`}
              >
                Sí, {confirmDialog.action === 'cancel' ? 'cancelar' : 'completar'}
              </button>
            </div>
          </div>
        </div>
      )}

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
              disabled={!isValidLocationSelected}
            />
          </div>

          <div className="relative group">
            <button 
              onClick={() => setIsCreateModalOpen(true)}
              disabled={!isValidLocationSelected || !can('RESERVATIONS', 'CREATE') || !timeZone}
              className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:bg-gray-400 flex items-center transition-colors shadow-sm"
            >
              Nueva Reserva
            </button>
            {!isValidLocationSelected && (
              <div className="absolute top-full mt-2 right-0 w-64 bg-gray-900 text-white text-xs rounded p-2 opacity-0 group-hover:opacity-100 transition-opacity z-10 pointer-events-none flex items-start">
                <AlertCircle className="w-4 h-4 mr-2 shrink-0 text-yellow-400" />
                Debes seleccionar una sede específica en el panel lateral para poder crear o ver reservas.
              </div>
            )}
          </div>
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        {!isValidLocationSelected ? (
          <div className="flex flex-col items-center justify-center h-64 text-gray-400 bg-gray-50">
            <MapPin className="w-12 h-12 text-blue-300 mb-3" />
            <h3 className="text-lg font-medium text-gray-600">Selecciona una Sede</h3>
            <p className="text-sm mt-1 max-w-md text-center">Para gestionar las citas, elige una ubicación específica en el selector superior.</p>
          </div>
        ) : isLoading ? (
          <div className="flex flex-col items-center justify-center h-64 text-gray-400">
            <div className="w-8 h-8 border-4 border-blue-200 border-t-blue-600 rounded-full animate-spin mb-4"></div>
            <p className="text-sm">Cargando agenda...</p>
          </div>
        ) : !timeZone ? (
          <p className="p-6">{profileError ? getApiErrorPresentation(profileError) : 'Configura la zona horaria del perfil del negocio para ver la agenda.'}</p>
        ) : (
          <ReservationList 
            canEdit={can('RESERVATIONS', 'UPDATE') && Boolean(timeZone)}
            canCancel={can('RESERVATIONS', 'CANCEL')}
            canComplete={can('RESERVATIONS', 'COMPLETE')}
            reservations={reservations} 
            services={services} 
            timeZone={timeZone}
            onEdit={(res) => { setEditingRes(res); setIsEditModalOpen(true); }} 
            onCancel={(id) => setConfirmDialog({ action: 'cancel', id, locationId: selectedLocationId })} 
            onComplete={(id) => setConfirmDialog({ action: 'complete', id, locationId: selectedLocationId })}
          />
        )}
      </div>

      {timeZone && <CreateReservationModal
        isOpen={isCreateModalOpen && can('RESERVATIONS', 'CREATE') && Boolean(timeZone)}
        onClose={() => setIsCreateModalOpen(false)} 
        onSuccess={reservation => {
          queryClient.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) });
          queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, reservation.locationId) });
          setNotification({ msg: 'Reserva creada exitosamente.', type: 'success' });
        }} 
        locations={locations} 
        services={services}
        timeZone={timeZone}
      />}

      {timeZone && <EditReservationModal
        isOpen={isEditModalOpen && can('RESERVATIONS', 'UPDATE') && Boolean(timeZone)}
        onClose={() => { setIsEditModalOpen(false); setEditingRes(null); }}
        onSuccess={reservation => {
          queryClient.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) });
          queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, reservation.locationId) });
          setNotification({ msg: 'Reserva reprogramada exitosamente.', type: 'success' });
        }}
        reservation={editingRes}
        timeZone={timeZone}
      />}
    </div>
  );
};
