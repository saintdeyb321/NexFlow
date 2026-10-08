import { useRef, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { MapPin } from 'lucide-react';
import { Alert, LoadingState, ErrorState, EmptyState } from '../../../components/ui/Feedback';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { getQuerySession } from '../../../core/query/queryPersistence';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions, can as hasCapability } from '../../../core/auth/permissions';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessHours, saveBusinessHours } from '../services/business.service';
import type { BusinessHoursDto } from '../types/business.types';
import { hoursPayload, hoursWeek, invalidateHoursViews, sameHours } from '../utils/businessHours';
import { HoursWeekEditor } from './HoursWeekEditor';

export const HoursTab = () => {
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const userId = useAuthStore(state => state.me?.user.id);
  if (!workspaceId || !userId || !can('BUSINESS_HOURS', 'READ')) return <EmptyState className="nf-panel" title="Horarios no disponibles" description="No tienes permiso para consultar los horarios de este negocio." />;
  return <WorkspaceHours key={JSON.stringify([workspaceId, userId, getQuerySession()])} workspaceId={workspaceId} userId={userId} />;
};

const WorkspaceHours = ({ workspaceId, userId }: { workspaceId: string; userId: string }) => {
  const queryClient = useQueryClient();
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);
  // Only edited UI drafts live here. Server schedules remain in TanStack Query.
  const [drafts, setDrafts] = useState<Record<string, BusinessHoursDto[]>>({});
  const [notices, setNotices] = useState<Record<string, { error?: string; success?: string }>>({});
  const [savingLocation, setSavingLocation] = useState<string | null>(null);
  const submitting = useRef(false);
  const save = useSessionMutation({
    mutationFn: ({ locationId, hours }: { locationId: string; hours: BusinessHoursDto[] }) => {
      const me = useAuthStore.getState().me;
      if (me?.workspace?.id !== workspaceId || me.user.id !== userId || !hasCapability(me, 'BUSINESS_HOURS', 'UPDATE') || !locationId || locationId === 'all')
        return Promise.reject(new Error('No tienes permiso para guardar los horarios de esta sede.'));
      return saveBusinessHours(locationId, hours);
    },
    onSuccess: (_, { locationId, hours }) => {
      queryClient.setQueryData(queryKeys.hours.byLocation(workspaceId, locationId), hours);
      setDrafts(previous => { const next = { ...previous }; delete next[locationId]; return next; });
      setNotices(previous => ({ ...previous, [locationId]: { success: 'Horarios guardados correctamente.' } }));
      return invalidateHoursViews(queryClient, workspaceId, locationId);
    },
    onError: (error, { locationId }) => setNotices(previous => ({ ...previous, [locationId]: { error: getApiErrorPresentation(error) } })),
    onSettled: () => { submitting.current = false; setSavingLocation(null); },
  });
  const pendingElsewhere = Object.keys(drafts).filter(id => id !== selectedLocationId).length;
  const saveHours = (hours: BusinessHoursDto[]) => {
    const latest = useAuthStore.getState();
    if (submitting.current || latest.selectedLocationId !== selectedLocationId || !selectedLocationId || selectedLocationId === 'all') return;
    if (latest.me?.workspace?.id !== workspaceId || latest.me.user.id !== userId || !hasCapability(latest.me, 'BUSINESS_HOURS', 'UPDATE')) return;
    let payload: BusinessHoursDto[];
    try { payload = hoursPayload(hours); }
    catch (error) { setNotices(previous => ({ ...previous, [selectedLocationId]: { error: error instanceof Error ? error.message : 'Revisa los horarios.' } })); return; }
    submitting.current = true; setSavingLocation(selectedLocationId);
    setNotices(previous => ({ ...previous, [selectedLocationId]: {} }));
    save.mutate({ locationId: selectedLocationId, hours: payload });
  };
  return <div className="space-y-4">
    {pendingElsewhere > 0 && <Alert tone="warning">Tienes cambios sin guardar en {pendingElsewhere === 1 ? 'otra sede' : `${pendingElsewhere} sedes`}. Se conservan en esta sesión; vuelve a cada sede para guardarlos o descartarlos.</Alert>}
    {savingLocation && savingLocation !== selectedLocationId && <Alert>Guardando los horarios de la sede anterior. Puedes revisar esta sede mientras termina.</Alert>}
    {!selectedLocationId || selectedLocationId === 'all'
      ? <EmptyState className="nf-panel" icon={<MapPin aria-hidden="true" className="w-6 h-6" />} title="Selecciona una sede" description="Los horarios se configuran por sede. Usa el selector del menú para continuar." />
      : <LocationHours key={selectedLocationId} workspaceId={workspaceId} locationId={selectedLocationId} draft={drafts[selectedLocationId]} notice={notices[selectedLocationId]}
        saving={savingLocation === selectedLocationId} saveBlocked={savingLocation !== null} onSave={saveHours}
        onDraft={(hours, baseline) => {
          setDrafts(previous => { const next = { ...previous }; if (sameHours(hours, baseline)) delete next[selectedLocationId]; else next[selectedLocationId] = hours; return next; });
          setNotices(previous => ({ ...previous, [selectedLocationId]: {} }));
        }}
        onDiscard={() => {
          setDrafts(previous => { const next = { ...previous }; delete next[selectedLocationId]; return next; });
          setNotices(previous => ({ ...previous, [selectedLocationId]: {} }));
        }} />}
  </div>;
};

interface LocationHoursProps {
  workspaceId: string; locationId: string; draft?: BusinessHoursDto[]; notice?: { error?: string; success?: string };
  saving: boolean; saveBlocked: boolean; onSave: (hours: BusinessHoursDto[]) => void;
  onDraft: (hours: BusinessHoursDto[], baseline: BusinessHoursDto[]) => void; onDiscard: () => void;
}
const LocationHours = ({ workspaceId, locationId, draft, notice, saving, saveBlocked, onSave, onDraft, onDiscard }: LocationHoursProps) => {
  const { can } = usePermissions();
  const query = useQuery({
    ...queryPolicies.stable, queryKey: queryKeys.hours.byLocation(workspaceId, locationId),
    queryFn: async ({ signal }) => { const rows = await getBusinessHours(locationId, signal); hoursWeek(rows); return rows; },
  });
  if (query.isPending) return <LoadingState title="Cargando horarios de la sede..." />;
  if (query.isError) return <ErrorState title="No se pudieron cargar los horarios" description={getApiErrorPresentation(query.error)} onRetry={() => void query.refetch()} />;
  const baseline = hoursWeek(query.data);
  return <HoursWeekEditor hours={draft ?? baseline} unconfigured={query.data.length === 0} partial={query.data.length > 0 && query.data.length < 7}
    edited={!!draft} editable={can('BUSINESS_HOURS', 'UPDATE')} saving={saving} saveBlocked={saveBlocked} error={notice?.error} success={notice?.success}
    onChange={hours => onDraft(hours, baseline)} onDiscard={onDiscard} onSave={onSave} />;
};
