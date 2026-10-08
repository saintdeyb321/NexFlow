export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
  timeZone: string; 
}

export interface FaqDto {
  id?: string;
  question: string;
  answer: string;
  category?: string | null;
  isActive: boolean;
}

export interface LocationDto {
  id?: string; 
  name: string;
  address: string;
  reference?: string | null;
  mapUrl?: string | null;
  isMain: boolean;
}

export interface BusinessHoursDto {
  dayOfWeek: number; 
  openTime: string; 
  closeTime: string; 
  isClosed: boolean; 
}

// WHATSAPP (Evolution API)
export type ConnectionStatus = 'DISCONNECTED' | 'CONNECTING' | 'QR_AVAILABLE' | 'QR_EXPIRED' | 'CONNECTED' | 'RECONNECTING' | 'UNAVAILABLE' | 'DISCONNECT_PENDING';

export interface WhatsAppStatusResponse {
  status: ConnectionStatus;
  isLinked: boolean;
  canConnect: boolean;
  requiresLogout: boolean;
  qrBase64: string | null;
  qrExpiresAt: string | null;
  message: string | null;
}

export type WhatsAppConnectResponse = WhatsAppStatusResponse;
