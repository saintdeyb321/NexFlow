import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Plus, Pencil, Tag, Trash2 } from 'lucide-react';
import { getServices, saveService, deleteService } from '../services/business.service';
import type { ServiceDto } from '../types/business.types';
import { ServiceModal } from '../components/ServiceModal';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const ServicesPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [serviceToEdit, setServiceToEdit] = useState<ServiceDto | null>(null);

  // 🔥 Auditoría (Sprint 5.1): Aislamiento de Servicios (Removido Zustand)
  const { data: services = [], isLoading: isServicesLoading } = useQuery({
    queryKey: ['services', workspaceId],
    queryFn: getServices,
    enabled: !!workspaceId,
    staleTime: 1000 * 60 * 10,
  });

  const saveMutation = useMutation({
    mutationFn: saveService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['services', workspaceId] });
      setIsModalOpen(false);
    },
    onError: (error: any) => alert(`Error al guardar: ${error.message}`)
  });

  const deleteMutation = useMutation({
    mutationFn: deleteService,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['services', workspaceId] });
    },
    onError: (error: any) => alert(`Error al eliminar: ${error.message}`)
  });

  const handleOpenNew = () => {
    setServiceToEdit(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (service: ServiceDto) => {
    setServiceToEdit(service);
    setIsModalOpen(true);
  };

  const handleDelete = (serviceId: string) => {
    if (window.confirm('¿Estás seguro de que deseas eliminar este servicio?')) {
      deleteMutation.mutate(serviceId);
    }
  };

  if (isServicesLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando servicios...</div>;

  return (
    <div className="max-w-5xl">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center mb-8 gap-4">
        <div className="flex items-center">
          <div className="w-10 h-10 bg-yellow-100 rounded-lg flex items-center justify-center mr-4">
             <Tag className="w-5 h-5 text-yellow-600" />
          </div>
          <h1 className="text-2xl font-bold text-gray-900">Servicios</h1>
        </div>
        
        <button 
          onClick={handleOpenNew}
          className="flex items-center px-5 py-2.5 bg-purple-700 text-white text-sm font-medium rounded-lg hover:bg-purple-800 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4 mr-2" />
          Nuevo Servicio
        </button>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-gray-100 pb-2">
          Lista de Servicios ({services.length})
        </h3>
        
        <div className="space-y-3">
          {services.length === 0 ? (
            <div className="text-center py-10 text-gray-500">Aún no tienes servicios registrados.</div>
          ) : (
            services.map((service) => (
              <div 
                key={service.id} 
                className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-xl hover:border-blue-200 hover:shadow-sm transition-all"
              >
                <div className="flex items-center">
                  <div className="w-12 h-12 bg-blue-50 rounded-lg flex items-center justify-center mr-4">
                    <Tag className="w-5 h-5 text-blue-500" />
                  </div>
                  <div>
                    <h4 className="font-bold text-gray-900 text-sm md:text-base">{service.name}</h4>
                    <div className="flex items-center mt-1">
                      {service.isActive ? (
                         <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-green-100 text-green-700">● ACTIVO</span>
                      ) : (
                         <span className="inline-flex items-center px-2 py-0.5 rounded text-xs font-medium bg-gray-100 text-gray-600">INACTIVO</span>
                      )}
                      <span className="ml-3 text-xs text-gray-500 border-l border-gray-200 pl-3">
                        {service.durationInMinutes} min • S/ {service.priceMinorUnits ? (service.priceMinorUnits / 100).toFixed(2) : '0.00'}
                      </span>
                    </div>
                  </div>
                </div>

                <div className="flex gap-2">
                  <button
                    onClick={() => handleOpenEdit(service)}
                    className="p-2.5 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors"
                    title="Editar"
                  >
                    <Pencil className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(service.id!)}
                    className="p-2.5 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors"
                    title="Eliminar"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      <ServiceModal 
        isOpen={isModalOpen} 
        onClose={() => setIsModalOpen(false)}
        onSave={async (service) => {
          await saveMutation.mutateAsync(service);
        }}
        initialData={serviceToEdit}
      />
    </div>
  );
};