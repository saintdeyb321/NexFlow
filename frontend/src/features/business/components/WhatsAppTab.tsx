import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/useToast';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { Alert, LoadingState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { usePermissions } from '../../../core/auth/permissions';
import { useState, useEffect, useMemo, useRef } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { QrCode, RefreshCw, PowerOff, ShieldCheck, MessageCircle } from 'lucide-react';
import { getWhatsAppStatus, connectWhatsApp, disconnectWhatsApp } from '../services/business.service';
import { canPairWhatsApp, whatsappDeadlineReview, whatsappPairingDeadline, whatsappPollInterval, whatsappStatus } from '../whatsAppState';
import type { ConnectionStatus, WhatsAppStatusResponse } from '../types/business.types';

const labels: Record<ConnectionStatus, string> = {
  CONNECTED: 'Conectado', DISCONNECTED: 'Sin vinculación', CONNECTING: 'Conectando',
  QR_AVAILABLE: 'QR listo para vincular', QR_EXPIRED: 'QR expirado', RECONNECTING: 'Reconectando sesión',
  UNAVAILABLE: 'Conexión no disponible', DISCONNECT_PENDING: 'Desconexión por confirmar',
};
const descriptions: Record<ConnectionStatus, string> = {
  CONNECTED: 'WhatsApp está vinculado a este negocio. La sesión se conserva hasta que confirmes una desconexión.',
  DISCONNECTED: 'No hay una sesión vinculada. Puedes conectar el número que usará tu negocio.',
  CONNECTING: 'La vinculación está en curso. Revisa el estado para continuar con la misma sesión.',
  QR_AVAILABLE: 'Escanea el código desde el teléfono que deseas vincular antes de que expire.',
  QR_EXPIRED: 'El código venció. Revisa el estado antes de generar otro si aún no completaste la vinculación.',
  RECONNECTING: 'La sesión sigue vinculada. Puedes revisar o reintentar la conexión de esa misma sesión.',
  UNAVAILABLE: 'No se pudo comprobar la conexión. Revisa el estado antes de intentar otra operación.',
  DISCONNECT_PENDING: 'La desconexión aún no está confirmada. Revisa el estado antes de vincular otro número.',
};

export const WhatsAppTab = () => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  return <WhatsAppConnectionPanel key={workspaceId ?? 'none'} workspaceId={workspaceId} />;
};

const WhatsAppConnectionPanel = ({ workspaceId }: { workspaceId: string | undefined }) => {
  const toast = useToast();
  const { can } = usePermissions();
  const canRead = can('CONVERSATIONS', 'READ');
  const canConfigure = can('CONVERSATIONS', 'CONFIGURE');
  const queryClient = useQueryClient();
  const visible = usePageVisible();
  const queryKey = useMemo(() => queryKeys.business.whatsapp(workspaceId), [workspaceId]);
  const [now, setNow] = useState(Date.now);
  const [pollUntil, setPollUntil] = useState(0);
  const [showDisconnectConfirm, setShowDisconnectConfirm] = useState(false);
  const forceRefresh = useRef(false);
  const actionInFlight = useRef(false);
  const { data, isLoading, error, refetch, isFetching } = useQuery({
    ...queryPolicies.dynamic, queryKey,
    queryFn: ({ signal }) => {
      const refresh = forceRefresh.current;
      forceRefresh.current = false;
      return getWhatsAppStatus(signal, refresh);
    },
    enabled: visible && !!workspaceId && canRead,
    refetchOnWindowFocus: false,
    retry: false,
    refetchInterval: query => query.state.error ? false : whatsappPollInterval(query.state.data, visible, Date.now(), pollUntil),
  });
  const reviewStatus = () => {
    if (!visible || !workspaceId || !canRead) return;
    forceRefresh.current = true;
    void refetch({ cancelRefetch: false });
  };
  const saveStatus = (response: WhatsAppStatusResponse) => queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, response);
  const connectMutation = useSessionMutation({
    mutationFn: connectWhatsApp,
    onSuccess: response => {
      saveStatus(response);
      setPollUntil(whatsappPairingDeadline(response, Date.now()));
      toast.success(response.status === 'CONNECTED' ? 'WhatsApp ya está vinculado.'
        : response.qrBase64 ? 'Escanea el QR antes de su expiración.' : 'La sesión se conserva. Consulta su reconexión.');
    },
    onError: failure => { toast.toastApiError(failure); void queryClient.invalidateQueries({ queryKey, exact: true }); },
  });
  const disconnectMutation = useSessionMutation({
    mutationFn: disconnectWhatsApp,
    onSuccess: response => {
      saveStatus(response);
      setPollUntil(0);
      setShowDisconnectConfirm(false);
      connectMutation.reset();
      if (response.status === 'DISCONNECTED' && !response.isLinked && !response.requiresLogout)
        toast.success('Desconexión confirmada.');
      else toast.warning('No se pudo confirmar la desconexión; revisa el estado.');
    },
    onError: failure => { toast.toastApiError(failure); void queryClient.invalidateQueries({ queryKey, exact: true }); },
  });
  const processing = connectMutation.isPending || disconnectMutation.isPending;
  // Guard reentrant UI clicks before TanStack's pending state has rendered.
  // The existing callbacks still own status, errors, cache and polling behavior.
  const runAction = (request: () => Promise<WhatsAppStatusResponse>) => {
    if (actionInFlight.current || processing) return;
    actionInFlight.current = true;
    void request().catch(() => undefined).finally(() => { actionInFlight.current = false; });
  };
  const status = whatsappStatus(data, Boolean(error), now);
  const pairingPending = !error && (data?.status === 'QR_AVAILABLE' || data?.status === 'CONNECTING' || data?.status === 'RECONNECTING');
  const activePollUntil = pairingPending ? pollUntil : 0;
  const qr = canConfigure && status === 'QR_AVAILABLE' && !data?.isLinked ? data?.qrBase64 : null;
  const expiresAt = status === 'QR_AVAILABLE' && data?.qrExpiresAt ? Date.parse(data.qrExpiresAt) : 0;
  const seconds = Math.max(0, Math.ceil((expiresAt - now) / 1000));
  useEffect(() => {
    if (!visible || (!expiresAt && !activePollUntil)) return;
    const timer = setInterval(() => {
      const current = Date.now();
      setNow(current);
      if (current >= Math.max(expiresAt, activePollUntil)) clearInterval(timer);
    }, 1000);
    return () => clearInterval(timer);
  }, [visible, expiresAt, activePollUntil]);
  // Stop the timer/poll budget after its bounded deadline; no permanent provider polling.
  useEffect(() => {
    if (!canRead || error || !whatsappDeadlineReview(data, visible, activePollUntil)) return;
    const timer = setTimeout(() => {
      setPollUntil(0);
      forceRefresh.current = true;
      void refetch({ cancelRefetch: false });
    }, Math.max(0, activePollUntil - Date.now()));
    return () => clearTimeout(timer);
  }, [canRead, activePollUntil, data, error, visible, refetch]);

  if (!canRead) return <ErrorState description="No tienes permiso para consultar la conexión de WhatsApp." />;
  if (isLoading) return <LoadingState title="Consultando conexión de WhatsApp..." />;
  return <section className="nf-panel nf-settings-panel overflow-hidden" aria-label="Conexión a WhatsApp">
    <ConfirmDialog isOpen={showDisconnectConfirm} title="¿Cerrar la sesión de WhatsApp?"
      description="Esta acción cierra la vinculación actual y detiene la atención por WhatsApp. Solo después de la confirmación podrás vincular otro número. Si Evolution falla, la vinculación seguirá reservada."
      destructive confirmLabel="Confirmar desconexión" isLoading={disconnectMutation.isPending}
      confirmDisabled={!canConfigure || processing} onClose={() => setShowDisconnectConfirm(false)}
      onConfirm={() => { if (canConfigure && !processing) runAction(() => disconnectMutation.mutateAsync()); }}>
      {disconnectMutation.isError && <Alert tone="warning">No se pudo confirmar la desconexión; revisa el estado. {getApiErrorPresentation(disconnectMutation.error)}</Alert>}
    </ConfirmDialog>
    <div className="nf-settings-panel-header">
      <div className="flex items-start gap-3 min-w-0"><MessageCircle aria-hidden="true" className="w-6 h-6 shrink-0 text-primary mt-1" />
      <div className="min-w-0"><h2 className="text-lg font-semibold">Conexión a WhatsApp</h2>
        <p className="text-sm text-muted">Una sesión exclusiva para este negocio. Cambiar de número requiere cerrar la sesión actual.</p></div>
      </div>
    </div>
    <div className="nf-settings-panel-body space-y-5">
    <div aria-live="polite" className="rounded-xl border border-line bg-surface-soft p-4 space-y-3" data-whatsapp-state={status}>
      <p className="text-sm font-medium">Estado de la conexión</p>
      <StatusBadge className="max-w-full whitespace-normal!" label={labels[status]} tone={status === 'CONNECTED' ? 'success' : status === 'UNAVAILABLE' || status === 'DISCONNECT_PENDING' ? 'warning' : 'neutral'} />
      <p className="text-sm text-muted leading-relaxed">{descriptions[status]}</p>
    </div>
    {error && <ErrorState description={getApiErrorPresentation(error)} onRetry={reviewStatus} />}
    {data?.message && <Alert tone="neutral">{data.message}</Alert>}
    {disconnectMutation.isError && !showDisconnectConfirm && <Alert tone="warning">No se pudo confirmar la desconexión; revisa el estado. {getApiErrorPresentation(disconnectMutation.error)}</Alert>}
    {status === 'UNAVAILABLE' && <Alert tone="warning">No se ha liberado la vinculación. Revisa la conexión antes de intentar otra operación.</Alert>}
    {data?.isLinked && <div className="flex items-start gap-2 text-sm"><ShieldCheck aria-hidden="true" className="w-5 h-5 shrink-0 text-primary" /><p>La sesión sigue asignada a este negocio. No se permite vincular otro número.</p></div>}
    {qr && <div className="rounded-xl border border-line p-4 space-y-4" aria-label="Instrucciones de vinculación">
      <div><h3 className="font-semibold">Vincula tu teléfono</h3><ol className="list-decimal pl-5 mt-2 space-y-1 text-sm text-muted"><li>Abre WhatsApp en tu teléfono.</li><li>Entra en Dispositivos vinculados.</li><li>Elige Vincular un dispositivo y escanea este código.</li></ol></div>
      <img src={qr.startsWith('data:image') ? qr : `data:image/png;base64,${qr}`} alt="Código QR para vincular WhatsApp" className="block mx-auto w-full max-w-64 aspect-square object-contain bg-surface p-3 border border-line rounded-xl" />
      <p className="text-sm text-muted text-center">El QR expira en {seconds} segundos.</p>
    </div>}
    {status === 'QR_EXPIRED' && <Alert tone="warning">El QR expiró. Revisa el estado y genera otro si no completaste la vinculación.</Alert>}
    {!canConfigure && <Alert>Solo lectura. Necesitas permiso de configuración para vincular o desconectar WhatsApp.</Alert>}
    <div className="nf-settings-actions justify-start!">
      {!data?.isLinked && !data?.requiresLogout && <Button variant="primary" isLoading={connectMutation.isPending}
        disabled={!canPairWhatsApp(data, canConfigure, processing, status)}
        onClick={() => { if (canPairWhatsApp(data, canConfigure, processing, status)) runAction(() => connectMutation.mutateAsync()); }}>
        <QrCode aria-hidden="true" className="w-4 h-4 mr-2" />{status === 'QR_EXPIRED' ? 'Generar otro QR' : 'Conectar WhatsApp'}
      </Button>}
      <Button variant="secondary" disabled={!workspaceId || processing || isFetching} isLoading={isFetching} onClick={reviewStatus}>
        <RefreshCw aria-hidden="true" className="w-4 h-4 mr-2" />Revisar estado
      </Button>
      {data?.isLinked && status === 'RECONNECTING' && <Button variant="secondary" disabled={!canConfigure || processing} isLoading={connectMutation.isPending}
        onClick={() => { if (canConfigure && !processing) runAction(() => connectMutation.mutateAsync()); }}>Reintentar conexión de esta sesión</Button>}
      {(data?.requiresLogout || data?.isLinked || qr) && <Button variant="secondary" disabled={!canConfigure || processing}
        onClick={() => setShowDisconnectConfirm(true)}><PowerOff aria-hidden="true" className="w-4 h-4 mr-2" />
        {status === 'DISCONNECT_PENDING' ? 'Reintentar desconexión' : 'Desconectar explícitamente'}</Button>}
    </div>
    </div>
  </section>;
};
