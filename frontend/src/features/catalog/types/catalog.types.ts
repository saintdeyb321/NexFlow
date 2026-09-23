export interface CatalogCategoryDto {
  id?: string;
  name: string;
  description?: string | null;
  scope: 'PRODUCT' | 'SERVICE' | 'SHARED';
  isActive: boolean;
  displayOrder: number;
}

export interface CatalogItemDto {
  id?: string;
  categoryId: string;
  type: 'PRODUCT' | 'SERVICE';
  name: string;
  description?: string | null;
  priceMinorUnits: number;
  currency: string;
  isActive: boolean;
  
  // 🔥 SPRINT 6: Contrato sincronizado con el Backend (Reemplaza availableAtLocations)
  locationScope: 'ALL' | 'SPECIFIC';
  locationIds: string[];
  
  // Campos exclusivos de Servicios
  durationInMinutes?: number | null;
  requiresReservation?: boolean;
  
  // Multimedia
  imageUrl?: string | null;
  metadata?: Record<string, unknown>;
}