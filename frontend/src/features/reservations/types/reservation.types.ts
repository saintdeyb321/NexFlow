export interface CreateReservationRequest {
  locationId: string;
  serviceId: string;
  customerName: string; // 🔥 CORRECCIÓN: Ahora es obligatorio
  customerIdentifier: string;
  dateTime: string;
}

export interface ReservationDto {
  id: string;
  workspaceId: string; // Agregado para coincidir con C#
  locationId: string;
  serviceId: string;
  customerName: string;
  customerIdentifier: string;
  dateTime: string; // Ya no usa startTime
  status: string;
}