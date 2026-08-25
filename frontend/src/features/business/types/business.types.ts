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
  price?: number;
  currency?: string;
  durationInMinutes: number;
  requiresReservation: boolean;
  isActive: boolean;
  availableAtLocations?: string[]; // IDs de las sedes
}

export interface FaqDto {
  id: string;
  question: string;
  answer: string;
  category: string;
}

export interface LocationDto {
  id?: string; // Opcional para la creación
  name: string;
  address: string;
  reference: string;
  isMain: boolean;
}

export interface BusinessHoursDto {
  dayOfWeek: number; 
  openTime: string; 
  closeTime: string; 
  isClosed: boolean; 
}