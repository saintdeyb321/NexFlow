import { useState, useEffect } from 'react';
import { Clock, Plus, Trash2, Scissors } from 'lucide-react';
import { getServices, saveService, deleteService } from '../services/business.service';
import type { ServiceDto } from '../types/business.types';

export const ServicesPage = () => {
  const [services, setServices] = useState<ServiceDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  
  const [newService, setNewService] = useState<Partial<ServiceDto>>({
    name: '',
    durationInMinutes: 30,
    requiresReservation: true,
    isActive: true
  });

  useEffect(() => {
    loadServices();
  }, []);

  const loadServices = async () => {
    try {
      const data = await getServices();
      setServices(data || []);
    } catch (error: any) {
      alert(`Error cargando servicios: ${error.response?.data?.error || error.message || 'Error desconocido'}`);
    } finally {
      setIsLoading(false);
    }
  };

  const handleAddService = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newService.name || !newService.durationInMinutes) return;

    setIsSaving(true);
    try {
      const serviceToSave: ServiceDto = {
        id: crypto.randomUUID(), 
        name: newService.name,
        durationInMinutes: newService.durationInMinutes,
        requiresReservation: newService.requiresReservation ?? true,
        isActive: newService.isActive ?? true
      };
      
      await saveService(serviceToSave);
      setServices([...services, serviceToSave]);
      setNewService({ name: '', durationInMinutes: 30, requiresReservation: true, isActive: true }); 
    } catch (error: any) {
      // 🔥 CORRECCIÓN: Feedback visual si falla
      alert(`No se pudo guardar el servicio: ${error.response?.data?.error || error.message || 'Inténtalo de nuevo.'}`);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (serviceId: string) => {
    if (!window.confirm("¿Seguro que deseas eliminar este servicio?")) return;
    try {
      await deleteService(serviceId);
      setServices(services.filter(s => s.id !== serviceId));
    } catch (error: any) {
      alert(`Error al eliminar: ${error.response?.data?.error || error.message || 'Error desconocido'}`);
    }
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando servicios...</div>;

  return (
    <div className="max-w-4xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">
          <Scissors className="w-6 h-6 mr-3 text-blue-600" />
          Servicios Ofrecidos
        </h1>
        <p className="mt-1 text-sm text-gray-500">Define los servicios y su duración. La IA usará estos tiempos para calcular la disponibilidad en la agenda.</p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        <div className="lg:col-span-1">
          <form onSubmit={handleAddService} className="bg-white shadow-sm border border-gray-200 rounded-xl p-5 sticky top-6">
            <h3 className="font-semibold text-gray-900 mb-4">Añadir Servicio</h3>
            
            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-700 mb-1">Nombre del Servicio</label>
              <input
                type="text"
                value={newService.name}
                onChange={(e) => setNewService({ ...newService, name: e.target.value })}
                placeholder="Ej: Corte de cabello"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 outline-none"
                required
              />
            </div>
            
            <div className="mb-6">
              <label className="block text-sm font-medium text-gray-700 mb-1">Duración (Minutos)</label>
              <input
                type="number"
                min="5"
                step="5"
                value={newService.durationInMinutes}
                onChange={(e) => setNewService({ ...newService, durationInMinutes: parseInt(e.target.value) })}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg focus:ring-2 focus:ring-blue-500 outline-none"
                required
              />
            </div>

            <button
              type="submit"
              disabled={isSaving}
              className="w-full flex items-center justify-center px-4 py-2 bg-blue-600 text-white font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50 transition-colors"
            >
              <Plus className="w-4 h-4 mr-2" />
              {isSaving ? 'Guardando...' : 'Añadir a la lista'}
            </button>
          </form>
        </div>

        <div className="lg:col-span-2 space-y-3">
          {services.length === 0 ? (
            <div className="text-center p-8 bg-gray-50 border border-dashed border-gray-300 rounded-xl text-gray-500">
              No hay servicios registrados. Empieza creando uno.
            </div>
          ) : (
            services.map((service) => (
              <div key={service.id} className="flex items-center justify-between p-4 bg-white border border-gray-200 rounded-xl hover:shadow-sm transition-shadow">
                <div>
                  <h4 className="font-medium text-gray-900">{service.name}</h4>
                  <div className="flex items-center text-sm text-gray-500 mt-1">
                    <Clock className="w-4 h-4 mr-1" />
                    {service.durationInMinutes} minutos
                  </div>
                </div>
                <button
                  onClick={() => handleDelete(service.id)}
                  className="p-2 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                >
                  <Trash2 className="w-5 h-5" />
                </button>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};