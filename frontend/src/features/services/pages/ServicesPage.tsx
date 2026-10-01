import { Button, IconButton } from '../../../components/ui/Button';
import { Card } from '../../../components/ui/Layout';
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
import { Plus, Pencil, Tag, Trash2, Scissors, FolderPlus } from 'lucide-react';
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
    <div className="max-w-5xl mx-auto animate-in fade-in">

      {showCategoryPrompt && <CategoryManager scope="SERVICE" onClose={() => setShowCategoryPrompt(false)} />}

      <div className="flex flex-col md:flex-row justify-between items-start md:items-center mb-8 gap-4">
        <div className="flex items-center">
          <div className="w-10 h-10 bg-purple-100 rounded-lg flex items-center justify-center mr-4">
             <Scissors className="w-5 h-5 text-purple-600" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Servicios</h1>
            <p className="text-sm text-gray-500 mt-1">Configura las prestaciones y su duración para las reservas.</p>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <Button variant="secondary"
            disabled={!can('SERVICES', 'READ')} onClick={() => setShowCategoryPrompt(true)}
            className="flex items-center px-4 py-2.5 bg-gray-100 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-200 transition-colors"
          >
            <FolderPlus className="w-4 h-4 mr-2" />
            Categoría
          </Button>

          <Button variant="primary"
            disabled={!can('SERVICES', 'CREATE')} onClick={handleOpenNew}
            className="flex items-center px-5 py-2.5 bg-purple-600 text-white text-sm font-medium rounded-lg hover:bg-purple-700 transition-colors shadow-sm"
          >
            <Plus className="w-4 h-4 mr-2" />
            Nuevo Servicio
          </Button>
        </div>
      </div>

      {can('SERVICES', 'GENERATE') && <ArtifactGenerator scope="SERVICE" title="Folleto de Servicios (PDF)" />}

      <Card className="mt-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-gray-100 pb-2">
          Lista de Servicios ({services.length})
        </h3>

        <div className="space-y-3">
          {services.length === 0 ? (
            <EmptyState title="No hay servicios disponibles en esta sede." />
          ) : (
            services.map((service) => (
              <div key={service.id} className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-xl hover:border-blue-200 hover:shadow-sm transition-all">
                <div className="flex items-center">
                  <div className="w-12 h-12 bg-blue-50 rounded-lg flex items-center justify-center mr-4">
                    <Tag className="w-5 h-5 text-blue-500" />
                  </div>
                  <div>
                    <h4 className="font-bold text-gray-900 text-sm md:text-base">{service.name}</h4>
                    <div className="flex items-center mt-1">
                      <StatusBadge label={service.isActive ? '● ACTIVO' : 'INACTIVO'} tone={service.isActive ? 'success' : 'neutral'} />
                      <span className="ml-3 text-xs text-gray-500 border-l border-gray-200 pl-3">
                        {service.durationInMinutes} min • {service.currency} {service.priceMinorUnits ? (service.priceMinorUnits / 100).toFixed(2) : '0.00'}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2 relative">
                  <IconButton variant="ghost" label="Editar" disabled={!can('SERVICES', 'UPDATE')} onClick={() => handleOpenEdit(service)} className="p-2.5 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors" title="Editar">
                    <Pencil className="w-4 h-4" />
                  </IconButton>
                  <IconButton variant="ghost" label="Eliminar" disabled={!can('SERVICES', 'DELETE')} onClick={() => setDeleteConfirmId(service.id!)} className="p-2.5 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors" title="Eliminar">
                    <Trash2 className="w-4 h-4" />
                  </IconButton>

                </div>
              </div>
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
