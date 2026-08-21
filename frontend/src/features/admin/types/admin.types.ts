export interface ProvisionWorkspaceRequest {
  email: string;
  firstName: string;
  lastName: string;
  workspaceName: string;
  templateName?: string; 
  customModules?: string[]; 
  expiresAt: string;
}

export interface WorkspaceSummaryDto {
  id: string;
  name: string;
  status: number;
  ownerEmail: string;
  createdAt: string;
}