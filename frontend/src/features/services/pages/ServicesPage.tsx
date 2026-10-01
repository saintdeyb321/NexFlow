import { Button, IconButton } from '../../../components/ui/Button';
import { OfferingImage } from '../../../components/ui/OfferingImage';
import { Card, PageHeader } from '../../../components/ui/Layout';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, EmptyState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/Toast';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { CategoryManager } from '../../catalog/components/CategoryManager';

import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Plus, Pencil, Trash2, Scissors, FolderPlus } from 'lucide-react';
import { getServices, saveService, deleteService } from '../services/services.service';
import { ServiceModal } from '../components/ServiceModal';
import { useAuthStore } from '../../../core/store/useAuthStore';
import type { ServiceDto } from '../types/services.types';
import { ArtifactGenerator } from '../../artifacts/components/ArtifactGenerator';

export const ServicesPage = () => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [serviceToEdit, setServiceToEdit] = useState<ServiceDto | null>(null);

  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [showCategoryPrompt, setShowCategoryPrompt] = useState(false);

  const { data: services = [], isLoading: isServicesLoading, isError, refetch } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.services.list(workspaceId, selectedLocationId),
    queryFn: ({ signal }) => getServices(selectedLocationId, signal),
    enabled: !!workspaceId && can('SERVICES', 'READ'),

  });

  const saveMutation = useSessionMutation({
    mutationFn: saveService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services.all(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, 'SERVICE') });
      setIsModalOpen(false);
      toast.success('Servicio guardado exitosamente.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
  });

  const deleteMutation = useSessionMutation({
    mutationFn: deleteService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.services.all(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, 'SERVICE') });
      setDeleteConfirmId(null);
      toast.success('Servicio eliminado.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
  });

  const handleOpenNew = () => {
    setServiceToEdit(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (service: ServiceDto) => {
    setServiceToEdit(service);
    setIsModalOpen(true);
  };

  if (isServicesLoading) {
    return <LoadingState title="Cargando servicios..." />;
  }

  if (isError) return <ErrorState onRetry={() => void refetch()} />;
  return (
    <div className="nf-page">

      {showCategoryPrompt && <CategoryManager scope="SERVICE" onClose={() => setShowCategoryPrompt(false)} />}

      <PageHeader title="Servicios" description="Presenta tus servicios y gestiona su disponibilidad por sede." icon={<Scissors aria-hidden="true" className="w-5 h-5" />} actions={
        <><Button variant="secondary" disabled={!can('SERVICES', 'READ')} onClick={() => setShowCategoryPrompt(true)}><FolderPlus aria-hidden="true" className="w-4 h-4" /> Categorías</Button>
        <Button disabled={!can('SERVICES', 'CREATE')} onClick={handleOpenNew}><Plus aria-hidden="true" className="w-4 h-4" /> Nuevo servicio</Button></>
      } />

      {can('SERVICES', 'GENERATE') && <ArtifactGenerator scope="SERVICE" title="Folleto de Servicios (PDF)" />}

      <Card className="mt-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-line pb-2">
          Lista de Servicios ({services.length})
        </h3>

        <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
          {services.length === 0 ? (
            <EmptyState className="col-span-full" title="No hay servicios disponibles en esta sede." action={can('SERVICES', 'CREATE') ? <Button onClick={handleOpenNew}>Crear servicio</Button> : undefined} />
          ) : (
            services.map((service) => (
              <article key={service.id} className="nf-panel p-4 min-w-0 flex flex-col gap-4">
                <div className="flex items-start gap-4 min-w-0">
                  <OfferingImage src={service.imageUrl} name={service.name} icon={<Scissors aria-hidden="true" className="w-6 h-6" />} className="w-20 h-20 rounded-xl shrink-0" />
                  <div className="min-w-0"><h3 className="font-semibold text-foreground break-words">{service.name}</h3>
                    <p className="text-sm text-muted mt-1">{service.durationInMinutes ?? '—'} min · {service.currency} {((service.priceMinorUnits ?? 0) / 100).toFixed(2)}</p>
                    <p className="text-xs text-muted mt-2">{service.requiresReservation ? 'Requiere reserva' : 'Sin reserva requerida'}</p>
                  </div>
                </div>
                <p className="text-sm text-muted line-clamp-2">{service.description}</p>
                <div className="flex flex-wrap items-center justify-between gap-2 border-t border-line pt-3 mt-auto">
                  <div className="space-y-2"><StatusBadge label={service.isActive ? 'Activo' : 'Inactivo'} tone={service.isActive ? 'success' : 'neutral'} />
                    <p className="text-xs text-muted">{service.locationScope === 'ALL' ? 'Todas las sedes' : `${service.locationIds?.length ?? 0} sedes específicas`}</p></div>
                  <div className="flex gap-1"><IconButton label="Editar servicio" disabled={!can('SERVICES', 'UPDATE')} onClick={() => handleOpenEdit(service)}><Pencil aria-hidden="true" className="w-4 h-4" /></IconButton>
                    <IconButton label="Eliminar servicio" disabled={!can('SERVICES', 'DELETE')} onClick={() => setDeleteConfirmId(service.id!)} className=""><Trash2 aria-hidden="true" className="w-4 h-4" /></IconButton></div>
                </div>
              </article>
            ))
          )}
        </div>
      </Card>

      <ConfirmDialog isOpen={deleteConfirmId !== null} title="Eliminar servicio" description="¿Eliminar este servicio?" destructive confirmLabel="Eliminar" isLoading={deleteMutation.isPending} confirmDisabled={!can('SERVICES', 'DELETE')} onClose={() => setDeleteConfirmId(null)} onConfirm={() => { if (deleteConfirmId && can('SERVICES', 'DELETE')) deleteMutation.mutate(deleteConfirmId); }} />
      <ServiceModal
        isOpen={isModalOpen && can('SERVICES', serviceToEdit ? 'UPDATE' : 'CREATE')}
        onClose={() => setIsModalOpen(false)}
        onSave={async (service) => { if (can('SERVICES', service.id ? 'UPDATE' : 'CREATE')) await saveMutation.mutateAsync(service); }}
        initialData={serviceToEdit}
      />
    </div>
  );
};
