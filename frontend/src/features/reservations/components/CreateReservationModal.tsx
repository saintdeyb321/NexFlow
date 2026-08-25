import { useState } from 'react';
import { X } from 'lucide-react';
import { createReservation } from '../services/reservation.service';
import type { LocationDto } from '../../business/types/business.types';
import type { ServiceDto } from '../../business/types/business.types';

interface CreateReservationModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSuccess: () => void;
  locations: LocationDto[];
  services: ServiceDto[];
}

export const CreateReservationModal = ({ isOpen, onClose, onSuccess, locations, services }: CreateReservationModalProps) => {
  const [isSaving, setIsSaving] = useState(false);
  const [formData, setFormData] = useState({
    locationId: locations.length > 0 ? (locations.find(l => l.isMain)?.id || locations[0].id) : '',
    serviceId: services.length > 0 ? services[0].id : '',
    customerName: '', // 🔥 NUEVO: Obligatorio para el backend
    customerIdentifier: '',
    date: new Date().toISOString().split('T')[0],
    time: '10:00'
  });

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.locationId || !formData.serviceId || !formData.customerIdentifier || !formData.customerName) {
      alert("Por favor, completa todos los campos.");
      return;
    }

    setIsSaving(true);
    try {
      const exactDateTime = new Date(`${formData.date}T${formData.time}:00`).toISOString();

      await createReservation({
        locationId: formData.locationId,
        serviceId: formData.serviceId,
        customerName: formData.customerName, // 🔥 CORRECCIÓN: Añadido al payload
        customerIdentifier: formData.customerIdentifier,
        dateTime: exactDateTime
      });

      onSuccess(); 
      onClose();   
    } catch (error: any) {
      alert(`Error al crear la reserva: ${error.response?.data?.error || 'Conflicto de horario'}`);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50 p-4">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-md overflow-hidden">
        <div className="flex justify-between items-center px-6 py-4 border-b bg-gray-50">
          <h3 className="text-lg font-bold text-gray-800">Nueva Reserva Manual</h3>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="w-5 h-5" />
          </button>
        </div>

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Sede</label>
            <select 
              value={formData.locationId} 
              onChange={e => setFormData({...formData, locationId: e.target.value})}
              className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
            >
              {locations.map(loc => <option key={loc.id} value={loc.id}>{loc.name}</option>)}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Servicio</label>
            <select 
              value={formData.serviceId} 
              onChange={e => setFormData({...formData, serviceId: e.target.value})}
              className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
            >
              {services.map(srv => <option key={srv.id} value={srv.id}>{srv.name} ({srv.durationInMinutes} min)</option>)}
            </select>
          </div>

          {/* 🔥 CORRECCIÓN: Separamos el Nombre del Teléfono */}
          <div>
            <label className="block text-sm font-medium mb-1">Nombre del Cliente</label>
            <input 
              type="text" 
              value={formData.customerName} 
              onChange={e => setFormData({...formData, customerName: e.target.value})}
              placeholder="Ej: Juan Pérez"
              className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
              required
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Teléfono (WhatsApp)</label>
            <input 
              type="text" 
              value={formData.customerIdentifier} 
              onChange={e => setFormData({...formData, customerIdentifier: e.target.value})}
              placeholder="Ej: +51987654321"
              className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
              required
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium mb-1">Fecha</label>
              <input 
                type="date" 
                value={formData.date} 
                onChange={e => setFormData({...formData, date: e.target.value})}
                className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
                required
              />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Hora</label>
              <input 
                type="time" 
                value={formData.time} 
                onChange={e => setFormData({...formData, time: e.target.value})}
                className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm"
                required
              />
            </div>
          </div>

          <div className="pt-4 flex justify-end space-x-3">
            <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium">
              Cancelar
            </button>
            <button type="submit" disabled={isSaving} className="px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 disabled:opacity-50">
              {isSaving ? 'Guardando...' : 'Confirmar Cita'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};