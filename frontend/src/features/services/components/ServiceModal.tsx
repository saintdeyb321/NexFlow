import { Alert, ErrorState, LoadingState } from '../../../components/ui/Feedback';
import { Button } from '../../../components/ui/Button';
import { Input, Select, Textarea, FormField } from '../../../components/ui/Form';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Save, MapPin } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { getLocations } from '../../business/services/business.service';
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

export const ServiceModal = (props: ServiceModalProps) => props.isOpen
  ? <ServiceModalForm key={props.initialData?.id ?? 'new'} {...props} /> : null;

const ServiceModalForm = ({ isOpen, onClose, onSave, initialData }: ServiceModalProps) => {
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const { can } = usePermissions();
  const [isSaving, setIsSaving] = useState(false);
  const [isUploadingImage, setIsUploadingImage] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);

  const { data: categories = [], isLoading: categoriesLoading, isError: categoriesError, refetch: refetchCategories } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.catalog.categories(workspaceId, 'SERVICE'),
    queryFn: ({ signal }) => getServiceCategories(signal),
    enabled: !!workspaceId && isOpen && can('SERVICES', 'READ'),
  });

  const { data: locations = [], isLoading: locationsLoading, isError: locationsError, refetch: refetchLocations } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: !!workspaceId && isOpen && can('LOCATIONS', 'READ'),
  });

  const [formData, setFormData] = useState<Partial<ServiceDto>>(() => initialData ?? {
    name: '', description: '', durationInMinutes: 30, priceMinorUnits: 0, currency: 'PEN', requiresReservation: true, isActive: true, categoryId: categories[0]?.id || '', type: 'SERVICE', imageUrl: null,
    locationScope: 'ALL', locationIds: []
  });

  const categoryId = formData.categoryId || (!initialData ? categories[0]?.id || '' : '');

  const toggleLocation = (locId: string) => {
    const current = formData.locationIds || [];
    const updated = current.includes(locId) ? current.filter((id: string) => id !== locId) : [...current, locId];
    setFormData({ ...formData, locationIds: updated });
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setFormError(null);
    if (!formData.name || !categoryId || (formData.requiresReservation && (!formData.durationInMinutes || formData.durationInMinutes < 5))) {
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
        categoryId,
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
    } catch {
      // The save mutation owner reports API failures through Toast.
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={initialData ? 'Editar Servicio' : 'Nuevo Servicio'} closeDisabled={isSaving || isUploadingImage}>

      {formError && <Alert tone="error" className="mb-4">{formError}</Alert>}
      {categoriesError && <ErrorState title="No pudimos cargar las categorías" onRetry={() => void refetchCategories()} />}
      {categoriesLoading && <LoadingState title="Cargando categorías..." />}

      <form onSubmit={handleSubmit} className="space-y-4">
        <FormField label="Nombre del Servicio">
          <Input type="text" value={formData.name || ''} onChange={e => setFormData({ ...formData, name: e.target.value })} className="w-full border focus:ring-primary" required />
        </FormField>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <FormField label="Categoría">
            <Select value={categoryId} onChange={e => setFormData({ ...formData, categoryId: e.target.value })} className="w-full border focus:ring-primary" required>
              <option value="" disabled>Selecciona una categoría...</option>
              {categories.map((c: ServiceCategoryDto) => <option key={c.id} value={c.id}>{c.name}</option>)}
            </Select>
          </FormField>
          <div>
            <ImageUploader value={formData.imageUrl} onChange={(url) => setFormData({ ...formData, imageUrl: url })} onUploadingContext={setIsUploadingImage} label="Foto del Servicio" />
          </div>
        </div>

        <FormField label="Descripción para la IA">
          <Textarea rows={2} value={formData.description || ''} onChange={e => setFormData({ ...formData, description: e.target.value })} className="w-full border focus:ring-primary" />
        </FormField>

        <div className="grid gap-4 grid-cols-1 sm:grid-cols-2">
          <FormField label="Duración (Min)">
            <Input type="number" min="5" step="5" value={formData.durationInMinutes ?? ''} onChange={e => setFormData({ ...formData, durationInMinutes: e.target.value ? parseInt(e.target.value) : null })} className="w-full border focus:ring-primary" required={formData.requiresReservation === true} disabled={!formData.requiresReservation} />
          </FormField>
          <FormField label="Precio">
            {control => (<div className="flex">
              <span className="px-3 py-2 bg-gray-100 border border-r-0 border-line rounded-l-xl text-muted">{formData.currency || 'PEN'}</span>
              <Input {...control} type="number" min="0" step="0.10" value={(formData.priceMinorUnits || 0) / 100} onChange={e => setFormData({ ...formData, priceMinorUnits: Math.round(parseFloat(e.target.value || '0') * 100) })} className="w-full border rounded-r-xl focus:ring-primary" />
            </div>)}
          </FormField>
        </div>

        <fieldset className="p-4 bg-surface-soft rounded-xl border border-line">
          <legend className="text-sm font-medium text-gray-700 flex items-center">
            <MapPin aria-hidden="true" className="w-4 h-4 mr-2 text-muted" /> Disponibilidad en Sedes
          </legend>
          <div className="flex gap-4 mb-3">
            <label className="flex items-center text-sm cursor-pointer">
              <Input type="radio" name="locScope" checked={formData.locationScope === 'ALL'} onChange={() => setFormData({ ...formData, locationScope: 'ALL' })} className="mr-2 focus:ring-primary" />
              Todas las Sedes
            </label>
            <label className="flex items-center text-sm cursor-pointer">
              <Input type="radio" name="locScope" checked={formData.locationScope === 'SPECIFIC'} onChange={() => setFormData({ ...formData, locationScope: 'SPECIFIC' })} className="mr-2 focus:ring-primary" />
              Sedes Específicas
            </label>
          </div>

          {formData.locationScope === 'SPECIFIC' && (
            <div className="grid gap-2 mt-2 pt-2 border-t border-line grid-cols-1 sm:grid-cols-2">
              {locationsError ? <ErrorState title="No pudimos cargar las sedes" onRetry={() => void refetchLocations()} />
                : locationsLoading ? <LoadingState title="Cargando sedes..." />
                : !can('LOCATIONS', 'READ') ? <p className="text-sm text-muted">No tienes permiso para consultar las sedes.</p>
                : locations.map(loc => (
                <label key={loc.id} className="flex items-center text-sm cursor-pointer">
                  <Input type="checkbox" checked={(formData.locationIds || []).includes(loc.id!)} onChange={() => toggleLocation(loc.id!)} className="mr-2 focus:ring-primary" />
                  {loc.name}
                </label>
              ))}
            </div>
          )}
        </fieldset>

        <div className="flex items-center justify-between pt-2">
          <label className="flex items-center text-sm cursor-pointer text-gray-700">
            <Input type="checkbox" checked={formData.requiresReservation || false} onChange={e => setFormData({ ...formData, requiresReservation: e.target.checked })} className="mr-2 focus:ring-primary" /> Requiere Cita
          </label>
          <label className="flex items-center text-sm cursor-pointer text-gray-700">
            <Input type="checkbox" checked={formData.isActive ?? true} onChange={e => setFormData({ ...formData, isActive: e.target.checked })} className="mr-2 focus:ring-green-500" /> Activo
          </label>
        </div>

        <div className="pt-6 border-t border-line flex justify-end gap-3">
          <Button variant="secondary" type="button" disabled={isSaving || isUploadingImage} onClick={onClose} className="text-sm font-medium">Cancelar</Button>
          <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving || isUploadingImage || categoriesLoading || categoriesError || categories.length === 0} className="flex items-center text-sm font-medium disabled:opacity-50 transition-colors">
            <Save aria-hidden="true" className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
