import type { BusinessOfferingDto, BusinessCategoryDto } from '../../shared/types/business-offering.types';

export type ServiceCategoryDto = BusinessCategoryDto;

export interface ServiceDto extends BusinessOfferingDto {
  type: 'SERVICE';
  durationInMinutes?: number | null;
  requiresReservation?: boolean;
}