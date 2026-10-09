import { useMemo, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Calendar, List, Search } from 'lucide-react';
import { PageHeader } from '../../../components/ui/Layout';
import { Button } from '../../../components/ui/Button';
import { FormField, Input, Select } from '../../../components/ui/Form';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, ErrorState, Alert } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/useToast';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';
import { ApiError, getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getBusinessToday } from '../../../core/utils/dateTime';
import { getQuerySession } from '../../../core/query/queryPersistence';
import { getReservationContext, getReservationsForWeek, cancelReservation, completeReservation } from '../services/reservation.service';
import { invalidateReservationViews } from '../services/reservation.queries';
import { getLocations } from '../../business/services/business.service';
import { getServices } from '../../services/services/services.service';
import { CreateReservationModal } from '../components/CreateReservationModal';
import { EditReservationModal } from '../components/EditReservationModal';
import { ReservationList } from '../components/ReservationList';
import { WeeklyAgenda } from '../components/WeeklyAgenda';
import { WeekNavigation } from '../components/WeekNavigation';
import { canRetainWeek, filterReservations, formatWeek, reservationStatusLabels, summarizeReservations, weekOf } from '../utils/weeklyAgenda';
import type { StatusFilter } from '../utils/weeklyAgenda';
import type { ReservationDto } from '../types/reservation.types';

const statusFilters: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'Todas' }, { value: 'Pending', label: 'Pendientes' },
  { value: 'Confirmed', label: 'Confirmadas' }, { value: 'Completed', label: 'Completadas' }, { value: 'Cancelled', label: 'Canceladas' },
];

export const ReservationsPage = () => {
  const me = useAuthStore(state => state.me);
  const workspaceId = me?.workspace?.id;
  const { can } = usePermissions();
  if (!workspaceId || !can('RESERVATIONS', 'READ')) return <Alert tone="warning">No tienes permiso para consultar reservas en este workspace.</Alert>;
  // Reset local navigation/forms when tenant identity changes; server state remains in TanStack Query.
  return <WorkspaceAgenda key={JSON.stringify([me.user.id, workspaceId, getQuerySession()])} workspaceId={workspaceId} />;
};

const WorkspaceAgenda = ({ workspaceId }: { workspaceId: string }) => {
  const context = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.reservations.context(workspaceId),
    queryFn: ({ signal }) => getReservationContext(signal),
  });
  if (context.isError) return <ErrorState title="No se pudo cargar la zona horaria de la agenda" description={context.error instanceof ApiError ? getApiErrorPresentation(context.error) : context.error.message} onRetry={() => void context.refetch()} />;
  if (context.isPending || context.isFetching) return <LoadingState title="Cargando zona horaria de la agenda..." />;
  return <ResolvedAgenda key={context.data.timeZone} workspaceId={workspaceId} timeZone={context.data.timeZone} />;
};

const ResolvedAgenda = ({ workspaceId, timeZone }: { workspaceId: string; timeZone: string }) => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const selectedLocationId = useAuthStore(state => state.selectedLocationId);
  const canReadServices = can('SERVICES', 'READ');
  const canReadLocations = can('LOCATIONS', 'READ');
  const today = getBusinessToday(timeZone);
  const [chosenDate, setChosenDate] = useState('');
  const [rangeError, setRangeError] = useState('');
  const range = weekOf(chosenDate || today);
  const [chosenDay, setChosenDay] = useState('');
  const [viewMode, setViewMode] = useState<'week' | 'list'>('week');
  const [status, setStatus] = useState<StatusFilter>('all');
  const [search, setSearch] = useState('');
  const [createDate, setCreateDate] = useState<string | null>(null);
  const [editingRes, setEditingRes] = useState<ReservationDto | null>(null);
  const [confirmDialog, setConfirmDialog] = useState<{ action: 'cancel' | 'complete'; reservation: ReservationDto } | null>(null);

  const servicesQuery = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.services.list(workspaceId, selectedLocationId),
    queryFn: ({ signal }) => getServices(selectedLocationId, signal),
    enabled: canReadServices,
  });
  const locationsQuery = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.locations.all(workspaceId),
    queryFn: ({ signal }) => getLocations(signal),
    enabled: canReadLocations,
  });
  const weekKey = queryKeys.reservations.week(workspaceId, selectedLocationId, range.from, range.to, timeZone);
  const reservationsQuery = useQuery({
    ...queryPolicies.dynamic,
    queryKey: weekKey,
    queryFn: async ({ signal }) => ({
      reservations: await getReservationsForWeek(selectedLocationId, range.from, range.to, signal),
      from: range.from, to: range.to,
    }),
    enabled: Boolean(selectedLocationId) && can('RESERVATIONS', 'READ'),
    placeholderData: (previous, previousQuery) => canRetainWeek(previousQuery?.queryKey, weekKey) ? previous : undefined,
  });
  const loadedRange = reservationsQuery.data ? weekOf(reservationsQuery.data.from) : range;
  const reservations = reservationsQuery.data?.reservations;
  const serviceNames = useMemo(() => new Map((canReadServices ? servicesQuery.data ?? [] : []).flatMap(service => service.id ? [[service.id, service.name] as const] : [])), [servicesQuery.data, canReadServices]);
  const locationNames = useMemo(() => new Map((canReadLocations ? locationsQuery.data ?? [] : []).flatMap(location => location.id ? [[location.id, location.name] as const] : [])), [locationsQuery.data, canReadLocations]);
  const visibleReservations = useMemo(() => filterReservations(reservations ?? [], status, search, serviceNames), [reservations, status, search, serviceNames]);
  const summary = useMemo(() => summarizeReservations(reservations ?? []), [reservations]);
  const selectedDay = loadedRange.days.includes(chosenDay) ? chosenDay : loadedRange.days.includes(today) ? today : loadedRange.from;
  const showingPrevious = reservationsQuery.isPlaceholderData;
  const canCreate = can('RESERVATIONS', 'CREATE') && !showingPrevious;

  const refresh = (reservation: Pick<ReservationDto, 'locationId'>) => invalidateReservationViews(queryClient, workspaceId, reservation.locationId);
  const cancelMutation = useSessionMutation({
    mutationFn: (reservation: ReservationDto) => cancelReservation(reservation.id),
    onSuccess: (_data, reservation) => { void refresh(reservation); toast.success('Reserva cancelada exitosamente.'); setConfirmDialog(null); },
    onError: error => { toast.toastApiError(error); setConfirmDialog(null); },
  });
  const completeMutation = useSessionMutation({
    mutationFn: (reservation: ReservationDto) => completeReservation(reservation.id),
    onSuccess: (_data, reservation) => { void refresh(reservation); toast.success('Reserva marcada como completada.'); setConfirmDialog(null); },
    onError: error => { toast.toastApiError(error); setConfirmDialog(null); },
  });
  const mutating = cancelMutation.isPending || completeMutation.isPending;
  const view = {
    timeZone, serviceNames, locationNames, showLocation: selectedLocationId === 'all',
    canEdit: can('RESERVATIONS', 'UPDATE') && !showingPrevious && !mutating,
    canCancel: can('RESERVATIONS', 'CANCEL') && !showingPrevious && !mutating,
    canComplete: can('RESERVATIONS', 'COMPLETE') && !showingPrevious && !mutating,
    onEdit: setEditingRes,
    onCancel: (reservation: ReservationDto) => setConfirmDialog({ action: 'cancel', reservation }),
    onComplete: (reservation: ReservationDto) => setConfirmDialog({ action: 'complete', reservation }),
  };
  const navigate = (date: string) => {
    try { weekOf(date); setChosenDate(date); setChosenDay(''); setRangeError(''); }
    catch { setRangeError('Selecciona una fecha válida cuya semana completa pueda consultarse.'); }
  };
  const executeAction = () => {
    if (!confirmDialog || mutating) return;
    if (confirmDialog.action === 'cancel' && can('RESERVATIONS', 'CANCEL')) cancelMutation.mutate(confirmDialog.reservation);
    if (confirmDialog.action === 'complete' && can('RESERVATIONS', 'COMPLETE') && confirmDialog.reservation.status === 'Confirmed') completeMutation.mutate(confirmDialog.reservation);
  };

  return <div className="nf-page">
    <PageHeader title="Reservas" description="Tu agenda de lunes a domingo. Consulta citas actuales e históricas por sede." icon={<Calendar className="w-5 h-5" />} actions={
      <Button onClick={() => setCreateDate(getBusinessToday(timeZone))} disabled={!canCreate}>Nueva reserva</Button>
    } />
    <>
      <WeekNavigation from={range.from} to={range.to} today={today} onChange={navigate} onCurrentWeek={() => navigate(getBusinessToday(timeZone))} />
      {rangeError && <Alert tone="error" className="mb-4">{rangeError}</Alert>}
      <p className="text-xs text-muted mb-4">Zona horaria: {timeZone} · {selectedLocationId === 'all' ? 'Todas las sedes' : locationNames.get(selectedLocationId) ?? 'Sede seleccionada'}</p>
      {selectedLocationId === 'all' && can('RESERVATIONS', 'CREATE') && <p className="text-sm text-muted mb-4">Puedes consultar todas las sedes. Para crear una reserva, selecciona una sede dentro del formulario.</p>}
      {reservationsQuery.isPending ? <LoadingState title="Cargando agenda semanal..." /> : reservationsQuery.isError && !reservationsQuery.data ? (
        <ErrorState description={getApiErrorPresentation(reservationsQuery.error)} onRetry={() => void reservationsQuery.refetch()} />
      ) : reservationsQuery.data ? <>
        {reservationsQuery.isError && <ErrorState description={getApiErrorPresentation(reservationsQuery.error)} onRetry={() => void reservationsQuery.refetch()} />}
        {reservationsQuery.isFetching && <Alert className="mb-4">{showingPrevious
          ? `Cargando ${formatWeek(range.from, range.to)}. Se muestra todavía ${formatWeek(loadedRange.from, loadedRange.to)}; las acciones están deshabilitadas.`
          : 'Actualizando las reservas de esta semana…'}</Alert>}
        {(servicesQuery.isError || locationsQuery.isError) && <Alert tone="warning" className="mb-4">No se pudieron cargar todos los nombres de servicios o sedes. Las reservas siguen disponibles.
          {servicesQuery.isError && <Button variant="ghost" onClick={() => void servicesQuery.refetch()}>Reintentar servicios</Button>}
          {locationsQuery.isError && <Button variant="ghost" onClick={() => void locationsQuery.refetch()}>Reintentar sedes</Button>}
        </Alert>}
        <section aria-label="Resumen de la semana mostrada" className="grid grid-cols-2 sm:grid-cols-3 xl:grid-cols-5 gap-3 mb-5">
          <div className="nf-panel p-4"><p className="text-xs text-muted">Total semanal</p><p className="text-2xl font-semibold">{reservations?.length ?? 0}</p></div>
          {(Object.keys(reservationStatusLabels) as ReservationDto['status'][]).map(value => <div className="nf-panel p-4" key={value}><p className="text-xs text-muted">{reservationStatusLabels[value]}</p><p className="text-2xl font-semibold">{summary[value]}</p></div>)}
        </section>
        <section className="nf-panel overflow-hidden" aria-label="Reservas de la semana mostrada" aria-busy={reservationsQuery.isFetching}>
          <div className="border-b border-line p-4 flex flex-wrap gap-4 items-end">
            <div className="flex gap-1 rounded-xl bg-surface-soft p-1" role="group" aria-label="Vista de la agenda">
              <Button variant={viewMode === 'week' ? 'primary' : 'ghost'} aria-pressed={viewMode === 'week'} onClick={() => setViewMode('week')}><Calendar aria-hidden="true" className="w-4 h-4" />Semana</Button>
              <Button variant={viewMode === 'list' ? 'primary' : 'ghost'} aria-pressed={viewMode === 'list'} onClick={() => setViewMode('list')}><List aria-hidden="true" className="w-4 h-4" />Lista</Button>
            </div>
            <FormField label="Estado" className="w-full sm:w-44"><Select value={status} onChange={event => setStatus(event.target.value as StatusFilter)}>{statusFilters.map(filter => <option value={filter.value} key={filter.value}>{filter.label}</option>)}</Select></FormField>
            <FormField label="Buscar en esta semana" helperText="Cliente o servicio; solo reservas de la semana mostrada." className="w-full sm:flex-1 sm:min-w-52">
              {control => <div className="relative"><Search aria-hidden="true" className="absolute left-3 top-3 w-4 h-4 text-muted" /><Input {...control} type="search" value={search} onChange={event => setSearch(event.target.value)} placeholder="Nombre del cliente o servicio" className="pl-9" /></div>}
            </FormField>
            <p className="text-xs text-muted" role="status">{visibleReservations.length} de {reservations?.length ?? 0} reservas</p>
          </div>
          {viewMode === 'week' ? <WeeklyAgenda {...view} reservations={visibleReservations} days={loadedRange.days} selectedDay={selectedDay} today={today} canCreate={canCreate} onSelectDay={setChosenDay} onCreate={setCreateDate} /> : <ReservationList {...view} reservations={visibleReservations} />}
        </section>
      </> : null}
    </>

    <ConfirmDialog isOpen={confirmDialog !== null} title={confirmDialog?.action === 'cancel' ? '¿Cancelar reserva?' : '¿Completar reserva?'} description={confirmDialog?.action === 'cancel' ? 'El cliente perderá su espacio agendado.' : 'Esta acción marcará la cita como finalizada.'} destructive={confirmDialog?.action === 'cancel'} confirmLabel={confirmDialog?.action === 'cancel' ? 'Sí, cancelar' : 'Sí, completar'} cancelLabel="No, volver" isLoading={mutating} confirmDisabled={!can('RESERVATIONS', confirmDialog?.action === 'cancel' ? 'CANCEL' : 'COMPLETE')} onClose={() => { if (!mutating) setConfirmDialog(null); }} onConfirm={executeAction} />
    <CreateReservationModal isOpen={createDate !== null && can('RESERVATIONS', 'CREATE')} initialDate={createDate ?? undefined} onClose={() => setCreateDate(null)} onSuccess={reservation => { void refresh(reservation); toast.success('Reserva creada exitosamente.'); }} locations={canReadLocations ? locationsQuery.data ?? [] : []} services={canReadServices ? servicesQuery.data ?? [] : []} timeZone={timeZone} />
    <EditReservationModal isOpen={editingRes !== null && can('RESERVATIONS', 'UPDATE')} onClose={() => setEditingRes(null)} onSuccess={reservation => { void refresh(reservation); toast.success('Reserva reprogramada exitosamente.'); }} reservation={editingRes} timeZone={timeZone} />
  </div>;
};
