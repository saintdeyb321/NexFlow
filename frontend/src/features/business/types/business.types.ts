export interface BusinessProfile {
  commercialName: string;
  taxId: string; // RUC
  contactEmail: string;
  whatsAppNumber: string;
  description: string;
}

// 🔥 SPRINT 6: Eliminados CatalogCategoryDto y ServiceDto de aquí. 
// Ahora toda la aplicación consumirá el modelo unificado desde catalog.types.ts

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