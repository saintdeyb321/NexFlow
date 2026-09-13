import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Save } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { axiosClient } from '../../../core/api/axiosClient';
import { useAuthStore } from '../../../core/store/useAuthStore';
import type { CatalogItemDto, CatalogCategoryDto } from '../../catalog/types/catalog.types'; 
import { ImageUploader } from '../../../components/ui/ImageUploader';

interface ServiceModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (service: CatalogItemDto) => Promise<void>;
  initialData?: CatalogItemDto | null;
}

export const ServiceModal = ({ isOpen, onClose, onSave, initialData }: ServiceModalProps) => {
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const [isSaving, setIsSaving] = useState(false);
  const [isUploadingImage, setIsUploadingImage] = useState(false);
  
  const fetchServiceCategories = async (): Promise<CatalogCategoryDto[]> => {
    const { data } = await axiosClient.get<CatalogCategoryDto[]>('/catalog/categories?scope=SERVICE');
    return data;
  };

  const { data: categories = [] } = useQuery({
    queryKey: ['serviceCategories', workspaceId],
    queryFn: fetchServiceCategories,
    enabled: !!workspaceId && isOpen,
  });

  const [formData, setFormData] = useState<Partial<CatalogItemDto>>({
    name: '', description: '', durationInMinutes: 30, priceMinorUnits: 0, currency: 'PEN', requiresReservation: true, isActive: true, categoryId: '', type: 'SERVICE', imageUrl: null
  });

  // 🔥 CORRECCIÓN DEL LOOP INFINITO: 
  // Solo actualizamos el estado cuando el modal se ABRE (isOpen cambia a true)
  useEffect(() => {
    if (isOpen) {
      if (initialData) {
        setFormData(initialData);
      } else {
        setFormData({ name: '', description: '', durationInMinutes: 30, priceMinorUnits: 0, currency: 'PEN', requiresReservation: true, isActive: true, categoryId: categories[0]?.id || '', type: 'SERVICE', imageUrl: null });
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, initialData]); // 🛑 JAMÁS pongas 'categories' aquí, eso causa el loop infinito.

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.name || !formData.durationInMinutes || !formData.categoryId) return alert("Completa todos los campos obligatorios.");

    setIsSaving(true);
    try {
      const serviceToSave: CatalogItemDto = {
        ...formData,
        id: formData.id || crypto.randomUUID(),
        type: 'SERVICE',
        categoryId: formData.categoryId,
        name: formData.name!,
        priceMinorUnits: formData.priceMinorUnits || 0,
        currency: formData.currency || 'PEN',
        isActive: formData.isActive ?? true,
        durationInMinutes: formData.durationInMinutes,
        requiresReservation: formData.requiresReservation ?? true,
      } as CatalogItemDto;
      
      await onSave(serviceToSave);
      onClose();
    } catch (error) {
      console.error(error);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={initialData ? 'Editar Servicio' : 'Nuevo Servicio'}>
      <form onSubmit={handleSubmit} className="space-y-4">
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Nombre del Servicio *</label>
          <input type="text" value={formData.name || ''} onChange={e => setFormData({ ...formData, name: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl" required />
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Categoría *</label>
            <select value={formData.categoryId || ''} onChange={e => setFormData({ ...formData, categoryId: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl" required>
              <option value="" disabled>Selecciona una categoría...</option>
              {categories.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
            </select>
            {categories.length === 0 && <p className="text-xs text-orange-500 mt-1">⚠️ Crea una categoría primero.</p>}
          </div>
          
          <div>
            <ImageUploader 
              value={formData.imageUrl} 
              onChange={(url) => setFormData({ ...formData, imageUrl: url })} 
              onUploadingContext={setIsUploadingImage}
              label="Foto del Servicio"
            />
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Descripción para la IA</label>
          <textarea rows={2} value={formData.description || ''} onChange={e => setFormData({ ...formData, description: e.target.value })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl" />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Duración (Min) *</label>
            <input type="number" min="5" step="5" value={formData.durationInMinutes || 30} onChange={e => setFormData({ ...formData, durationInMinutes: parseInt(e.target.value) })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl" required />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Precio</label>
            <div className="flex">
              <span className="px-3 py-2 bg-gray-100 border border-r-0 border-gray-200 rounded-l-xl text-gray-500">S/</span>
              <input type="number" min="0" step="0.10" value={(formData.priceMinorUnits || 0) / 100} onChange={e => setFormData({ ...formData, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100) })} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-r-xl" />
            </div>
          </div>
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
          <button type="submit" disabled={isSaving || isUploadingImage || categories.length === 0} className="flex items-center px-5 py-2.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-xl disabled:opacity-50">
            <Save className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </button>
        </div>
      </form>
    </Modal>
  );
};