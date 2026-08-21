export interface ReservationDto {
  id: string;
  workspaceId: string;
  locationId: string;
  serviceId: string;
  customerIdentifier: string; // Teléfono o WhatsApp
  startTime: string; // Fecha y hora (ISO)
  status: string; // "CONFIRMED", "CANCELLED", "PENDING"
}