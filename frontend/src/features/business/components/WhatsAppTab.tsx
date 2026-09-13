import { useState, useEffect } from 'react';
import { QrCode, RefreshCw, PowerOff, ShieldCheck, MessageCircle } from 'lucide-react';
import { getWhatsAppStatus, connectWhatsApp, disconnectWhatsApp } from '../services/business.service';
import type { ConnectionStatus } from '../types/business.types';

interface WhatsAppTabProps {
  showMessage: (text: string, type: 'success' | 'error') => void;
}

export const WhatsAppTab = ({ showMessage }: WhatsAppTabProps) => {
  const [status, setStatus] = useState<ConnectionStatus>('DISCONNECTED');
  const [qrCode, setQrCode] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [isProcessing, setIsProcessing] = useState(false);

  const fetchStatus = async () => {
    try {
      const response = await getWhatsAppStatus();
      setStatus(response.status);
    } catch (error) {
      setStatus('ERROR');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchStatus();
    // Polling cada 5 segundos solo si está esperando escanear el QR
    const interval = setInterval(() => {
      if (status === 'QR_AVAILABLE' || status === 'CONNECTING') {
        fetchStatus();
      }
    }, 5000);
    return () => clearInterval(interval);
  }, [status]);

  const handleConnect = async () => {
    setIsProcessing(true);
    setQrCode(null);
    try {
      const response = await connectWhatsApp();
      if (response.qrBase64) {
        setQrCode(response.qrBase64);
        setStatus('QR_AVAILABLE');
        showMessage('Código QR generado. Escanéalo con tu WhatsApp.', 'success');
      }
    } catch (error) {
      showMessage('No se pudo generar el código QR. Intenta nuevamente.', 'error');
      setStatus('ERROR');
    } finally {
      setIsProcessing(false);
    }
  };

  const handleDisconnect = async () => {
    if (!window.confirm("¿Estás seguro de que deseas desconectar este dispositivo? El bot dejará de funcionar.")) return;
    
    setIsProcessing(true);
    try {
      await disconnectWhatsApp();
      setStatus('DISCONNECTED');
      setQrCode(null);
      showMessage('WhatsApp desconectado exitosamente.', 'success');
    } catch (error) {
      showMessage('Ocurrió un error al intentar desconectar.', 'error');
    } finally {
      setIsProcessing(false);
    }
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

      <div className="border border-gray-100 rounded-lg p-6 bg-gray-50">
        
        {/* ESTADO: DESCONECTADO */}
        {status === 'DISCONNECTED' || status === 'ERROR' ? (
          <div className="flex flex-col items-center justify-center text-center">
            <QrCode className="w-16 h-16 text-gray-400 mb-4" />
            <h3 className="text-lg font-medium text-gray-900 mb-2">Dispositivo no vinculado</h3>
            <p className="text-gray-500 text-sm mb-6 max-w-md">
              Haz clic en conectar para generar un código QR. Deberás escanearlo desde la sección "Dispositivos Vinculados" en tu aplicación de WhatsApp.
            </p>
            <button 
              onClick={handleConnect} 
              disabled={isProcessing}
              className="bg-green-600 hover:bg-green-700 text-white font-medium py-2 px-6 rounded-lg transition-colors flex items-center disabled:opacity-50"
            >
              {isProcessing ? <RefreshCw className="w-5 h-5 mr-2 animate-spin" /> : <QrCode className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Generando QR...' : 'Generar Código QR'}
            </button>
          </div>
        ) : null}

        {/* ESTADO: ESPERANDO ESCANEO (QR) */}
        {status === 'QR_AVAILABLE' && qrCode ? (
          <div className="flex flex-col items-center justify-center text-center">
            <h3 className="text-lg font-medium text-gray-900 mb-2">Escanea el código QR</h3>
            <p className="text-gray-500 text-sm mb-4">Abre WhatsApp en tu teléfono {'>'} Dispositivos Vinculados {'>'} Vincular un dispositivo.</p>
            
            <div className="bg-white p-4 rounded-lg shadow-sm border border-gray-200 mb-4">
              {/* Evolution API devuelve la cadena en Base64 cruda, si ya trae el data:image la usamos, sino se lo agregamos */}
              <img 
                src={qrCode.startsWith('data:image') ? qrCode : `data:image/png;base64,${qrCode}`} 
                alt="WhatsApp QR Code" 
                className="w-64 h-64"
              />
            </div>
            
            <div className="flex items-center text-blue-600 text-sm font-medium animate-pulse">
              <RefreshCw className="w-4 h-4 mr-2 animate-spin" />
              Esperando conexión...
            </div>
            
            <button onClick={handleDisconnect} className="mt-6 text-sm text-red-500 hover:text-red-700 font-medium">
              Cancelar
            </button>
          </div>
        ) : null}

        {/* ESTADO: CONECTADO */}
        {status === 'CONNECTED' ? (
          <div className="flex flex-col items-center justify-center text-center py-4">
            <div className="w-20 h-20 bg-green-100 rounded-full flex items-center justify-center mb-4">
              <ShieldCheck className="w-10 h-10 text-green-600" />
            </div>
            <h3 className="text-xl font-bold text-gray-900 mb-1">¡WhatsApp Conectado!</h3>
            <p className="text-green-600 font-medium text-sm flex items-center mb-6">
              <span className="w-2 h-2 bg-green-500 rounded-full mr-2 animate-pulse"></span>
              El asistente virtual está activo y respondiendo.
            </p>

            <button 
              onClick={handleDisconnect} 
              disabled={isProcessing}
              className="mt-2 bg-white border border-red-200 hover:bg-red-50 text-red-600 font-medium py-2 px-6 rounded-lg transition-colors flex items-center disabled:opacity-50"
            >
              {isProcessing ? <RefreshCw className="w-5 h-5 mr-2 animate-spin" /> : <PowerOff className="w-5 h-5 mr-2" />}
              {isProcessing ? 'Desconectando...' : 'Desconectar Dispositivo'}
            </button>
          </div>
        ) : null}

      </div>
    </div>
  );
};