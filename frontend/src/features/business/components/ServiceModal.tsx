import { useState, useEffect } from 'react';
import { Save } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import type { ServiceDto } from '../types/business.types';

interface ServiceModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (service: ServiceDto) => Promise<void>;
  initialData?: ServiceDto | null;
}

export const ServiceModal = ({ isOpen, onClose, onSave, initialData }: ServiceModalProps) => {
  const [isSaving, setIsSaving] = useState(false);
  const [formData, setFormData] = useState<Partial<ServiceDto>>({
    name: '',
    description: '',
    durationInMinutes: 30,
    price: 0,
    currency: 'PEN',
    requiresReservation: true,
    isActive: true,
  });

  // Si nos pasan datos iniciales (Editar), los cargamos. Si no, limpiamos (Nuevo).
  useEffect(() => {
    if (initialData) {
      setFormData(initialData);
    } else {
      setFormData({
        name: '', description: '', durationInMinutes: 30, price: 0, currency: 'PEN', requiresReservation: true, isActive: true
      });
    }
  }, [initialData, isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name || !formData.durationInMinutes) return;

    setIsSaving(true);
    try {
      const serviceToSave: ServiceDto = {
        id: formData.id || crypto.randomUUID(),
        name: formData.name,
        description: formData.description,
        durationInMinutes: formData.durationInMinutes,
        price: formData.price,
        currency: formData.currency,
        requiresReservation: formData.requiresReservation ?? true,
        isActive: formData.isActive ?? true
      };
      
      await onSave(serviceToSave);
      onClose();
    } catch (error) {
      console.error(error);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal 
      isOpen={isOpen} 
      onClose={onClose} 
      title={initialData ? 'Editar Servicio' : 'Nuevo Servicio'}
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Nombre del Servicio *</label>
          <input
            type="text"
            value={formData.name}
            onChange={e => setFormData({ ...formData, name: e.target.value })}
            className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
            required
          />
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Descripción para la IA</label>
          <textarea
            rows={2}
            value={formData.description || ''}
            onChange={e => setFormData({ ...formData, description: e.target.value })}
            className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
            placeholder="Breve detalle de lo que incluye el servicio..."
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Duración (Minutos) *</label>
            <input
              type="number" min="5" step="5"
              value={formData.durationInMinutes}
              onChange={e => setFormData({ ...formData, durationInMinutes: parseInt(e.target.value) })}
              className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
              required
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Precio Referencial</label>
            <div className="flex">
              <span className="inline-flex items-center px-3 text-sm text-gray-500 bg-gray-100 border border-r-0 border-gray-200 rounded-l-xl">
                S/
              </span>
              <input
                type="number" min="0" step="0.10"
                value={formData.price}
                onChange={e => setFormData({ ...formData, price: parseFloat(e.target.value) })}
                className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-r-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
              />
            </div>
          </div>
        </div>

        <div className="flex items-center justify-between pt-2">
          <label className="flex items-center text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={formData.requiresReservation}
              onChange={e => setFormData({ ...formData, requiresReservation: e.target.checked })}
              className="w-4 h-4 text-blue-600 rounded border-gray-300 focus:ring-blue-500 mr-2"
            />
            ¿Requiere Cita Previa?
          </label>
          <label className="flex items-center text-sm text-gray-700 cursor-pointer">
            <input
              type="checkbox"
              checked={formData.isActive}
              onChange={e => setFormData({ ...formData, isActive: e.target.checked })}
              className="w-4 h-4 text-green-600 rounded border-gray-300 focus:ring-green-500 mr-2"
            />
            Servicio Activo
          </label>
        </div>

        <div className="pt-6 border-t border-gray-100 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-5 py-2.5 text-sm font-medium text-gray-600 hover:bg-gray-100 rounded-xl transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={isSaving} className="flex items-center px-5 py-2.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-xl transition-colors disabled:opacity-50">
            <Save className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </button>
        </div>
      </form>
    </Modal>
  );
};