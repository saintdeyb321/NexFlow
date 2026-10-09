import { Button } from '../../../components/ui/Button';
import { Input, Select, FormField } from '../../../components/ui/Form';
import { Alert, LoadingState, EmptyState, ErrorState } from '../../../components/ui/Feedback';
import { Modal } from '../../../components/ui/Modal';
import { useToast } from '../../../components/ui/useToast';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import type { ReservationDto } from '../types/reservation.types';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Clock, Calendar as CalendarIcon } from 'lucide-react';
import { createReservation, getAvailability } from '../services/reservation.service';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessToday } from '../../../core/utils/dateTime';
import { isCivilDate, servicesForLocation } from '../utils/weeklyAgenda';
import type { LocationDto } from '../../business/types/business.types';
import type { ServiceDto } from '../../services/types/services.types';

interface CreateReservationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: (reservation: ReservationDto) => void;
  locations: LocationDto[];
  services: ServiceDto[];
  timeZone: string;
  initialDate?: string;
}

export const CreateReservationModal = (props: CreateReservationModalProps) => {
  const locationId = useAuthStore(state => state.selectedLocationId);
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  if (!props.isOpen) return null;
  return <CreateReservationForm key={`${workspaceId}:${locationId}:${props.timeZone}:${props.initialDate ?? ''}`} {...props} />;
};

const CreateReservationForm = ({ isOpen, onClose, onSuccess, locations, services, timeZone, initialDate }: CreateReservationModalProps) => {
  const toast = useToast();
  const globalLocationId = useAuthStore(state => state.selectedLocationId);
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [slotReviewTime, setSlotReviewTime] = useState(() => Date.now());
  const [locationId, setLocationId] = useState(globalLocationId === 'all' ? '' : globalLocationId);
  const eligibleServices = servicesForLocation(services, locationId);
  const today = getBusinessToday(timeZone);

  const [formData, setFormData] = useState({
    serviceId: eligibleServices[0]?.id || '',
    customerName: '',
    customerIdentifier: '',
    date: initialDate || today,
    timeSlot: '' // Ahora guardamos el ISO string exacto devuelto por la disponibilidad
  });
  const selectedService = eligibleServices.find(service => service.id === formData.serviceId);
  const concreteLocation = locationId !== 'all' && locations.some(location => location.id === locationId);
  const validDate = isCivilDate(formData.date) && formData.date >= today;

  const { data: slots = [], isLoading: isLoadingSlots, isFetching: isFetchingSlots, error: slotsError, refetch: refetchSlots } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.reservations.slots(workspaceId, locationId, formData.serviceId, formData.date, timeZone),
    queryFn: ({ signal }) => getAvailability(locationId, formData.serviceId, formData.date, signal),
    enabled: isOpen && !!workspaceId && concreteLocation && !!selectedService && validDate && can('RESERVATIONS', 'CHECK_AVAILABILITY'),
  });

  const saveMutation = useSessionMutation({
    mutationFn: createReservation,
    onSuccess: reservation => { onSuccess(reservation); onClose(); },
    onError: error => toast.toastApiError(error),
  });
  const isSaving = saveMutation.isPending;

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isSaving || !can('RESERVATIONS', 'CREATE') || !can('RESERVATIONS', 'CHECK_AVAILABILITY')) return;
    setErrorMessage(null);

    if (!concreteLocation || !selectedService || !validDate || isFetchingSlots || slotsError
      || !slots.some(slot => slot.startTime === formData.timeSlot && slot.isAvailable && Date.parse(slot.startTime) > Date.now())) {
      setSlotReviewTime(Date.now());
      setFormData({ ...formData, timeSlot: '' });
      setErrorMessage("Selecciona una sede, un servicio y un horario vigente confirmado por la disponibilidad.");
      return;
    }

    if (!workspaceId || !formData.customerIdentifier.trim() || !formData.customerName.trim()) {
      setErrorMessage("Por favor, completa todos los datos del cliente.");
      return;
    }

    saveMutation.mutate({
      locationId,
      serviceId: formData.serviceId,
      customerName: formData.customerName,
      customerIdentifier: formData.customerIdentifier,
      dateTime: formData.timeSlot
    });
  };

  const formatTimeUI = (isoString: string) => {
    return new Intl.DateTimeFormat('es-PE', {
      hour: '2-digit', minute: '2-digit', timeZone, hour12: true
    }).format(new Date(isoString));
  };

  const currentLocation = locations.find(l => l.id === locationId);
  const availableSlots = slots.filter(slot => slot.isAvailable && Date.parse(slot.startTime) > slotReviewTime);
  const selectedSlotIsAvailable = availableSlots.some(slot => slot.startTime === formData.timeSlot);

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Nueva Reserva Manual" size="lg" closeDisabled={isSaving}>
      {errorMessage && <Alert tone="error" className="mb-4">{errorMessage}</Alert>}
        <form onSubmit={handleSubmit} className="space-y-5">

          <FormField label="Sede" required>
            {globalLocationId === 'all' ? <Select value={locationId} required onChange={event => { setLocationId(event.target.value); setFormData({ ...formData, serviceId: '', timeSlot: '' }); }}>
              <option value="" disabled>Selecciona una sede...</option>
              {locations.filter(location => location.id).map(location => <option key={location.id} value={location.id}>{location.name}</option>)}
            </Select> : <Input type="text" value={currentLocation?.name || 'Sede no disponible'} disabled />}
          </FormField>
          {formData.date < today && <Alert tone="warning">La fecha elegida pertenece al historial. Selecciona hoy o una fecha futura para crear una reserva.</Alert>}
          {!can('RESERVATIONS', 'CHECK_AVAILABILITY') && <Alert tone="warning">No tienes permiso para consultar horarios disponibles.</Alert>}
          {locations.length === 0 && <Alert tone="warning">No hay sedes disponibles para crear una reserva. Comprueba tus permisos y vuelve a intentar la carga.</Alert>}

          <div className="grid gap-4 grid-cols-1 sm:grid-cols-2">
            <FormField label="Servicio">
              <Select value={selectedService ? formData.serviceId : ''} disabled={!concreteLocation} onChange={e => setFormData({...formData, serviceId: e.target.value, timeSlot: ''})} className="w-full border focus:ring-primary text-sm" required>
                <option value="" disabled>Selecciona un servicio...</option>
                {eligibleServices.map(srv => <option key={srv.id} value={srv.id}>{srv.name} ({srv.durationInMinutes} min)</option>)}
              </Select>
            </FormField>
            <FormField required label="Fecha">
              {control => (<div className="relative">
                <CalendarIcon aria-hidden="true" className="w-4 h-4 absolute left-3 top-2.5 text-gray-400" />
                <Input {...control} type="date" min={getBusinessToday(timeZone)} value={formData.date} onChange={e => setFormData({...formData, date: e.target.value, timeSlot: ''})} className="w-full border pl-9 pr-3 focus:ring-primary text-sm" required />
              </div>)}
            </FormField>
          </div>

          <fieldset className="bg-blue-50/50 border border-blue-100 rounded-xl p-4">
            <legend className="text-sm font-medium text-blue-900 flex items-center">
              <Clock aria-hidden="true" className="w-4 h-4 mr-2" /> Horarios Disponibles
            </legend>

            {!can('RESERVATIONS', 'CHECK_AVAILABILITY') ? (
              <EmptyState className="py-6" title="La consulta de horarios requiere permiso de disponibilidad." />
            ) : !concreteLocation || !selectedService || !validDate ? (
              <EmptyState className="py-6" title="Elige sede, servicio y una fecha vigente para consultar horarios." />
            ) : isLoadingSlots || isFetchingSlots ? (
              <LoadingState className="py-6" title="Consultando horarios..." />
            ) : slotsError ? (
              <ErrorState description={getApiErrorPresentation(slotsError)} onRetry={() => void refetchSlots()} />
            ) : availableSlots.length === 0 ? (
              <EmptyState className="py-6 bg-surface rounded-lg" title="No hay turnos disponibles para esta fecha." />
            ) : (
              <div className="grid grid-cols-3 sm:grid-cols-4 gap-2 max-h-48 overflow-y-auto pr-1 custom-scrollbar">
                {availableSlots.map((slot) => (
                  <Button variant="primary"
                    key={slot.startTime}
                    type="button"
                    aria-pressed={formData.timeSlot === slot.startTime}
                    onClick={() => setFormData({ ...formData, timeSlot: slot.startTime })}
                    className={`py-2 text-sm font-medium rounded-lg border transition-all ${
                      formData.timeSlot === slot.startTime
                        ? 'bg-blue-600 text-white border-blue-600 shadow-md transform scale-[1.02]'
                        : 'bg-white text-gray-700 border-gray-200 hover:border-blue-300 hover:bg-blue-50'
                    }`}
                  >
                    {formatTimeUI(slot.startTime)}
                  </Button>
                ))}
              </div>
            )}
          </fieldset>

          <div className="grid gap-4 pt-2 border-t border-line grid-cols-1 sm:grid-cols-2">
            <FormField label="Nombre del Cliente">
              <Input type="text" value={formData.customerName} onChange={e => setFormData({...formData, customerName: e.target.value})} placeholder="Ej: Juan Pérez" className="w-full border focus:ring-primary text-sm" required />
            </FormField>
            <FormField label="Teléfono (WhatsApp)">
              <Input type="tel" pattern="^\+?[0-9]{9,15}$" title="El teléfono debe tener entre 9 y 15 números y puede incluir el código de país (Ej: +51987654321)" value={formData.customerIdentifier} onChange={e => setFormData({...formData, customerIdentifier: e.target.value})} placeholder="Ej: +51987654321" className="w-full border focus:ring-primary text-sm" required />
            </FormField>
          </div>

          <div className="pt-4 flex justify-end space-x-3">
            <Button variant="secondary" type="button" disabled={isSaving} onClick={onClose} className="text-sm font-medium transition-colors">Cancelar</Button>
            <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving || isFetchingSlots || !!slotsError || !selectedSlotIsAvailable || !validDate || !concreteLocation || !selectedService || !can('RESERVATIONS', 'CREATE') || !can('RESERVATIONS', 'CHECK_AVAILABILITY')} className="text-sm font-medium disabled:opacity-50 transition-colors">
              {isSaving ? 'Agendando...' : 'Confirmar Cita'}
            </Button>
          </div>
        </form>
    </Modal>
  );
};
