export interface ProvisionWorkspaceRequest {
  email: string;
  firstName: string;
  lastName: string;
  workspaceName: string;
  templateId?: string; 
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