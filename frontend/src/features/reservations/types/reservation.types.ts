export interface ReservationDto {
  id: string;
  workspaceId: string;
  locationId: string;
  serviceId: string;
  customerIdentifier: string; 
  startTime: string; // ISO Date
  status: string; 
}

// NUEVO: Petición para agendar manualmente
export interface CreateReservationRequest {
  locationId: string;
  serviceId: string;
  customerIdentifier: string;
  dateTime: string; // ISO Date
}