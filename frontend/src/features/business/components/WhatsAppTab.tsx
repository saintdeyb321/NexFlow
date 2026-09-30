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

interface WhatsAppTabProps {
  showMessage: (text: string, type: 'success' | 'error') => void;
}

export const WhatsAppTab = ({ showMessage }: WhatsAppTabProps) => {
  const { can } = usePermissions();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const queryClient = useQueryClient();
  const isPageVisible = usePageVisible();
  const queryKey = queryKeys.business.whatsapp(workspaceId);
  const [qrExpired, setQrExpired] = useState(false);
  const [timeLeft, setTimeLeft] = useState(30);
  const [showDisconnectConfirm, setShowDisconnectConfirm] = useState(false);
  const { data, isLoading, error } = useQuery({
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
        showMessage('Código QR generado. Tienes 30 segundos para escanearlo.', 'success');
      } else if ('status' in response && response.status === 'CONNECTED') {
        queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, { status: 'CONNECTED' });
        showMessage('El dispositivo ya estaba conectado.', 'success');
      }
    },
    onError: error => showMessage(getApiErrorPresentation(error), 'error'),
  });
  const disconnectMutation = useSessionMutation({
    mutationFn: disconnectWhatsApp,
    onSuccess: () => {
      queryClient.setQueryData<WhatsAppStatusResponse>(queryKey, { status: 'DISCONNECTED' });
      connectMutation.reset();
      setQrExpired(false);
      showMessage('WhatsApp desconectado exitosamente.', 'success');
    },
    onError: error => showMessage(getApiErrorPresentation(error), 'error'),
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
    setShowDisconnectConfirm(false);
    disconnectMutation.mutate();
  };

  if (isLoading) {
    return <div className="p-8 text-center text-gray-500 animate-pulse">Consultando estado de conexión...</div>;
  }

  return (
    <div className="bg-white p-6 rounded-xl border border-gray-200 shadow-sm max-w-2xl">
      <div className="flex items-center mb-6">
        <div className={`p-3 rounded-full mr-4 ${status === 'CONNECTED' ? 'bg-green-100 text-green-600' : 'bg-gray-100 text-gray-600'}`}>
          <MessageCircle className="w-8 h-8" />
        </div>
        <div>
          <h2 className="text-xl font-bold text-gray-800">Conexión a WhatsApp</h2>
          <p className="text-sm text-gray-500">
            Vincula tu número de empresa para que el asistente de NexFlow empiece a atender a tus clientes.
          </p>
        </div>
      </div>

      {error && <p role="alert">{getApiErrorPresentation(error)}</p>}
      <div className="border border-gray-100 rounded-lg p-6 bg-gray-50">
        
        {status === 'DISCONNECTED' || status === 'ERROR' || status === 'QR_EXPIRED' ? (
          <div className="flex flex-col items-center justify-center text-center">
            <QrCode className="w-16 h-16 text-gray-400 mb-4" />
            <h3 className="text-lg font-medium text-gray-900 mb-2">
              {status === 'QR_EXPIRED' ? 'El código QR ha expirado' : 'Dispositivo no vinculado'}
            </h3>
            <p className="text-gray-500 text-sm mb-6 max-w-md">
              {status === 'QR_EXPIRED' 
                ? 'Por seguridad, el código QR solo es válido por 30 segundos. Genera uno nuevo para intentar otra vez.'
                : 'Haz clic en conectar para generar un código QR. Deberás escanearlo desde la sección "Dispositivos Vinculados" en tu aplicación de WhatsApp.'}
            </p>
            <button 
              onClick={handleConnect} 
              disabled={isProcessing || !can('CONVERSATIONS', 'CONFIGURE')}
              className="bg-green-600 hover:bg-green-700 text-white font-medium py-2 px-6 rounded-lg transition-colors flex items-center disabled:opacity-50"
            >
              {isProcessing ? <RefreshCw className="w-5 h-5 mr-2 animate-spin" /> : <QrCode className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Generando QR...' : 'Generar Código QR'}
            </button>
          </div>
        ) : null}

        {status === 'QR_AVAILABLE' && qrCode ? (
          <div className="flex flex-col items-center justify-center text-center">
            <h3 className="text-lg font-medium text-gray-900 mb-2">Escanea el código QR</h3>
            <p className="text-gray-500 text-sm mb-4">Abre WhatsApp {'>'} Dispositivos Vinculados {'>'} Vincular un dispositivo.</p>
            
            <div className="bg-white p-4 rounded-lg shadow-sm border border-gray-200 mb-4 relative">
              <img 
                src={qrCode.startsWith('data:image') ? qrCode : `data:image/png;base64,${qrCode}`} 
                alt="WhatsApp QR Code" 
                className="w-64 h-64"
              />
              <div className="absolute bottom-2 right-2 bg-black/70 text-white text-xs font-bold px-2 py-1 rounded-md flex items-center">
                <Clock className="w-3 h-3 mr-1" /> {timeLeft}s
              </div>
            </div>
            
            <div className="flex items-center text-blue-600 text-sm font-medium animate-pulse">
              <RefreshCw className="w-4 h-4 mr-2 animate-spin" />
              Esperando conexión...
            </div>
          </div>
        ) : null}

        {status === 'CONNECTED' ? (
          <div className="flex flex-col items-center justify-center text-center py-4 relative">
            <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center mb-4">
              <ShieldCheck className="w-10 h-10 text-green-600" />
            </div>
            <h3 className="text-xl font-bold text-gray-900 mb-1">¡WhatsApp Conectado!</h3>
            <p className="text-green-600 font-medium text-sm flex items-center mb-6">
              <span className="w-2 h-2 bg-green-500 rounded-full mr-2 animate-pulse"></span>
              El asistente virtual está activo y respondiendo.
            </p>

            <button 
              onClick={() => setShowDisconnectConfirm(true)} 
              disabled={isProcessing || !can('CONVERSATIONS', 'CONFIGURE')}
              className="mt-2 bg-white border border-red-200 hover:bg-red-50 text-red-600 font-medium py-2 px-6 rounded-lg transition-colors flex items-center disabled:opacity-50"
            >
              {isProcessing ? <RefreshCw className="w-5 h-5 mr-2 animate-spin" /> : <PowerOff className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Desconectando...' : 'Desconectar Dispositivo'}
            </button>

            {/* 🔥 Modal flotante de desconexión sin bloquear la UI */}
            {showDisconnectConfirm && (
              <div className="absolute top-full mt-4 bg-white border border-gray-200 shadow-xl p-5 rounded-xl z-10 w-80 text-left">
                <h4 className="text-red-600 font-bold mb-2">¿Desconectar dispositivo?</h4>
                <p className="text-sm text-gray-600 mb-4">El bot dejará de funcionar y ya no responderá automáticamente a tus clientes.</p>
                <div className="flex justify-end gap-2">
                  <button onClick={() => setShowDisconnectConfirm(false)} className="px-3 py-1.5 bg-gray-100 text-gray-700 rounded-lg text-sm font-medium">Cancelar</button>
                  <button disabled={!can('CONVERSATIONS', 'CONFIGURE') || isProcessing} onClick={executeDisconnect} className="px-3 py-1.5 bg-red-600 text-white rounded-lg text-sm font-medium hover:bg-red-700">Sí, Desconectar</button>
                </div>
              </div>
            )}
          </div>
        ) : null}

      </div>
    </div>
  );
};
