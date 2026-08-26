export interface ProvisionWorkspaceRequest {
  email: string;
  firstName: string;
  lastName: string;
  workspaceName: string;
  templateCode?: string; // 🔥 Exactamente como lo pide C#
  customModules?: string[]; 
  expiresAt: string;
  maxLocations: number; // 🔥 Confirmado que C# lo espera
}

export interface WorkspaceSummaryDto {
  id: string;
  name: string;
  status: number;
  ownerEmail: string;
  createdAt: string;
}