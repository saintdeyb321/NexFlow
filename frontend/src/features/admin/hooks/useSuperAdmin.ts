import { useCallback, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { getSystemWorkspaces, provisionNewWorkspace, suspendWorkspace, reactivateWorkspace, deleteWorkspace } from '../services/admin.service';
import type { WorkspaceSummaryDto, ProvisionWorkspaceRequest } from '../types/admin.types';

export const useSuperAdmin = () => {
  const me = useAuthStore(state => state.me);
  const queryClient = useQueryClient();
  const queryKey = ['systemWorkspaces', me?.user.id];
  const [isProvisioning, setIsProvisioning] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { data: workspaces = [], isLoading, error } = useQuery({
    queryKey, queryFn: getSystemWorkspaces, enabled: me?.user.isSuperAdmin === true,
    refetchInterval: query => query.state.data?.some(workspace => workspace.status === 5) ? 5000 : false,
  });
  const loadWorkspaces = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ['systemWorkspaces', me?.user.id] });
  }, [queryClient, me?.user.id]);

  const handleProvision = async (payload: ProvisionWorkspaceRequest, onSuccess: () => void) => {
    setIsProvisioning(true);
    setErrorMessage(null);
    try {
      await provisionNewWorkspace(payload);
      await loadWorkspaces();
      onSuccess();
    } catch (error: unknown) {
      setErrorMessage(getApiErrorPresentation(error));
    } finally {
      setIsProvisioning(false);
    }
  };

  const handleToggleStatus = async (workspace: WorkspaceSummaryDto) => {
    if (workspace.status === 5) return;
    setErrorMessage(null);
    try {
      if (workspace.status === 2) await reactivateWorkspace(workspace.id);
      else await suspendWorkspace(workspace.id);
      await loadWorkspaces();
    } catch (error: unknown) {
      setErrorMessage(getApiErrorPresentation(error));
    }
  };

  const handleDelete = async (workspace: WorkspaceSummaryDto) => {
    if (workspace.status === 5) return;
    setErrorMessage(null);
    try {
      await deleteWorkspace(workspace.id);
      queryClient.setQueryData<WorkspaceSummaryDto[]>(queryKey, previous =>
        previous?.map(item => item.id === workspace.id ? { ...item, status: 5 } : item));
      await loadWorkspaces();
    } catch (error: unknown) {
      setErrorMessage(getApiErrorPresentation(error));
    }
  };

  return { workspaces, isLoading, isProvisioning, loadWorkspaces, handleProvision, handleToggleStatus, handleDelete,
    errorMessage: errorMessage || (error ? getApiErrorPresentation(error) : null) };
};
