export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
}
export interface ServiceDto {
  id: string; // Usamos string para los Guid en frontend
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
  id: string;
  name: string;
  address: string;
  reference: string;
  isMain: boolean;
}

export interface BusinessHoursDto {
  dayOfWeek: number; // 0 = Domingo, 1 = Lunes...
  openTime: string; // "08:00"
  closeTime: string; // "18:00"
  isClosed: boolean; // true si está cerrado ese día
}
