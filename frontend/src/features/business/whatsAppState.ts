import type { ConnectionStatus, WhatsAppStatusResponse } from './types/business.types';

export const whatsappStatus = (data: WhatsAppStatusResponse | undefined, hasError: boolean, now: number): ConnectionStatus => {
  if (hasError) return 'UNAVAILABLE';
  if (data?.status === 'QR_AVAILABLE' && data.qrExpiresAt && Date.parse(data.qrExpiresAt) <= now) return 'QR_EXPIRED';
  return data?.status ?? 'DISCONNECTED';
};

export const whatsappPollInterval = (data: WhatsAppStatusResponse | undefined, visible: boolean, now: number, until: number): number | false => {
  if (!visible || !data) return false;
  if (data.status === 'QR_AVAILABLE' && data.qrExpiresAt && now < Date.parse(data.qrExpiresAt)) return 5000;
  if (now >= until) return false;
  return data.status === 'CONNECTING' || data.status === 'RECONNECTING' ? 5000 : false;
};

export const canPairWhatsApp = (data: WhatsAppStatusResponse | undefined, canConfigure: boolean, processing: boolean, status: ConnectionStatus): boolean =>
  Boolean(canConfigure && !processing && data?.canConnect && !data.isLinked && !data.requiresLogout
    && status !== 'UNAVAILABLE' && status !== 'QR_AVAILABLE' && status !== 'CONNECTED');
