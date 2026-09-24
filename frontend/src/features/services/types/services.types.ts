import type { BusinessOfferingDto } from '../../catalog/types/catalog.types';

export interface ServiceDto extends BusinessOfferingDto {
  type: 'SERVICE';
  durationInMinutes?: number | null;
  requiresReservation?: boolean;
}