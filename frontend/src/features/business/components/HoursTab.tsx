import { useState, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { getBusinessHours, saveBusinessHours } from '../services/business.service';
import type { BusinessHoursDto } from '../types/business.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { MapPin } from 'lucide-react';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const HoursTab = ({ showMessage }: { showMessage: (msg: string, type: 'success' | 'error') => void }) => {
  const queryClient = useQueryClient();
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);
  const [hours, setHours] = useState<BusinessHoursDto[]>([]);

  // 🔥 Sprint 5.1/5.2: Carga usando TanStack vinculada al Selector Global
  const { data: fetchedHours, isLoading } = useQuery({
    queryKey: ['businessHours', selectedLocationId],
    queryFn: () => getBusinessHours(selectedLocationId),
    enabled: selectedLocationId !== 'all',
  });

  useEffect(() => {
    if (fetchedHours && fetchedHours.length > 0) {
      setHours(fetchedHours);
    } else {
      setHours(DAYS_OF_WEEK.map(d => ({ dayOfWeek: d.id, openTime: '08:00', closeTime: '18:00', isClosed: d.id === 0 })));
    }
  }, [fetchedHours, selectedLocationId]);

  const saveMutation = useMutation({
    mutationFn: (newHours: BusinessHoursDto[]) => saveBusinessHours(selectedLocationId, newHours),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['businessHours', selectedLocationId] });
      showMessage('Horarios actualizados correctamente', 'success');
    },
    onError: (error: any) => {
      showMessage(error.message || 'Error guardando horarios', 'error');
    }
  });

  const updateHour = (day: number, field: keyof BusinessHoursDto, value: any) => {
    setHours(hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h));
  };

  const handleSave = async () => {
    for (const h of hours) {
      if (!h.isClosed) {
        if (!h.openTime || !h.closeTime) {
          const dayName = DAYS_OF_WEEK.find(d => d.id === h.dayOfWeek)?.name;
          showMessage(`Completa la hora de apertura y cierre para el día ${dayName}.`, 'error');
          return;
        }
        if (h.openTime >= h.closeTime) {
          const dayName = DAYS_OF_WEEK.find(d => d.id === h.dayOfWeek)?.name;
          showMessage(`En el día ${dayName}, la hora de apertura (${h.openTime}) debe ser menor al cierre (${h.closeTime}).`, 'error');
          return;
        }
      }
    }
    saveMutation.mutate(hours);
  };

  // 🔥 Bloqueo Estricto si está en "Todas las sedes"
  if (selectedLocationId === 'all') {
    return (
      <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-12 text-center animate-in fade-in">
        <div className="w-16 h-16 bg-blue-50 rounded-full flex items-center justify-center mx-auto mb-4">
          <MapPin className="w-8 h-8 text-blue-500" />
        </div>
        <h3 className="text-xl font-bold text-gray-900 mb-2">Selecciona una sede específica</h3>
        <p className="text-gray-500 max-w-md mx-auto">
          Los horarios de atención se configuran de manera individual por cada local. Por favor, usa el selector de sedes en la barra lateral izquierda para continuar.
        </p>
      </div>
    );
  }

  if (isLoading) return <div className="p-6 text-center text-gray-500">Cargando horarios de la sede...</div>;

  return (
    <div className="bg-white shadow-sm border border-gray-200 rounded-xl p-6 animate-in fade-in">
      <div className="space-y-4 pt-2">
        {DAYS_OF_WEEK.map(day => {
          const h = hours.find(x => x.dayOfWeek === day.id) || { openTime: '', closeTime: '', isClosed: true, dayOfWeek: day.id };
          return (
            <div key={day.id} className="flex items-center justify-between border-b pb-3">
              <div className="w-32 font-medium text-gray-700">{day.name}</div>
              <div className="flex items-center space-x-4">
                <label className="flex items-center text-sm text-gray-600 cursor-pointer">
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
        <button onClick={handleSave} disabled={saveMutation.isPending} className="px-5 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
          {saveMutation.isPending ? 'Guardando...' : 'Guardar Horarios'}
        </button>
      </div>
    </div>
  );
};