import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/useToast';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { usePermissions } from '../../../core/auth/permissions';
import { useState, useEffect, useMemo } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { QrCode, RefreshCw, PowerOff, ShieldCheck, MessageCircle } from 'lucide-react';
import { getWhatsAppStatus, connectWhatsApp, disconnectWhatsApp } from '../services/business.service';
import { canPairWhatsApp, whatsappPollInterval, whatsappStatus } from '../whatsAppState';
import type { ConnectionStatus, WhatsAppStatusResponse } from '../types/business.types';

const labels: Record<ConnectionStatus, string> = {
  CONNECTED: 'Conectado', DISCONNECTED: 'Sin vinculación', CONNECTING: 'Conectando',
  QR_AVAILABLE: 'QR listo para vincular', QR_EXPIRED: 'QR expirado', RECONNECTING: 'Sesión vinculada, conexión interrumpida',
  UNAVAILABLE: 'Evolution no disponible', DISCONNECT_PENDING: 'Desconexión pendiente de confirmar',
};

export const WhatsAppTab = () => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  return <WhatsAppConnectionPanel key={workspaceId ?? 'none'} workspaceId={workspaceId} />;
};

const WhatsAppConnectionPanel = ({ workspaceId }: { workspaceId: string | undefined }) => {
  const toast = useToast();
  const { can } = usePermissions();
  const canConfigure = can('CONVERSATIONS', 'CONFIGURE');
  const queryClient = useQueryClient();
  const visible = usePageVisible();
  const queryKey = useMemo(() => queryKeys.business.whatsapp(workspaceId), [workspaceId]);
  const [now, setNow] = useState(Date.now);
  const [pollUntil, setPollUntil] = useState(0);
  const [showDisconnectConfirm, setShowDisconnectConfirm] = useState(false);
  const { data, isLoading, error, refetch, isFetching } = useQuery({
    ...queryPolicies.dynamic, queryKey,
    queryFn: ({ signal }) => getWhatsAppStatus(signal, true),
    enabled: !!workspaceId && can('CONVERSATIONS', 'READ'),
    refetchOnWindowFocus: false,
    retry: false,
    refetchInterval: query => whatsappPollInterval(query.state.data, visible, Date.now(), pollUntil),
  });
  const saveStatus = (response: WhatsAppStatusResponse) => queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, response);
  const connectMutation = useSessionMutation({
    mutationFn: connectWhatsApp,
    onSuccess: response => {
      saveStatus(response);
      setPollUntil(response.qrExpiresAt ? Date.parse(response.qrExpiresAt) : Date.now() + 60_000);
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
      toast.success('Desconexión confirmada. Ya puedes vincular otro número.');
    },
    onError: failure => { toast.toastApiError(failure); void queryClient.invalidateQueries({ queryKey, exact: true }); },
  });
  const processing = connectMutation.isPending || disconnectMutation.isPending;
  const status = whatsappStatus(data, Boolean(error), now);
  const qr = canConfigure && status === 'QR_AVAILABLE' && !data?.isLinked ? data?.qrBase64 : null;
  const expiresAt = data?.qrExpiresAt ? Date.parse(data.qrExpiresAt) : 0;
  const seconds = Math.max(0, Math.ceil((expiresAt - now) / 1000));
  useEffect(() => {
    if (!visible || (!expiresAt && !pollUntil)) return;
    const timer = setInterval(() => {
      const current = Date.now();
      setNow(current);
      if (current >= Math.max(expiresAt, pollUntil)) clearInterval(timer);
    }, 1000);
    return () => clearInterval(timer);
  }, [visible, expiresAt, pollUntil]);
  // Stop the timer/poll budget after its bounded deadline; no permanent provider polling.
  useEffect(() => {
    if (!pollUntil) return;
    const timer = setTimeout(() => { setPollUntil(0); void queryClient.invalidateQueries({ queryKey, exact: true }); }, Math.max(0, pollUntil - Date.now()));
    return () => clearTimeout(timer);
  }, [pollUntil, queryClient, queryKey]);

  if (isLoading) return <LoadingState title="Consultando conexión de WhatsApp..." />;
  return <div className="nf-panel p-5 sm:p-6 max-w-3xl">
    <ConfirmDialog isOpen={showDisconnectConfirm} title="¿Cerrar la sesión de WhatsApp?"
      description="Esta acción cierra la vinculación actual y detiene la atención por WhatsApp. Solo después de la confirmación podrás vincular otro número. Si Evolution falla, la vinculación seguirá reservada."
      destructive confirmLabel="Confirmar desconexión" isLoading={disconnectMutation.isPending}
      confirmDisabled={!canConfigure || processing} onClose={() => setShowDisconnectConfirm(false)}
      onConfirm={() => { if (canConfigure && !processing) disconnectMutation.mutate(); }} />
    <div className="flex items-center gap-3 mb-5">
      <MessageCircle aria-hidden="true" className="w-8 h-8 text-primary" />
      <div><h2 className="text-xl font-bold text-foreground">Conexión a WhatsApp</h2>
        <p className="text-sm text-muted">Una sesión exclusiva para este negocio. Cambiar de número requiere cerrar la sesión actual.</p></div>
    </div>
    <div aria-live="polite" className="mb-4">
      <StatusBadge label={labels[status]} tone={status === 'CONNECTED' ? 'success' : status === 'UNAVAILABLE' || status === 'DISCONNECT_PENDING' ? 'warning' : 'neutral'} />
    </div>
    {error && <ErrorState description={getApiErrorPresentation(error)} onRetry={() => void refetch()} />}
    {data?.message && <p role="status" className="text-sm text-muted mb-4">{data.message}</p>}
    {status === 'UNAVAILABLE' && <p className="text-sm text-muted mb-4">No se ha liberado la vinculación. Revisa la conexión antes de intentar otra operación.</p>}
    {data?.isLinked && <div className="flex items-center gap-2 text-sm mb-4"><ShieldCheck aria-hidden="true" className="w-5 h-5" />La sesión sigue asignada a este workspace. No se permite vincular otro número.</div>}
    {qr && <div className="flex flex-col items-center text-center gap-3 mb-5">
      <p>Abre WhatsApp → Dispositivos vinculados → Vincular un dispositivo.</p>
      <img src={qr.startsWith('data:image') ? qr : `data:image/png;base64,${qr}`} alt="Código QR para vincular WhatsApp" className="w-64 h-64 bg-white p-3 rounded-lg" />
      <p>El QR expira en {seconds} segundos.</p>
    </div>}
    {status === 'QR_EXPIRED' && <p className="text-sm text-muted mb-4">El QR expiró. Revisa el estado y genera otro si no completaste la vinculación.</p>}
    {!canConfigure && <p className="text-sm text-muted mb-4">Necesitas permiso de configuración para vincular o desconectar WhatsApp.</p>}
    <div className="flex flex-wrap gap-3">
      {!data?.isLinked && !data?.requiresLogout && <Button variant="primary" isLoading={connectMutation.isPending}
        disabled={!canPairWhatsApp(data, canConfigure, processing, status)}
        onClick={() => { if (canPairWhatsApp(data, canConfigure, processing, status)) connectMutation.mutate(); }}>
        <QrCode aria-hidden="true" className="w-4 h-4 mr-2" />{status === 'QR_EXPIRED' ? 'Generar otro QR' : 'Conectar WhatsApp'}
      </Button>}
      <Button variant="secondary" disabled={processing || isFetching} isLoading={isFetching} onClick={() => void refetch()}>
        <RefreshCw aria-hidden="true" className="w-4 h-4 mr-2" />Revisar estado
      </Button>
      {data?.isLinked && status === 'RECONNECTING' && <Button variant="secondary" disabled={!canConfigure || processing} isLoading={connectMutation.isPending}
        onClick={() => { if (canConfigure && !processing) connectMutation.mutate(); }}>Reintentar conexión de esta sesión</Button>}
      {(data?.requiresLogout || data?.isLinked || qr) && <Button variant="secondary" disabled={!canConfigure || processing}
        onClick={() => setShowDisconnectConfirm(true)}><PowerOff aria-hidden="true" className="w-4 h-4 mr-2" />
        {status === 'DISCONNECT_PENDING' ? 'Reintentar desconexión' : 'Desconectar explícitamente'}</Button>}
    </div>
  </div>;
};
