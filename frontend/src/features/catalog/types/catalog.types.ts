import type { BusinessOfferingDto, BusinessCategoryDto } from '../../shared/types/business-offering.types';

// Alias para mantener la semántica limpia dentro del módulo de Catálogo
export type ProductCategoryDto = BusinessCategoryDto;

export interface ProductDto extends BusinessOfferingDto {
  type: 'PRODUCT';
}