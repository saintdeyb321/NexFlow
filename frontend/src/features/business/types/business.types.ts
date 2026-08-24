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
  durationInMinutes: number;
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