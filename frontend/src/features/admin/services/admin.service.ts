import { axiosClient } from '../../../core/api/axiosClient';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto } from '../types/admin.types';

export const getSystemWorkspaces = async (): Promise<WorkspaceSummaryDto[]> => {
  const { data } = await axiosClient.get<WorkspaceSummaryDto[]>('/superadmin/clients');
  return data;
};

export const provisionNewWorkspace = async (request: ProvisionWorkspaceRequest): Promise<void> => {
  await axiosClient.post('/superadmin/clients/provision', request);
};

// 🔥 NUEVAS ACCIONES DE GESTIÓN
export const suspendWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/suspend', { workspaceId });
};

export const reactivateWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/reactivate', { workspaceId });
};

export const deleteWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.delete(`/superadmin/clients/${workspaceId}`);
};