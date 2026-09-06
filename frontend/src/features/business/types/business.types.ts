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
  description?: string | null;
  category?: string | null;
  // 🔥 SPRINT 20: Tipado numérico estricto (Minor Units)
  priceMinorUnits: number; 
  currency: string;
  durationInMinutes: number;
  requiresReservation: boolean;
  isActive: boolean;
  availableAtLocations?: string[] | null;
}

export interface FaqDto {
  id?: string;
  question: string;
  answer: string;
  category?: string | null;
  isActive: boolean; // En C# el default es true, pero no es opcional
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