import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/useToast';
import { LoadingState, ErrorState, EmptyState } from '../../../components/ui/Feedback';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';

import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { getBusinessHours, saveBusinessHours } from '../services/business.service';
import type { BusinessHoursDto } from '../types/business.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { MapPin } from 'lucide-react';

const DAYS_OF_WEEK = [
  { id: 1, name: 'Lunes' }, { id: 2, name: 'Martes' }, { id: 3, name: 'Miércoles' },
  { id: 4, name: 'Jueves' }, { id: 5, name: 'Viernes' }, { id: 6, name: 'Sábado' }, { id: 0, name: 'Domingo' }
];

export const HoursTab = () => {
  const toast = useToast();
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const [draft, setDraft] = useState<{ locationId: string; hours: BusinessHoursDto[] } | null>(null);

  const { data: fetchedHours, isLoading, isError, refetch } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.hours.byLocation(workspaceId, selectedLocationId),
    queryFn: ({ signal }) => getBusinessHours(selectedLocationId, signal),
    enabled: selectedLocationId !== 'all' && !!workspaceId && can('BUSINESS_HOURS', 'READ'),
  });

  const hours = draft?.locationId === selectedLocationId ? draft.hours
    : DAYS_OF_WEEK.map(day => fetchedHours?.find(hour => hour.dayOfWeek === day.id)
      ?? { dayOfWeek: day.id, openTime: '', closeTime: '', isClosed: true });

  const saveMutation = useSessionMutation({
    mutationFn: ({ locationId, hours }: { locationId: string; hours: BusinessHoursDto[] }) => saveBusinessHours(locationId, hours),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.hours.byLocation(workspaceId, variables.locationId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.reservations.availabilityByLocation(workspaceId, variables.locationId) });
      if (variables.locationId === selectedLocationId) setDraft(null);
      toast.success('Horarios actualizados correctamente');
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  const updateHour = <K extends keyof BusinessHoursDto>(day: number, field: K, value: BusinessHoursDto[K]) => {
    setDraft({ locationId: selectedLocationId, hours: hours.map(h => h.dayOfWeek === day ? { ...h, [field]: value } : h) });
  };

  const handleSave = async () => {
    for (const h of hours) {
      if (!h.isClosed) {
        if (!h.openTime || !h.closeTime) {
          const dayName = DAYS_OF_WEEK.find(d => d.id === h.dayOfWeek)?.name;
          toast.warning(`Completa la hora de apertura y cierre para el día ${dayName}.`);
          return;
        }
        if (h.openTime >= h.closeTime) {
          const dayName = DAYS_OF_WEEK.find(d => d.id === h.dayOfWeek)?.name;
          toast.warning(`En el día ${dayName}, la hora de apertura (${h.openTime}) debe ser menor al cierre (${h.closeTime}).`);
          return;
        }
      }
    }
    if (can('BUSINESS_HOURS', 'UPDATE')) saveMutation.mutate({ locationId: selectedLocationId, hours });
  };

  if (isError) return <ErrorState onRetry={() => void refetch()} />;
  if (selectedLocationId === 'all') {
    return <EmptyState className="nf-panel" icon={<MapPin aria-hidden="true" className="w-6 h-6" />} title="Selecciona una sede" description="Los horarios se configuran por sede. Usa el selector del menú para continuar." />;
  }

  if (isLoading) return <LoadingState title="Cargando horarios de la sede..." />;

  return (
    <div className="bg-surface shadow-sm border border-line rounded-xl p-6 animate-in fade-in">
      <h2 className="text-lg font-semibold mb-1">Horario semanal</h2><p className="text-sm text-muted mb-6">Define los días y las horas de atención de esta sede.</p>
      <div className="space-y-4 pt-2">
        {DAYS_OF_WEEK.map(day => {
          const h = hours.find(x => x.dayOfWeek === day.id) || { openTime: '', closeTime: '', isClosed: true, dayOfWeek: day.id };
          return (
            <div key={day.id} className="grid grid-cols-1 sm:grid-cols-[8rem_1fr] items-center gap-3 border-b border-line pb-4">
              <div className="w-32 font-medium text-gray-700">{day.name}</div>
              <div className="grid grid-cols-2 sm:flex sm:flex-wrap items-center gap-3 min-w-0">
                <label className="col-span-2 flex items-center text-sm text-muted cursor-pointer min-h-11">
                  <input type="checkbox" disabled={!can('BUSINESS_HOURS', 'UPDATE')} checked={h.isClosed} onChange={(e) => updateHour(day.id, 'isClosed', e.target.checked)} className="mr-2 rounded text-primary" />
                  Cerrado
                </label>
                <input aria-label={`Apertura ${day.name}`} type="time" disabled={h.isClosed || !can('BUSINESS_HOURS', 'UPDATE')} value={h.openTime} onChange={(e) => updateHour(day.id, 'openTime', e.target.value)} className="nf-control sm:w-36" />
                <span aria-hidden="true" className="hidden sm:block text-muted">—</span>
                <input aria-label={`Cierre ${day.name}`} type="time" disabled={h.isClosed || !can('BUSINESS_HOURS', 'UPDATE')} value={h.closeTime} onChange={(e) => updateHour(day.id, 'closeTime', e.target.value)} className="nf-control sm:w-36" />
              </div>
            </div>
          )
        })}
      </div>
      <div className="flex justify-end mt-6">
        <Button variant="primary" isLoading={saveMutation.isPending} onClick={handleSave} disabled={saveMutation.isPending || !can('BUSINESS_HOURS', 'UPDATE')} className="disabled:opacity-50">
          {saveMutation.isPending ? 'Guardando...' : 'Guardar Horarios'}
        </Button>
      </div>
    </div>
  );
};
