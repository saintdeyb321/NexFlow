import { getStatusTone } from '../../../components/ui/statusPresentation';
import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/Toast';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, ErrorState, StatusBadge } from '../../../components/ui/Feedback';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { usePermissions } from '../../../core/auth/permissions';
import { useState, useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { QrCode, RefreshCw, PowerOff, ShieldCheck, MessageCircle, Clock } from 'lucide-react';
import { getWhatsAppStatus, connectWhatsApp, disconnectWhatsApp } from '../services/business.service';
import type { ConnectionStatus, WhatsAppStatusResponse } from '../types/business.types';

export const WhatsAppTab = () => {
  const toast = useToast();
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const queryClient = useQueryClient();
  const isPageVisible = usePageVisible();
  const queryKey = queryKeys.business.whatsapp(workspaceId);
  const [qrExpired, setQrExpired] = useState(false);
  const [timeLeft, setTimeLeft] = useState(30);
  const [showDisconnectConfirm, setShowDisconnectConfirm] = useState(false);
  const { data, isLoading, error, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey,
    queryFn: ({ signal }) => getWhatsAppStatus(signal),
    enabled: !!workspaceId && can('CONVERSATIONS', 'READ'),
    refetchInterval: query => isPageVisible && !qrExpired &&
      (query.state.data?.status === 'QR_AVAILABLE' || query.state.data?.status === 'CONNECTING') ? 5000 : false,
  });
  const connectMutation = useSessionMutation({
    mutationFn: async () => {
      const response = await connectWhatsApp();
      if (!('qrBase64' in response && response.qrBase64) && !('status' in response && response.status === 'CONNECTED'))
        throw new Error('Respuesta de conexión inválida.');
      return response;
    },
    onSuccess: response => {
      setQrExpired(false);
      if ('qrBase64' in response && response.qrBase64) {
        queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, { status: 'QR_AVAILABLE' });
        toast.success('Código QR generado. Tienes 30 segundos para escanearlo.');
      } else if ('status' in response && response.status === 'CONNECTED') {
        queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, { status: 'CONNECTED' });
        toast.success('El dispositivo ya estaba conectado.');
      }
    },
    onError: error => toast.toastApiError(error),
  });
  const disconnectMutation = useSessionMutation({
    mutationFn: disconnectWhatsApp,
    onSuccess: () => {
      queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, { status: 'DISCONNECTED' });
      connectMutation.reset();
      setQrExpired(false);
      toast.success('WhatsApp desconectado exitosamente.');
      setShowDisconnectConfirm(false);
    },
    onError: error => toast.toastApiError(error),
  });
  const qrCode = connectMutation.data && 'qrBase64' in connectMutation.data
    ? connectMutation.data.qrBase64 : null;
  const status: ConnectionStatus | 'QR_EXPIRED' = connectMutation.isPending ? 'CONNECTING'
    : data?.status === 'CONNECTED' ? 'CONNECTED'
    : qrExpired ? 'QR_EXPIRED'
    : error || connectMutation.isError ? 'ERROR'
    : data?.status ?? 'DISCONNECTED';
  const isProcessing = connectMutation.isPending || disconnectMutation.isPending;

  useEffect(() => {
    if (status !== 'QR_AVAILABLE' || !qrCode) return;
    setTimeLeft(30);
    const timer = setInterval(() => {
      setTimeLeft(previous => Math.max(0, previous - 1));
    }, 1000);
    return () => clearInterval(timer);
  }, [status, qrCode]);

  useEffect(() => {
    if (timeLeft === 0 && status === 'QR_AVAILABLE') setQrExpired(true);
  }, [timeLeft, status]);

  const handleConnect = () => {
    if (!can('CONVERSATIONS', 'CONFIGURE')) return;
    setQrExpired(false);
    connectMutation.mutate();
  };

  const executeDisconnect = () => {
    if (!can('CONVERSATIONS', 'CONFIGURE')) return;
    disconnectMutation.mutate();
  };

  if (isLoading) {
    return <LoadingState title="Consultando estado de conexión..." />;
  }

  return (
    <div className="nf-panel p-5 sm:p-6 max-w-3xl">
      <ConfirmDialog isOpen={showDisconnectConfirm} title="¿Desconectar dispositivo?" description="El bot dejará de funcionar y ya no responderá automáticamente a tus clientes." destructive confirmLabel="Sí, Desconectar" isLoading={isProcessing} confirmDisabled={!can('CONVERSATIONS', 'CONFIGURE')} onClose={() => setShowDisconnectConfirm(false)} onConfirm={executeDisconnect} />
      <div className="flex items-center mb-6">
        <div className={`p-3 rounded-full mr-4 ${status === 'CONNECTED' ? 'bg-green-100 text-green-600' : 'bg-gray-100 text-gray-600'}`}>
          <MessageCircle aria-hidden="true" className="w-8 h-8" />
        </div>
        <div>
          <h2 className="text-xl font-bold text-foreground">Conexión a WhatsApp</h2>
          <p className="text-sm text-muted">
            Vincula tu número de empresa para que el asistente de NexFlow empiece a atender a tus clientes.
          </p>
        </div>
      </div>

      <div className="mb-5"><StatusBadge tone={getStatusTone(status)} label={{ CONNECTED: 'Conectado', DISCONNECTED: 'Desconectado', CONNECTING: 'Conectando', ERROR: 'Error de conexión', QR_AVAILABLE: 'QR listo para vincular', QR_EXPIRED: 'QR expirado' }[status]} /></div>
      {error && <ErrorState description={getApiErrorPresentation(error)} onRetry={() => void refetch()} />}
      <div className="border border-line rounded-lg p-6 bg-surface-soft">

        {status === 'DISCONNECTED' || status === 'ERROR' || status === 'QR_EXPIRED' ? (
          <div className="flex flex-col items-center justify-center text-center">
            <QrCode aria-hidden="true" className="w-16 h-16 text-gray-400 mb-4" />
            <h3 className="text-lg font-medium text-foreground mb-2">
              {status === 'QR_EXPIRED' ? 'El código QR ha expirado' : 'Dispositivo no vinculado'}
            </h3>
            <p className="text-muted text-sm mb-6 max-w-md">
              {status === 'QR_EXPIRED'
                ? 'Por seguridad, el código QR solo es válido por 30 segundos. Genera uno nuevo para intentar otra vez.'
                : 'Haz clic en conectar para generar un código QR. Deberás escanearlo desde la sección "Dispositivos Vinculados" en tu aplicación de WhatsApp.'}
            </p>
            <Button variant="primary" isLoading={isProcessing}
              onClick={handleConnect}
              disabled={isProcessing || !can('CONVERSATIONS', 'CONFIGURE')}
              className="font-medium transition-colors flex items-center disabled:opacity-50"
            >
              {!isProcessing && <QrCode aria-hidden="true" className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Generando QR...' : 'Generar Código QR'}
            </Button>
          </div>
        ) : null}

        {status === 'QR_AVAILABLE' && qrCode ? (
          <div className="flex flex-col items-center justify-center text-center">
            <h3 className="text-lg font-medium text-foreground mb-2">Escanea el código QR</h3>
            <p className="text-muted text-sm mb-4">Abre WhatsApp {'>'} Dispositivos Vinculados {'>'} Vincular un dispositivo.</p>

            <div className="bg-surface p-3 rounded-xl border border-line mb-4 relative w-full max-w-72">
              <img
                src={qrCode.startsWith('data:image') ? qrCode : `data:image/png;base64,${qrCode}`}
                alt="WhatsApp QR Code"
                className="w-full max-w-64 aspect-square h-auto"
              />
              <div className="absolute bottom-2 right-2 bg-black/70 text-white text-xs font-bold px-2 py-1 rounded-md flex items-center">
                <Clock aria-hidden="true" className="w-3 h-3 mr-1" /> {timeLeft}s
              </div>
            </div>

            <div className="flex items-center text-primary text-sm font-medium animate-pulse">
              <RefreshCw aria-hidden="true" className="w-4 h-4 mr-2 animate-spin" />
              Esperando conexión...
            </div>
          </div>
        ) : null}

        {status === 'CONNECTED' ? (
          <div className="flex flex-col items-center justify-center text-center py-4 relative">
            <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center mb-4">
              <ShieldCheck aria-hidden="true" className="w-10 h-10 text-green-600" />
            </div>
            <h3 className="text-xl font-bold text-foreground mb-1">¡WhatsApp Conectado!</h3>
            <p className="text-green-600 font-medium text-sm flex items-center mb-6">
              <span className="w-2 h-2 bg-green-500 rounded-full mr-2 animate-pulse"></span>
              El asistente virtual está activo y respondiendo.
            </p>

            <Button variant="secondary" isLoading={isProcessing}
              onClick={() => setShowDisconnectConfirm(true)}
              disabled={isProcessing || !can('CONVERSATIONS', 'CONFIGURE')}
              className="mt-2 border font-medium transition-colors flex items-center disabled:opacity-50"
            >
              {!isProcessing && <PowerOff aria-hidden="true" className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Desconectando...' : 'Desconectar Dispositivo'}
            </Button>

          </div>
        ) : null}

      </div>
    </div>
  );
};
