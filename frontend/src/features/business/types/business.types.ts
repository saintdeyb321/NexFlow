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
  id?: string; 
  name: string;
  address: string;
  reference?: string;  // 🔥 CORRECCIÓN: Ahora es opcional en el front
  mapUrl?: string;     // 🔥 NUEVO: Link de Google Maps
  isMain: boolean;
}

export interface BusinessHoursDto {
  dayOfWeek: number; 
  openTime: string; 
  closeTime: string; 
  isClosed: boolean; 
}