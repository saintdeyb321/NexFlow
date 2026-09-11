export interface CatalogCategoryDto {
  id?: string;
  name: string;
  description?: string | null;
  scope: 'PRODUCT' | 'SERVICE' | 'SHARED'; // 🔥 SPRINT 2 y 6: Agregado el ámbito de negocio
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
  availableAtLocations?: string[];
  
  // Campos exclusivos de Servicios (Nulos para Productos)
  durationInMinutes?: number | null;
  requiresReservation?: boolean;
  
  // Para el Sprint 7 (Multimedia)
  imageUrl?: string | null;
  metadata?: Record<string, unknown>;
}