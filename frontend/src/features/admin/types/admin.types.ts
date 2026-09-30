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
  status: 0 | 1 | 2 | 3 | 4 | 5;
  ownerEmail: string;
  createdAt: string;
}
