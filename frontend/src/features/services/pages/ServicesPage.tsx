import { CategoryManager } from '../../catalog/components/CategoryManager';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Pencil, Tag, Trash2, Scissors, FolderPlus } from 'lucide-react';
import { getServices, saveService, deleteService } from '../services/services.service';
import { ServiceModal } from '../components/ServiceModal';
import { useAuthStore } from '../../../core/store/useAuthStore';
import type { ServiceDto } from '../types/services.types';
import { ArtifactGenerator } from '../../artifacts/components/ArtifactGenerator';

export const ServicesPage = () => {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const selectedLocationId = useAuthStore((state) => state.selectedLocationId);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [serviceToEdit, setServiceToEdit] = useState<ServiceDto | null>(null);

  // 🔥 Estados para reemplazar los alerts y prompts nativos
  const [notification, setNotification] = useState<{ msg: string, type: 'success' | 'error' } | null>(null);
  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const [showCategoryPrompt, setShowCategoryPrompt] = useState(false);

  const { data: services = [], isLoading: isServicesLoading } = useQuery({
    queryKey: ['services', workspaceId, selectedLocationId],
    queryFn: () => getServices(selectedLocationId),
    enabled: !!workspaceId && can('SERVICES', 'READ'),
    staleTime: 1000 * 60 * 10,
  });

  const saveMutation = useMutation({
    mutationFn: saveService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['services', workspaceId] });
      setIsModalOpen(false);
      setNotification({ msg: 'Servicio guardado exitosamente.', type: 'success' });
    },
    onError: (error: unknown) => setNotification({ msg: `Error al guardar: ${getApiErrorPresentation(error)}`, type: 'error' })
  });

  const deleteMutation = useMutation({
    mutationFn: deleteService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['services', workspaceId] });
      setDeleteConfirmId(null);
      setNotification({ msg: 'Servicio eliminado.', type: 'success' });
    },
    onError: (error: unknown) => setNotification({ msg: `Error al eliminar: ${getApiErrorPresentation(error)}`, type: 'error' })
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
    return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando servicios...</div>;
  }

  // 🔥 Estructura JSX corregida sin tags duplicados
  return (
    <div className="max-w-5xl mx-auto animate-in fade-in">
      
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

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
          <button 
            disabled={!can('SERVICES', 'READ')} onClick={() => setShowCategoryPrompt(true)}
            className="flex items-center px-4 py-2.5 bg-gray-100 text-gray-700 text-sm font-medium rounded-lg hover:bg-gray-200 transition-colors"
          >
            <FolderPlus className="w-4 h-4 mr-2" />
            Categoría
          </button>
          
          <button 
            disabled={!can('SERVICES', 'CREATE')} onClick={handleOpenNew}
            className="flex items-center px-5 py-2.5 bg-purple-600 text-white text-sm font-medium rounded-lg hover:bg-purple-700 transition-colors shadow-sm"
          >
            <Plus className="w-4 h-4 mr-2" />
            Nuevo Servicio
          </button>
        </div>
      </div>

      {can('SERVICES', 'GENERATE') && <ArtifactGenerator scope="SERVICE" title="Folleto de Servicios (PDF)" />}

      <div className="bg-white rounded-xl shadow-sm border border-gray-200 p-6 mt-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-gray-100 pb-2">
          Lista de Servicios ({services.length})
        </h3>
        
        <div className="space-y-3">
          {services.length === 0 ? (
            <div className="text-center py-10 text-gray-500">No hay servicios disponibles en esta sede.</div>
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
                      {service.isActive 
                         ? <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-700">● ACTIVO</span>
                         : <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">INACTIVO</span>
                      }
                      <span className="ml-3 text-xs text-gray-500 border-l border-gray-200 pl-3">
                        {service.durationInMinutes} min • {service.currency} {service.priceMinorUnits ? (service.priceMinorUnits / 100).toFixed(2) : '0.00'}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2 relative">
                  <button disabled={!can('SERVICES', 'UPDATE')} onClick={() => handleOpenEdit(service)} className="p-2.5 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors" title="Editar">
                    <Pencil className="w-4 h-4" />
                  </button>
                  <button disabled={!can('SERVICES', 'DELETE')} onClick={() => setDeleteConfirmId(service.id!)} className="p-2.5 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors" title="Eliminar">
                    <Trash2 className="w-4 h-4" />
                  </button>

                  {/* Modal en línea para borrar (Reemplaza confirm) */}
                  {deleteConfirmId === service.id && (
                    <div className="absolute right-0 top-12 bg-white border border-red-200 shadow-xl p-3 rounded-lg z-10 w-48">
                      <p className="text-xs text-red-600 font-medium mb-2">¿Eliminar servicio?</p>
                      <div className="flex justify-between gap-2">
                        <button onClick={() => setDeleteConfirmId(null)} className="flex-1 text-xs bg-gray-100 py-1 rounded">No</button>
                        <button onClick={() => deleteMutation.mutate(service.id!)} disabled={deleteMutation.isPending || !can('SERVICES', 'DELETE')} className="flex-1 text-xs bg-red-600 text-white py-1 rounded">Sí, borrar</button>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      <ServiceModal 
        isOpen={isModalOpen && can('SERVICES', serviceToEdit ? 'UPDATE' : 'CREATE')}
        onClose={() => setIsModalOpen(false)}
        onSave={async (service) => { if (can('SERVICES', service.id ? 'UPDATE' : 'CREATE')) await saveMutation.mutateAsync(service); }}
        initialData={serviceToEdit}
      />
    </div>
  );
};
