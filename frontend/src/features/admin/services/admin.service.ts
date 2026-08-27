import { axiosClient } from '../../../core/api/axiosClient';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto } from '../types/admin.types';

export const getSystemWorkspaces = async (): Promise<WorkspaceSummaryDto[]> => {
  const { data } = await axiosClient.get<WorkspaceSummaryDto[]>('/superadmin/clients');
  return data;
};

// 🔥 NUEVO: Traer catálogos reales
export const getSystemTemplates = async (): Promise<any[]> => {
  const { data } = await axiosClient.get<any[]>('/superadmin/clients/templates');
  return data;
};

export const getSystemModules = async (): Promise<any[]> => {
  const { data } = await axiosClient.get<any[]>('/superadmin/clients/modules');
  return data;
};

export const provisionNewWorkspace = async (request: ProvisionWorkspaceRequest): Promise<void> => {
  await axiosClient.post('/superadmin/clients/provision', request);
};

export const suspendWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/suspend', { workspaceId });
};

export const reactivateWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/reactivate', { workspaceId });
};

export const deleteWorkspace = async (workspaceId: string): Promise<void> => {
  await axiosClient.delete(`/superadmin/clients/${workspaceId}`);
};

// 🔥 NUEVO: Operaciones faltantes
export const renewWorkspaceLicense = async (workspaceId: string, extensionMonths: number): Promise<void> => {
  await axiosClient.post('/superadmin/clients/renew', { workspaceId, extensionMonths });
};

export const assignModuleToWorkspace = async (licenseId: string, moduleId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/assign-module', { licenseId, moduleId });
};