import { useState, useEffect } from 'react';
import { Plus, Pencil, Tag, Trash2 } from 'lucide-react';
import { getServices, saveService } from '../services/business.service';
import type { ServiceDto } from '../types/business.types';
import { ServiceModal } from '../components/ServiceModal';
// 🔥 Necesitamos importar axiosClient para la eliminación directa
import { axiosClient } from '../../../core/api/axiosClient';

export const ServicesPage = () => {
  const [services, setServices] = useState<ServiceDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [serviceToEdit, setServiceToEdit] = useState<ServiceDto | null>(null);

  useEffect(() => {
    loadServices();
  }, []);

  const loadServices = async () => {
    try {
      const data = await getServices();
      setServices(data || []);
    } catch (error: any) {
      alert(`Error: ${error.message}`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleOpenNew = () => {
    setServiceToEdit(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (service: ServiceDto) => {
    setServiceToEdit(service);
    setIsModalOpen(true);
  };

  // 🔥 NUEVO: Función para eliminar
  const handleDelete = async (serviceId: string) => {
    if (!window.confirm('¿Estás seguro de que deseas eliminar este servicio?')) return;
    
    try {
      await axiosClient.delete(`/business/services/${serviceId}`);
      setServices(prev => prev.filter(s => s.id !== serviceId));
    } catch (error: any) {
      alert(`Error al eliminar: ${error.message}`);
    }
  };

  const handleSaveService = async (service: ServiceDto) => {
    try {
      await saveService(service);
      setServices(prev => {
        const exists = prev.find(s => s.id === service.id);
        if (exists) return prev.map(s => s.id === service.id ? service : s);
        return [...prev, service];
      });
    } catch (error: any) {
      alert(`Error al guardar: ${error.message}`);
      throw error; 
    }
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando servicios...</div>;

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
                        {service.durationInMinutes} min • S/ {service.price?.toFixed(2) || '0.00'}
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
                  {/* 🔥 BOTÓN ELIMINAR */}
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
        onSave={handleSaveService}
        initialData={serviceToEdit}
      />
    </div>
  );
};