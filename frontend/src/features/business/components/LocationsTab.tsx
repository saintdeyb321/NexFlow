import { useState, useEffect } from 'react';
import { getLocations, saveLocation } from '../services/business.service';
import type { LocationDto } from '../types/business.types';

export const LocationsTab = ({ showMessage }: { showMessage: (msg: string, type: 'success' | 'error') => void }) => {
  const [locations, setLocations] = useState<LocationDto[]>([]);
  const [newLocation, setNewLocation] = useState<Partial<LocationDto>>({ name: '', address: '', reference: '', isMain: true });
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    loadLocations();
  }, []);

  const loadLocations = async () => {
    try {
      const data = await getLocations();
      setLocations(data);
    } catch (error) {
      console.error("Error al cargar sedes", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleSaveLocation = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSaving(true);
    try {
      const locToSave = { ...newLocation, isMain: locations.length === 0 ? true : newLocation.isMain } as LocationDto;
      await saveLocation(locToSave);
      showMessage('Sede registrada exitosamente', 'success');
      
      const updatedLocs = await getLocations();
      setLocations(updatedLocs);
      setNewLocation({ name: '', address: '', reference: '', isMain: false });
    } catch (error: any) { 
      // Mostramos el mensaje de error que viene del backend (Ej: "Límite de sedes alcanzado")
      const errorMsg = error.response?.data?.detail || error.response?.data || 'Error guardando la sede';
      showMessage(typeof errorMsg === 'string' ? errorMsg : 'Error guardando la sede', 'error'); 
    } finally { 
      setIsSaving(false); 
    }
  };

  if (isLoading) return <div className="p-6 text-center text-gray-500">Cargando sedes...</div>;

  return (
    <div className="space-y-6 animate-in fade-in">
      <form onSubmit={handleSaveLocation} className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
        <h3 className="text-lg font-bold text-gray-900 mb-4">Registrar Nueva Sede</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
          <div>
            <label className="block text-sm font-medium mb-1">Nombre (Ej: Sucursal Centro)</label>
            <input type="text" value={newLocation.name} onChange={e => setNewLocation({...newLocation, name: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
          </div>
          <div>
            <label className="block text-sm font-medium mb-1">Dirección Exacta</label>
            <input type="text" value={newLocation.address} onChange={e => setNewLocation({...newLocation, address: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" required />
          </div>
        </div>
        <div className="mb-4">
          <label className="block text-sm font-medium mb-1">Referencia</label>
          <input type="text" value={newLocation.reference} onChange={e => setNewLocation({...newLocation, reference: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500" placeholder="Ej: Frente al parque central" />
        </div>
        <div className="flex justify-end">
          <button type="submit" disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
            {isSaving ? 'Guardando...' : 'Añadir Sede'}
          </button>
        </div>
      </form>

      <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
        <h3 className="text-lg font-bold text-gray-900 mb-4">Tus Sedes Registradas</h3>
        {locations.length === 0 ? (
          <p className="text-sm text-gray-500 italic">Aún no hay sedes registradas.</p>
        ) : (
          <ul className="divide-y divide-gray-100">
            {locations.map(loc => (
              <li key={loc.id} className="py-3 flex justify-between items-start">
                <div>
                  <h4 className="font-medium text-gray-900 flex items-center">
                    {loc.name} {loc.isMain && <span className="ml-2 px-2 py-0.5 bg-green-100 text-green-700 text-xs rounded-full">Sede Principal</span>}
                  </h4>
                  <p className="text-sm text-gray-500 mt-1">{loc.address}</p>
                  {loc.reference && <p className="text-xs text-gray-400 mt-0.5">Ref: {loc.reference}</p>}
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
};