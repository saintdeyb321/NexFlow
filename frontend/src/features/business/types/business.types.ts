export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
}

export interface CatalogCategoryDto {
  id?: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  displayOrder: number;
}

export interface ServiceDto {
  id?: string;
  categoryId: string; // 🔥 NUEVO
  type: 'SERVICE';    // 🔥 NUEVO
  name: string;
  description?: string | null;
  priceMinorUnits: number;
  currency: string;
  isActive: boolean;
  durationInMinutes: number;
  requiresReservation: boolean;
  availableAtLocations?: string[];
  imageUrl?: string | null;
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