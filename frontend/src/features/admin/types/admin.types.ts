export interface ProvisionWorkspaceRequest {
  email: string;
  firstName: string;
  lastName: string;
  workspaceName: string;
  templateId?: string; // Opcional: Si se envía, usa la plantilla.
  customModules?: string[]; // Opcional: Si no hay plantilla, usa estos módulos (Ej: ['FAQ', 'RESERVATIONS']).
  expiresAt: string;
}

export interface WorkspaceSummaryDto {
  id: string;
  name: string;
  status: number;
  ownerEmail: string;
  createdAt: string;
}

