import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Trash2, Map, MapPin, Pencil, X } from 'lucide-react';
import { getLocations, createLocation, updateLocation, deleteLocation } from '../services/business.service';
import type { LocationDto } from '../types/business.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const LocationsTab = ({ showMessage }: { showMessage: (msg: string, type: 'success' | 'error') => void }) => {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const emptyLocation: Partial<LocationDto> = { name: '', address: '', reference: '', mapUrl: '', isMain: false };
  const [newLocation, setNewLocation] = useState<Partial<LocationDto>>(emptyLocation);
  const [isFormOpen, setIsFormOpen] = useState(false);
  const [deleteId, setDeleteId] = useState<string | null>(null);

  const { data: locations = [], isLoading } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: !!workspaceId && can('LOCATIONS', 'READ'),

  });

  // 🔥 SPRINT 06: Decisión dinámica POST/PUT
  const saveMutation = useSessionMutation({
    mutationFn: (loc: LocationDto) => loc.id ? updateLocation(loc.id, loc) : createLocation(loc),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.locations.all(workspaceId) });
      showMessage(newLocation.id ? 'Sede actualizada exitosamente' : 'Sede registrada exitosamente', 'success');
      setNewLocation(emptyLocation);
      setIsFormOpen(false);
    },
    onError: (error: unknown) => showMessage(getApiErrorPresentation(error), 'error')
  });

  const deleteMutation = useSessionMutation({
    mutationFn: deleteLocation,
    onSuccess: (_, locationId) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.locations.all(workspaceId) });
      queryClient.removeQueries({ queryKey: queryKeys.hours.byLocation(workspaceId, locationId) });
      queryClient.removeQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, locationId) });
      showMessage('Sede eliminada correctamente', 'success');
    },
    onError: (error: unknown) => {
      showMessage(getApiErrorPresentation(error), 'error');
    }
  });

  const handleEditClick = (loc: LocationDto) => {
    setNewLocation(loc);
    setIsFormOpen(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleSaveLocation = (e: React.FormEvent) => {
    e.preventDefault();
    const locToSave = { ...newLocation, isMain: locations.length === 0 ? true : newLocation.isMain } as LocationDto;
    if (can('LOCATIONS', locToSave.id ? 'UPDATE' : 'CREATE')) saveMutation.mutate(locToSave);
  };

  const handleDeleteLocation = (locationId: string) => { if (can('LOCATIONS', 'DELETE')) setDeleteId(locationId); };

  if (isLoading) return <div className="p-6 text-center text-gray-500">Cargando sedes...</div>;

  return (
    <div className="space-y-6 animate-in fade-in">
      {deleteId && <div role="dialog" className="bg-white border rounded-lg p-4">
        <p>¿Eliminar esta sede? Las reservas futuras o referencias impedirán su eliminación.</p>
        <button disabled={deleteMutation.isPending || !can('LOCATIONS', 'DELETE')} onClick={() => { deleteMutation.mutate(deleteId); setDeleteId(null); }}>Eliminar</button>
        <button onClick={() => setDeleteId(null)}>Volver</button>
      </div>}
      <div className="flex justify-between items-center bg-white p-4 rounded-xl border shadow-sm">
        <h3 className="font-bold text-gray-900">Gestión de Locales</h3>
        {!isFormOpen && (
          <button disabled={!can('LOCATIONS', 'CREATE')} onClick={() => { setNewLocation(emptyLocation); setIsFormOpen(true); }} className="px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 transition-colors">
            + Añadir Nueva Sede
          </button>
        )}
      </div>

      {isFormOpen && (
        <form onSubmit={handleSaveLocation} className="bg-white shadow-sm border-2 border-blue-100 rounded-xl p-6 animate-in slide-in-from-top-4">
          <div className="flex justify-between items-center mb-4 pb-2 border-b">
            <h3 className="text-lg font-bold text-blue-900 flex items-center">
              <MapPin className="w-5 h-5 mr-2 text-blue-600"/> {newLocation.id ? 'Editar Sede' : 'Registrar Nueva Sede'}
            </h3>
            <button type="button" onClick={() => setIsFormOpen(false)} className="text-gray-400 hover:text-gray-700">
              <X className="w-5 h-5" />
            </button>
          </div>
          
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Nombre (Ej: Sucursal Centro) *</label>
              <input type="text" value={newLocation.name || ''} onChange={e => setNewLocation({...newLocation, name: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" required />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Dirección Exacta *</label>
              <input type="text" value={newLocation.address || ''} onChange={e => setNewLocation({...newLocation, address: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" required />
            </div>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-5">
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1">Referencia</label>
              <input type="text" value={newLocation.reference || ''} onChange={e => setNewLocation({...newLocation, reference: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" placeholder="Ej: Frente al parque central" />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 mb-1 flex items-center">
                 Enlace de Google Maps
              </label>
              <input type="url" value={newLocation.mapUrl || ''} onChange={e => setNewLocation({...newLocation, mapUrl: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" placeholder="https://maps.app.goo.gl/..." />
              <p className="text-xs text-gray-500 mt-1">Vital para guiar a los clientes mediante IA.</p>
            </div>
          </div>

          {locations.length > 0 && (
            <div className="mb-4 flex items-center">
               <input type="checkbox" id="isMain" checked={newLocation.isMain || false} onChange={e => setNewLocation({...newLocation, isMain: e.target.checked})} className="w-4 h-4 text-blue-600 rounded focus:ring-blue-500" />
               <label htmlFor="isMain" className="ml-2 text-sm text-gray-700">Definir como mi Sede Principal</label>
            </div>
          )}

          <div className="flex justify-end pt-4 mt-2 border-t gap-3">
            <button type="button" onClick={() => setIsFormOpen(false)} className="px-5 py-2 text-sm font-medium text-gray-600 bg-gray-100 rounded-lg hover:bg-gray-200">
              Cancelar
            </button>
            <button type="submit" disabled={saveMutation.isPending || !can('LOCATIONS', newLocation.id ? 'UPDATE' : 'CREATE')} className="px-5 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 disabled:opacity-50">
              {saveMutation.isPending ? 'Guardando...' : (newLocation.id ? 'Guardar Cambios' : 'Añadir Sede')}
            </button>
          </div>
        </form>
      )}

      <div className="bg-white shadow-sm border border-gray-200 rounded-xl overflow-hidden">
        {locations.length === 0 ? (
          <div className="p-8 text-center text-gray-500 italic">Aún no hay sedes registradas.</div>
        ) : (
          <ul className="divide-y divide-gray-100">
            {locations.map(loc => (
              <li key={loc.id} className="p-6 flex justify-between items-center group hover:bg-gray-50 transition-colors">
                <div>
                  <h4 className="font-semibold text-gray-900 flex items-center text-lg">
                    {loc.name} 
                    {loc.isMain && <span className="ml-3 px-2 py-0.5 bg-green-100 text-green-700 text-xs rounded-full border border-green-200">Sede Principal</span>}
                  </h4>
                  <div className="mt-2 space-y-1">
                    <p className="text-sm text-gray-600 flex items-start"><MapPin className="w-4 h-4 mr-2 text-gray-400 mt-0.5 shrink-0"/> {loc.address}</p>
                    {loc.reference && <p className="text-sm text-gray-500 flex items-start pl-6"><span className="font-medium mr-1">Ref:</span> {loc.reference}</p>}
                    
                    {loc.mapUrl && (
                       <a href={loc.mapUrl} target="_blank" rel="noopener noreferrer" className="text-sm text-blue-600 hover:underline flex items-center pl-6 mt-1">
                         <Map className="w-4 h-4 mr-1"/> Ver en Google Maps
                       </a>
                    )}
                  </div>
                </div>
                
                <div className="flex space-x-2">
                  <button disabled={!can('LOCATIONS', 'UPDATE')} onClick={() => handleEditClick(loc)} className="p-2.5 opacity-0 group-hover:opacity-100 text-blue-500 hover:text-blue-700 hover:bg-blue-100 rounded-lg transition-all" title="Editar Sede">
                    <Pencil className="w-5 h-5" />
                  </button>
                  <button disabled={!can('LOCATIONS', 'DELETE')} onClick={() => handleDeleteLocation(loc.id!)} className="p-2.5 opacity-0 group-hover:opacity-100 text-red-500 hover:text-red-700 hover:bg-red-100 rounded-lg transition-all" title="Eliminar Sede">
                    <Trash2 className="w-5 h-5" />
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </div>
  );
};
