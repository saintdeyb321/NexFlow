import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { X, Clock, Calendar as CalendarIcon, Loader2 } from 'lucide-react';
import { createReservation, getAvailability } from '../services/reservation.service';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessToday } from '../../../core/utils/dateTime';
import type { LocationDto } from '../../business/types/business.types';
import type { ServiceDto } from '../../services/types/services.types';

interface CreateReservationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
  locations: LocationDto[];
  services: ServiceDto[]; 
  timeZone: string;
}

export const CreateReservationModal = ({ isOpen, onClose, onSuccess, locations, services, timeZone }: CreateReservationModalProps) => {
  const globalLocationId = useAuthStore(state => state.selectedLocationId);
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const [isSaving, setIsSaving] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  
  const [formData, setFormData] = useState({
    serviceId: '',
    customerName: '',
    customerIdentifier: '',
    date: '',
    timeSlot: '' // Ahora guardamos el ISO string exacto devuelto por la disponibilidad
  });

  useEffect(() => {
    if (isOpen) {
      setErrorMessage(null); 
      setFormData({
        serviceId: services.length > 0 ? (services[0].id || '') : '', 
        customerName: '',
        customerIdentifier: '',
        date: getBusinessToday(timeZone),
        timeSlot: ''
      });
    }
  }, [isOpen, services, timeZone]);

  // 🔥 SPRINT 04: Obtenemos disponibilidad real desde el Backend
  const { data: slots = [], isLoading: isLoadingSlots } = useQuery({
    queryKey: ['availability', workspaceId, globalLocationId, formData.serviceId, formData.date],
    queryFn: () => getAvailability(globalLocationId, formData.serviceId, formData.date),
    enabled: isOpen && !!workspaceId && !!globalLocationId && !!formData.serviceId && !!formData.date && can('RESERVATIONS', 'CHECK_AVAILABILITY'),
  });

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!can('RESERVATIONS', 'CREATE')) return;
    setErrorMessage(null);
    
    if (!formData.timeSlot) {
      setErrorMessage("Por favor, selecciona un horario disponible.");
      return;
    }

    if (!globalLocationId || !formData.serviceId || !formData.customerIdentifier || !formData.customerName) {
      setErrorMessage("Por favor, completa todos los datos del cliente.");
      return;
    }

    setIsSaving(true);
    try {
      await createReservation({
        locationId: globalLocationId,
        serviceId: formData.serviceId,
        customerName: formData.customerName,
        customerIdentifier: formData.customerIdentifier,
        dateTime: formData.timeSlot // El slot ya viene como ISO 8601 del backend
      });

      onSuccess(); 
      onClose();   
    } catch (error: unknown) {
      setErrorMessage(getApiErrorPresentation(error));
    } finally {
      setIsSaving(false);
    }
  };

  const formatTimeUI = (isoString: string) => {
    return new Intl.DateTimeFormat('es-PE', {
      hour: '2-digit', minute: '2-digit', timeZone, hour12: true
    }).format(new Date(isoString));
  };

  const currentLocation = locations.find(l => l.id === globalLocationId);

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4 overflow-y-auto">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg my-8 animate-in fade-in zoom-in-95">
        <div className="flex justify-between items-center px-6 py-4 border-b bg-gray-50 sticky top-0 rounded-t-xl z-10">
          <h3 className="text-lg font-bold text-gray-800">Nueva Reserva Manual</h3>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {errorMessage && (
          <div className="mx-6 mt-4 p-3 bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg flex items-start">
            <span>{errorMessage}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="p-6 space-y-5">
          {/* 🔥 SPRINT 04: Sede Bloqueada (Lectura) para evitar desincronización */}
          <div>
            <label className="block text-sm font-medium mb-1 text-gray-700">Sede (Seleccionada en panel)</label>
            <input type="text" value={currentLocation?.name || 'Sede desconocida'} disabled className="w-full border rounded-lg px-3 py-2 bg-gray-100 text-gray-500 text-sm cursor-not-allowed" />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium mb-1 text-gray-700">Servicio</label>
              <select value={formData.serviceId} onChange={e => setFormData({...formData, serviceId: e.target.value, timeSlot: ''})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm bg-white" required>
                <option value="" disabled>Selecciona un servicio...</option>
                {services.map(srv => <option key={srv.id} value={srv.id}>{srv.name} ({srv.durationInMinutes} min)</option>)}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium mb-1 text-gray-700">Fecha</label>
              <div className="relative">
                <CalendarIcon className="w-4 h-4 absolute left-3 top-2.5 text-gray-400" />
                <input type="date" min={getBusinessToday(timeZone)} value={formData.date} onChange={e => setFormData({...formData, date: e.target.value, timeSlot: ''})} className="w-full border rounded-lg pl-9 pr-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" required />
              </div>
            </div>
          </div>

          {/* 🔥 SPRINT 04: Grid de Disponibilidad (Availability Picker) */}
          <div className="bg-blue-50/50 border border-blue-100 rounded-xl p-4">
            <label className="block text-sm font-medium mb-3 text-blue-900 flex items-center">
              <Clock className="w-4 h-4 mr-2" /> Horarios Disponibles
            </label>
            
            {isLoadingSlots ? (
              <div className="flex justify-center items-center py-6 text-blue-600">
                <Loader2 className="w-6 h-6 animate-spin" />
              </div>
            ) : slots.length === 0 ? (
              <div className="text-center py-6 text-sm text-gray-500 bg-white rounded-lg border border-dashed border-gray-200">
                No hay turnos disponibles para esta fecha.
              </div>
            ) : (
              <div className="grid grid-cols-3 sm:grid-cols-4 gap-2 max-h-48 overflow-y-auto pr-1 custom-scrollbar">
                {slots.map((slot, idx) => (
                  <button
                    key={idx}
                    type="button"
                    onClick={() => setFormData({ ...formData, timeSlot: slot.startTime })}
                    className={`py-2 text-sm font-medium rounded-lg border transition-all ${
                      formData.timeSlot === slot.startTime 
                        ? 'bg-blue-600 text-white border-blue-600 shadow-md transform scale-[1.02]' 
                        : 'bg-white text-gray-700 border-gray-200 hover:border-blue-300 hover:bg-blue-50'
                    }`}
                  >
                    {formatTimeUI(slot.startTime)}
                  </button>
                ))}
              </div>
            )}
          </div>

          <div className="grid grid-cols-2 gap-4 pt-2 border-t border-gray-100">
            <div>
              <label className="block text-sm font-medium mb-1 text-gray-700">Nombre del Cliente</label>
              <input type="text" value={formData.customerName} onChange={e => setFormData({...formData, customerName: e.target.value})} placeholder="Ej: Juan Pérez" className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" required />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1 text-gray-700">Teléfono (WhatsApp)</label>
              <input type="tel" pattern="^\+?[0-9]{9,15}$" title="El teléfono debe tener entre 9 y 15 números y puede incluir el código de país (Ej: +51987654321)" value={formData.customerIdentifier} onChange={e => setFormData({...formData, customerIdentifier: e.target.value})} placeholder="Ej: +51987654321" className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" required />
            </div>
          </div>

          <div className="pt-4 flex justify-end space-x-3">
            <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 bg-gray-100 hover:bg-gray-200 rounded-lg text-sm font-medium transition-colors">Cancelar</button>
            <button type="submit" disabled={isSaving || !formData.timeSlot || !can('RESERVATIONS', 'CREATE')} className="px-5 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 disabled:opacity-50 transition-colors shadow-sm">
              {isSaving ? 'Agendando...' : 'Confirmar Cita'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};