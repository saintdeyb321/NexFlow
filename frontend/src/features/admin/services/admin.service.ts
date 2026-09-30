import { axiosClient } from '../../../core/api/axiosClient';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto, SystemModuleDto, SystemTemplateDto } from '../types/admin.types';

export const getSystemWorkspaces = async (signal?: AbortSignal): Promise<WorkspaceSummaryDto[]> => {
  const { data } = await axiosClient.get<WorkspaceSummaryDto[]>('/superadmin/clients', { signal });
  return data;
};

export const getSystemTemplates = async (signal?: AbortSignal): Promise<SystemTemplateDto[]> => {
  const { data } = await axiosClient.get<SystemTemplateDto[]>('/superadmin/clients/templates', { signal });
  return data;
};

export const getSystemModules = async (signal?: AbortSignal): Promise<SystemModuleDto[]> => {
  const { data } = await axiosClient.get<SystemModuleDto[]>('/superadmin/clients/modules', { signal });
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
  await axiosClient.post('/superadmin/clients/delete', { workspaceId });
};

export const renewWorkspaceLicense = async (workspaceId: string, durationInMonths: number): Promise<void> => {
  await axiosClient.post('/superadmin/clients/renew', { workspaceId, durationInMonths });
};

export const assignModuleToWorkspace = async (workspaceId: string, moduleId: string): Promise<void> => {
  await axiosClient.post('/superadmin/clients/assign-module', { workspaceId, moduleId });
};