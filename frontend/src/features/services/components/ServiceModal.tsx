import { queryPolicies } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Save, MapPin, AlertCircle } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { getLocations } from '../../business/services/business.service'; // 🔥 Importación correcta
import { useAuthStore } from '../../../core/store/useAuthStore';
import type { ServiceDto, ServiceCategoryDto } from '../types/services.types'; 
import { getServiceCategories } from '../services/services.service'; 
import { ImageUploader } from '../../../components/ui/ImageUploader';
import { usePermissions } from '../../../core/auth/permissions';

interface ServiceModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (service: ServiceDto) => Promise<void>;
  initialData?: ServiceDto | null;
}

export const ServiceModal = ({ isOpen, onClose, onSave, initialData }: ServiceModalProps) => {
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const { can } = usePermissions();
  const [isSaving, setIsSaving] = useState(false);
  const [isUploadingImage, setIsUploadingImage] = useState(false);
  
  // 🔥 SPRINT 09: Estado visual para errores en lugar de alert()
  const [formError, setFormError] = useState<string | null>(null);
  
  const { data: categories = [] } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.categories(workspaceId, 'SERVICE'),
    queryFn: ({ signal }) => getServiceCategories(signal),
    enabled: !!workspaceId && isOpen && can('SERVICES', 'READ'),
  });

  const { data: locations = [] } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: !!workspaceId && isOpen && can('LOCATIONS', 'READ'),
  });

  const [formData, setFormData] = useState<Partial<ServiceDto>>({
    name: '', description: '', durationInMinutes: 30, priceMinorUnits: 0, currency: 'PEN', requiresReservation: true, isActive: true, categoryId: '', type: 'SERVICE', imageUrl: null,
    locationScope: 'ALL', locationIds: []
  });

  useEffect(() => {
    setFormError(null);
    if (isOpen) {
      if (initialData) {
        setFormData(initialData);
      } else {
        setFormData({ 
          name: '', description: '', durationInMinutes: 30, priceMinorUnits: 0, currency: 'PEN', requiresReservation: true, isActive: true, categoryId: categories[0]?.id || '', type: 'SERVICE', imageUrl: null,
          locationScope: 'ALL', locationIds: [] 
        });
      }
    }
  }, [isOpen, initialData, categories]);

  const toggleLocation = (locId: string) => {
    const current = formData.locationIds || [];
    const updated = current.includes(locId) ? current.filter((id: string) => id !== locId) : [...current, locId];
    setFormData({ ...formData, locationIds: updated });
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);

    // 🔥 SPRINT 09: Validaciones en UI limpias
    if (!formData.name || !formData.durationInMinutes || !formData.categoryId) {
      setFormError("Completa el nombre, duración y categoría.");
      return;
    }
    
    if (formData.locationScope === 'SPECIFIC' && (!formData.locationIds || formData.locationIds.length === 0)) {
      setFormError("Si eliges 'Sedes Específicas', debes marcar al menos una sede.");
      return;
    }

    setIsSaving(true);
    try {
      const serviceToSave: ServiceDto = {
        ...formData,
        ...(formData.id ? { id: formData.id } : {}), // Dejamos que el Backend genere el ID
        type: 'SERVICE',
        categoryId: formData.categoryId,
        name: formData.name!,
        priceMinorUnits: formData.priceMinorUnits || 0,
        currency: formData.currency || 'PEN',
        isActive: formData.isActive ?? true,
        durationInMinutes: formData.durationInMinutes,
        requiresReservation: formData.requiresReservation ?? true,
        locationScope: formData.locationScope || 'ALL',
        locationIds: formData.locationScope === 'ALL' ? [] : (formData.locationIds || [])
      } as ServiceDto;
      
      await onSave(serviceToSave);
      onClose();
    } catch (error: unknown) {
      setFormError(getApiErrorPresentation(error));
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={initialData ? 'Editar Servicio' : 'Nuevo Servicio'}>
      
      {formError && (
        <div className="mb-4 text-xs font-medium text-red-600 flex items-center bg-red-50 p-3 rounded-lg border border-red-100">
          <AlertCircle className="w-4 h-4 mr-2 shrink-0" /> {formError}
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Nombre del Servicio *</label>
          <input type="text" value={formData.name || ''} onChange={e => setFormData({ ...formData, name: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-blue-500" required />
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Categoría *</label>
            <select value={formData.categoryId || ''} onChange={e => setFormData({ ...formData, categoryId: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-blue-500" required>
              <option value="" disabled>Selecciona una categoría...</option>
              {categories.map((c: ServiceCategoryDto) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
          </div>
          <div>
            <ImageUploader value={formData.imageUrl} onChange={(url) => setFormData({ ...formData, imageUrl: url })} onUploadingContext={setIsUploadingImage} label="Foto del Servicio" />
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Descripción para la IA</label>
          <textarea rows={2} value={formData.description || ''} onChange={e => setFormData({ ...formData, description: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-blue-500" />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Duración (Min) *</label>
            <input type="number" min="5" step="5" value={formData.durationInMinutes || 30} onChange={e => setFormData({ ...formData, durationInMinutes: parseInt(e.target.value) })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:ring-2 focus:ring-blue-500" required />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Precio</label>
            <div className="flex">
              <span className="px-3 py-2 bg-gray-100 border border-r-0 border-gray-200 rounded-l-xl text-gray-500">S/</span>
              <input type="number" min="0" step="0.10" value={(formData.priceMinorUnits || 0) / 100} onChange={e => setFormData({ ...formData, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100) })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-r-xl outline-none focus:ring-2 focus:ring-blue-500" />
            </div>
          </div>
        </div>

        <div className="p-4 bg-gray-50 rounded-xl border border-gray-200">
          <label className="block text-sm font-medium text-gray-700 mb-2 flex items-center">
            <MapPin className="w-4 h-4 mr-2 text-gray-500" /> Disponibilidad en Sedes
          </label>
          <div className="flex gap-4 mb-3">
            <label className="flex items-center text-sm cursor-pointer">
              <input type="radio" name="locScope" checked={formData.locationScope === 'ALL'} onChange={() => setFormData({ ...formData, locationScope: 'ALL' })} className="mr-2 text-blue-600 focus:ring-blue-500" />
              Todas las Sedes
            </label>
            <label className="flex items-center text-sm cursor-pointer">
              <input type="radio" name="locScope" checked={formData.locationScope === 'SPECIFIC'} onChange={() => setFormData({ ...formData, locationScope: 'SPECIFIC' })} className="mr-2 text-blue-600 focus:ring-blue-500" />
              Sedes Específicas
            </label>
          </div>
          
          {formData.locationScope === 'SPECIFIC' && (
            <div className="grid grid-cols-2 gap-2 mt-2 pt-2 border-t border-gray-200">
              {locations.map(loc => (
                <label key={loc.id} className="flex items-center text-sm cursor-pointer">
                  <input type="checkbox" checked={(formData.locationIds || []).includes(loc.id!)} onChange={() => toggleLocation(loc.id!)} className="mr-2 rounded text-blue-600 focus:ring-blue-500" />
                  {loc.name}
                </label>
              ))}
            </div>
          )}
        </div>

        <div className="flex items-center justify-between pt-2">
          <label className="flex items-center text-sm cursor-pointer text-gray-700">
            <input type="checkbox" checked={formData.requiresReservation || false} onChange={e => setFormData({ ...formData, requiresReservation: e.target.checked })} className="mr-2 rounded text-blue-600 focus:ring-blue-500" /> Requiere Cita
          </label>
          <label className="flex items-center text-sm cursor-pointer text-gray-700">
            <input type="checkbox" checked={formData.isActive ?? true} onChange={e => setFormData({ ...formData, isActive: e.target.checked })} className="mr-2 rounded text-green-600 focus:ring-green-500" /> Activo
          </label>
        </div>

        <div className="pt-6 border-t border-gray-100 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-5 py-2.5 text-sm font-medium text-gray-600 hover:bg-gray-100 rounded-xl">Cancelar</button>
          <button type="submit" disabled={isSaving || isUploadingImage || categories.length === 0} className="flex items-center px-5 py-2.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-xl disabled:opacity-50 transition-colors">
            <Save className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </button>
        </div>
      </form>
    </Modal>
  );
};
