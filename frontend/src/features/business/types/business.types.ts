export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
}

export interface ServiceDto {
  id: string;
  name: string;
  description?: string;
  category?: string;
  // 🔥 SPRINT 5: Migrado a unidades menores (ej: S/ 10.50 se envía como 1050)
  priceMinorUnits?: number; 
  currency?: string;
  durationInMinutes: number;
  requiresReservation: boolean;
  isActive: boolean;
  availableAtLocations?: string[];
}

export interface FaqDto {
  id: string;
  question: string;
  answer: string;
  category: string;
}

export interface LocationDto {
  id?: string; 
  name: string;
  address: string;
  reference?: string;
  mapUrl?: string;
  isMain: boolean;
}

export interface BusinessHoursDto {
  dayOfWeek: number; 
  openTime: string; 
  closeTime: string; 
  isClosed: boolean; 
}