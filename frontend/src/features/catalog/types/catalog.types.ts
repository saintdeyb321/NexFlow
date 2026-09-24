export interface CatalogCategoryDto {
  id?: string;
  name: string;
  description?: string | null;
  scope: 'PRODUCT' | 'SERVICE' | 'SHARED';
  isActive: boolean;
  displayOrder: number;
}

export interface BusinessOfferingDto {
  id?: string;
  categoryId: string;
  name: string;
  description?: string | null;
  priceMinorUnits: number;
  currency: string;
  isActive: boolean;
  locationScope: 'ALL' | 'SPECIFIC';
  locationIds: string[];
  imageUrl?: string | null;
  metadata?: Record<string, unknown>;
}

export interface ProductDto extends BusinessOfferingDto {
  type: 'PRODUCT';
}