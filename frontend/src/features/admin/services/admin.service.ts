import { axiosClient } from '../../../core/api/axiosClient';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto } from '../types/admin.types';

export const getSystemWorkspaces = async (): Promise<WorkspaceSummaryDto[]> => {
  // Ajustado a tu ruta base (necesitaremos este endpoint en C#)
  const { data } = await axiosClient.get<WorkspaceSummaryDto[]>('/superadmin/clients');
  return data;
};

export const provisionNewWorkspace = async (request: ProvisionWorkspaceRequest): Promise<void> => {
  // Ajustado a tu ruta exacta de aprovisionamiento
  await axiosClient.post('/superadmin/clients/provision', request);
};