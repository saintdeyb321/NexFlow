export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
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

// WHATSAPP (Evolution API) - SPRINT 5
export type ConnectionStatus = 'DISCONNECTED' | 'CONNECTING' | 'QR_AVAILABLE' | 'CONNECTED' | 'ERROR';

export interface WhatsAppStatusResponse {
  status: ConnectionStatus;
}

export interface WhatsAppConnectResponse {
  qrBase64: string;
}