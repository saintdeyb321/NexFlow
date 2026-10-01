import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, EmptyState, ErrorState, Alert } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/Toast';
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
  const toast = useToast();
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

  const { data: reservations = [], isLoading, isError, refetch } = useQuery<ReservationDto[]>({
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
      toast.success('Reserva cancelada exitosamente.');
      setConfirmDialog(null);
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
      setConfirmDialog(null);
    }
  });

  const completeMutation = useSessionMutation({
    mutationFn: ({ id }: { id: string; locationId: string }) => completeReservation(id),
    onSuccess: (_data, target) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, target.locationId) });
      toast.success('Reserva marcada como completada.');
      setConfirmDialog(null);
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
      setConfirmDialog(null);
    }
  });

  const executeAction = () => {
    if (confirmDialog?.action === 'cancel' && can('RESERVATIONS', 'CANCEL')) cancelMutation.mutate(confirmDialog);
    if (confirmDialog?.action === 'complete' && can('RESERVATIONS', 'COMPLETE')) completeMutation.mutate(confirmDialog);
  };

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in">

      <ConfirmDialog isOpen={confirmDialog !== null} title={confirmDialog?.action === 'cancel' ? '¿Cancelar reserva?' : '¿Completar reserva?'} description={confirmDialog?.action === 'cancel' ? 'El cliente perderá su espacio agendado.' : 'Esta acción marcará la cita como finalizada.'} destructive={confirmDialog?.action === 'cancel'} confirmLabel={confirmDialog?.action === 'cancel' ? 'Sí, cancelar' : 'Sí, completar'} cancelLabel="No, volver" isLoading={cancelMutation.isPending || completeMutation.isPending} confirmDisabled={!can('RESERVATIONS', confirmDialog?.action === 'cancel' ? 'CANCEL' : 'COMPLETE')} onClose={() => setConfirmDialog(null)} onConfirm={executeAction} />

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
            <Button variant="primary"
              onClick={() => setIsCreateModalOpen(true)}
              disabled={!isValidLocationSelected || !can('RESERVATIONS', 'CREATE') || !timeZone}
              className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 disabled:bg-gray-400 flex items-center transition-colors shadow-sm"
            >
              Nueva Reserva
            </Button>
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
          <EmptyState className="h-64 bg-gray-50" icon={<MapPin className="w-12 h-12 text-blue-300" />} title="Selecciona una Sede" description="Para gestionar las citas, elige una ubicación específica en el selector superior." />
        ) : isLoading ? (
          <LoadingState className="h-64" title="Cargando agenda..." />
        ) : isError ? (
          <ErrorState onRetry={() => void refetch()} />
        ) : !timeZone ? (
          <Alert tone={profileError ? 'error' : 'warning'} className="m-6">{profileError ? getApiErrorPresentation(profileError) : 'Configura la zona horaria del perfil del negocio para ver la agenda.'}</Alert>
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
          toast.success('Reserva creada exitosamente.');
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
          toast.success('Reserva reprogramada exitosamente.');
        }}
        reservation={editingRes}
        timeZone={timeZone}
      />}
    </div>
  );
};
