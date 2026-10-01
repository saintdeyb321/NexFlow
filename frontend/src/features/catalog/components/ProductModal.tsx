import { Button } from '../../../components/ui/Button';
import { Input, Select, Textarea, FormField } from '../../../components/ui/Form';
import { Modal } from '../../../components/ui/Modal';
import { useToast } from '../../../components/ui/Toast';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getLocations } from '../../business/services/business.service';
import { ImageUploader } from '../../../components/ui/ImageUploader';
import type { ProductDto, ProductCategoryDto } from '../types/catalog.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';

interface ProductModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (product: ProductDto) => void;
  isSaving: boolean;
  categories: ProductCategoryDto[];
  productToEdit?: ProductDto | null;
}

export const ProductModal = ({ isOpen, onClose, onSave, isSaving, categories, productToEdit }: ProductModalProps) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  const toast = useToast();
  const [isUploadingImage, setIsUploadingImage] = useState(false);

  const { data: locations = [] } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: !!workspaceId && isOpen && can('LOCATIONS', 'READ')
  });

  const [formData, setFormData] = useState<Partial<ProductDto>>({
    name: '',
    description: '',
    categoryId: '',
    priceMinorUnits: 0,
    currency: 'PEN',
    isActive: true,
    type: 'PRODUCT',
    locationScope: 'ALL',
    locationIds: []
  });
  useEffect(() => {
    if (isOpen) {
      if (productToEdit) {
        setFormData(productToEdit);
      } else {
        setFormData({
          name: '', description: '', categoryId: categories[0]?.id || '',
          priceMinorUnits: 0, currency: 'PEN', isActive: true,
          type: 'PRODUCT', locationScope: 'ALL', locationIds: []
        });
      }
    }
  }, [isOpen, productToEdit, categories]);

  if (!isOpen) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.categoryId) {
      toast.warning("Debes seleccionar una categoría");
      return;
    }

    const payload: ProductDto = {
      ...(formData as ProductDto),
      locationScope: formData.locationScope || 'ALL',
      locationIds: formData.locationScope === 'ALL' ? [] : (formData.locationIds || [])
    };
    onSave(payload);
  };

  const toggleLocation = (locId: string) => {
    const current = formData.locationIds || [];
    if (current.includes(locId)) {
      setFormData({ ...formData, locationIds: current.filter(id => id !== locId) });
    } else {
      setFormData({ ...formData, locationIds: [...current, locId] });
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={productToEdit ? 'Editar Producto' : 'Nuevo Producto'} size="lg" closeDisabled={isSaving || isUploadingImage}>
        <form onSubmit={handleSubmit} className="space-y-5">
          {/* Nombre */}
          <FormField label="Nombre del producto">
            <Input type="text" value={formData.name || ''} onChange={e => setFormData({...formData, name: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none" required placeholder="Ej. Lentes de contacto" />
          </FormField>

          <div className="grid grid-cols-2 gap-4">
            {/* Categoría */}
            <FormField label="Categoría">
              <Select value={formData.categoryId || ''} onChange={e => setFormData({...formData, categoryId: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none bg-white" required>
                <option value="" disabled>Selecciona...</option>
                {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </Select>
            </FormField>
            {/* Precio */}
            <FormField required label="Precio">
              {control => (<div className="flex items-center">
                <span className="mr-2 text-gray-500 font-medium">{formData.currency}</span>
                <Input {...control} type="number" step="0.10" min="0" value={(formData.priceMinorUnits || 0) / 100} onChange={e => setFormData({...formData, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100)})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none" required />
              </div>)}
            </FormField>
          </div>

          <div className="grid grid-cols-2 gap-4">
            {/* Moneda */}
            <FormField label="Moneda">
              <Select value={formData.currency || 'PEN'} onChange={e => setFormData({...formData, currency: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 outline-none bg-white">
                <option value="PEN">Soles (PEN)</option>
                <option value="USD">Dólares (USD)</option>
              </Select>
            </FormField>
            {/* Estado Activo */}
            <fieldset>
              <legend className="block text-sm font-medium mb-1 text-gray-700">Disponibilidad</legend>
              <label className="flex items-center mt-2 cursor-pointer">
                <Input type="checkbox" checked={formData.isActive || false} onChange={e => setFormData({...formData, isActive: e.target.checked})} className="w-5 h-5 text-blue-600 rounded focus:ring-blue-500 border-gray-300" />
                <span className={`ml-2 text-sm font-medium ${formData.isActive ? 'text-green-600' : 'text-gray-500'}`}>
                  {formData.isActive ? 'Producto Activo' : 'Oculto / Agotado'}
                </span>
              </label>
            </fieldset>
          </div>

          <ImageUploader onUploadingContext={setIsUploadingImage}
            value={formData.imageUrl}
            onChange={(url) => setFormData({ ...formData, imageUrl: url })}
            label="Foto del Producto"
          />

          <FormField label="Descripción detallada">
            <Textarea rows={3} value={formData.description || ''} onChange={e => setFormData({...formData, description: e.target.value})} className="w-full border border-gray-300 rounded-lg px-3 py-2 focus:ring-2 focus:ring-blue-500 outline-none resize-none" placeholder="Características principales del producto..." />
          </FormField>

          <div className="bg-gray-50 p-4 rounded-lg border border-gray-200">
            <h4 className="font-medium text-sm text-gray-900 mb-3">Disponible en:</h4>
            <div className="space-y-3">
              <label className="flex items-center">
                <Input type="radio" checked={formData.locationScope === 'ALL'} onChange={() => setFormData({...formData, locationScope: 'ALL', locationIds: []})} className="w-4 h-4 text-blue-600" />
                <span className="ml-2 text-sm text-gray-700">Todas las sedes</span>
              </label>
              <label className="flex items-center">
                <Input type="radio" checked={formData.locationScope === 'SPECIFIC'} onChange={() => setFormData({...formData, locationScope: 'SPECIFIC'})} className="w-4 h-4 text-blue-600" />
                <span className="ml-2 text-sm text-gray-700">Solo en sedes específicas</span>
              </label>

              {formData.locationScope === 'SPECIFIC' && (
                <div className="ml-6 mt-2 grid grid-cols-2 gap-2">
                  {locations.map(loc => (
                    <label key={loc.id} className="flex items-center">
                      <Input type="checkbox" checked={(formData.locationIds || []).includes(loc.id!)} onChange={() => toggleLocation(loc.id!)} className="w-4 h-4 text-blue-600 rounded border-gray-300" />
                      <span className="ml-2 text-sm text-gray-600">{loc.name}</span>
                    </label>
                  ))}
                  {locations.length === 0 && <span className="text-xs text-red-500">No hay sedes registradas.</span>}
                </div>
              )}
            </div>
          </div>

          <div className="flex justify-end gap-3 pt-4 border-t border-gray-100">
            <Button variant="secondary" type="button" disabled={isSaving || isUploadingImage} onClick={onClose} className="px-4 py-2 text-gray-600 bg-gray-100 hover:bg-gray-200 font-medium rounded-lg">Cancelar</Button>
            <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving || isUploadingImage || categories.length === 0} className="px-5 py-2 bg-blue-600 text-white font-medium hover:bg-blue-700 rounded-lg disabled:opacity-50 shadow-sm">
              {isSaving ? 'Guardando...' : 'Guardar Producto'}
            </Button>
          </div>
        </form>
    </Modal>
  );
};
