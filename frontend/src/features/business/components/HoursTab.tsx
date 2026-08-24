import { useState, useEffect } from 'react';
import { getLocations, getBusinessHours, saveBusinessHours } from '../services/business.service';
import type { BusinessHoursDto, LocationDto } from '../types/business.types';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const HoursTab = ({ showMessage }: { showMessage: (msg: string, type: 'success' | 'error') => void }) => {
  const [locations, setLocations] = useState<LocationDto[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string>('');
  const [hours, setHours] = useState<BusinessHoursDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);

  useEffect(() => {
    loadLocations();
  }, []);

  useEffect(() => {
    if (selectedLocationId) loadHours(selectedLocationId);
  }, [selectedLocationId]);

  const loadLocations = async () => {
    try {
      const locs = await getLocations();
      setLocations(locs);
      if (locs.length > 0) setSelectedLocationId(locs[0].id!);
    } catch (error) {
      showMessage('Error al cargar las sedes.', 'error');
    } finally {
      setIsLoading(false);
    }
  };

  const loadHours = async (locationId: string) => {
    try {
      const data = await getBusinessHours(locationId);
      if (data && data.length > 0) {
        setHours(data);
      } else {
        setHours(DAYS_OF_WEEK.map(d => ({ dayOfWeek: d.id, openTime: '08:00', closeTime: '18:00', isClosed: d.id === 0 })));
      }
    } catch (error) {
      showMessage('Error al cargar los horarios.', 'error');
    }
  };

  const updateHour = (day: number, field: keyof BusinessHoursDto, value: any) => {
    setHours(hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h));
  };

  const handleSave = async () => {
    setIsSaving(true);
    try {
      await saveBusinessHours(selectedLocationId, hours);
      showMessage('Horarios actualizados correctamente', 'success');
    } catch {
      showMessage('Error guardando horarios', 'error');
    } finally {
      setIsSaving(false);
    }
  };

  if (isLoading) return <div className="p-6 text-center text-gray-500">Cargando sedes...</div>;
  if (locations.length === 0) return <div className="p-6 text-center text-red-500">Debes registrar al menos una sede primero.</div>;

  return (
    <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-6">
      <div className="mb-6">
        <label className="block text-sm font-bold text-gray-700 mb-2">Selecciona la Sede</label>
        <select 
          value={selectedLocationId} 
          onChange={(e) => setSelectedLocationId(e.target.value)}
          className="w-full md:w-1/2 border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500"
        >
          {locations.map(loc => (
            <option key={loc.id} value={loc.id}>{loc.name} {loc.isMain ? '(Principal)' : ''}</option>
          ))}
        </select>
      </div>

      <div className="space-y-4 border-t pt-4">
        {DAYS_OF_WEEK.map(day => {
          const h = hours.find(x => x.dayOfWeek === day.id) || { openTime: '', closeTime: '', isClosed: true, dayOfWeek: day.id };
          return (
            <div key={day.id} className="flex items-center justify-between border-b pb-3">
              <div className="w-32 font-medium text-gray-700">{day.name}</div>
              <div className="flex items-center space-x-4">
                <label className="flex items-center text-sm text-gray-600">
                  <input type="checkbox" checked={h.isClosed} onChange={(e) => updateHour(day.id, 'isClosed', e.target.checked)} className="mr-2 rounded text-blue-600" />
                  Cerrado
                </label>
                <input type="time" disabled={h.isClosed} value={h.openTime} onChange={(e) => updateHour(day.id, 'openTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
                <span className="text-gray-400">-</span>
                <input type="time" disabled={h.isClosed} value={h.closeTime} onChange={(e) => updateHour(day.id, 'closeTime', e.target.value)} className="border rounded px-2 py-1 text-sm disabled:opacity-50" />
              </div>
            </div>
          )
        })}
      </div>
      <div className="flex justify-end mt-6">
        <button onClick={handleSave} disabled={isSaving} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700">{isSaving ? 'Guardando...' : 'Guardar Horarios'}</button>
      </div>
    </div>
  );
};