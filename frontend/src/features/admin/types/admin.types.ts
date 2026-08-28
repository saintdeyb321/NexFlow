export interface ProvisionWorkspaceRequest {
  email: string;
  firstName?: string;
  lastName?: string;
  workspaceName: string;
  templateCode?: string;
  customModules?: string[]; 
  expiresAt: string;
  maxLocations: number;
}

export interface WorkspaceSummaryDto {
  id: string;
  name: string;
  status: number;
  ownerEmail: string;
  createdAt: string;
}