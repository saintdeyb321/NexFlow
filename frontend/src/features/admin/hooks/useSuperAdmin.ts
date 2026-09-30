import { getQuerySession, isCurrentQuerySession } from '../../../core/query/queryPersistence';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { useCallback, useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { getSystemWorkspaces, provisionNewWorkspace, suspendWorkspace, reactivateWorkspace, deleteWorkspace } from '../services/admin.service';
import type { WorkspaceSummaryDto, ProvisionWorkspaceRequest } from '../types/admin.types';

export const useSuperAdmin = () => {
  const isPageVisible = usePageVisible();
  const me = useAuthStore(state => state.me);
  const queryClient = useQueryClient();
  const queryKey = queryKeys.system.workspaces(me?.user.id);
  const [isProvisioning, setIsProvisioning] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { data: workspaces = [], isLoading, error } = useQuery({
    ...queryPolicies.dynamic,
    queryKey, queryFn: ({ signal }) => getSystemWorkspaces(signal), enabled: me?.user.isSuperAdmin === true,
    refetchInterval: query => isPageVisible && query.state.data?.some(workspace => workspace.status === 5) ? 5000 : false,
  });
  const loadWorkspaces = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: queryKeys.system.workspaces(me?.user.id) });
  }, [queryClient, me?.user.id]);

  const handleProvision = async (payload: ProvisionWorkspaceRequest, onSuccess: () => void) => {
    const session = getQuerySession();
    setIsProvisioning(true);
    setErrorMessage(null);
    try {
      await provisionNewWorkspace(payload);
      if (!isCurrentQuerySession(session)) return;
      await loadWorkspaces();
      if (isCurrentQuerySession(session)) onSuccess();
    } catch (error: unknown) {
      if (isCurrentQuerySession(session)) setErrorMessage(getApiErrorPresentation(error));
    } finally {
      if (isCurrentQuerySession(session)) setIsProvisioning(false);
    }
  };

  const handleToggleStatus = async (workspace: WorkspaceSummaryDto) => {
    if (workspace.status === 5) return;
    const session = getQuerySession();
    setErrorMessage(null);
    try {
      if (workspace.status === 2) await reactivateWorkspace(workspace.id);
      else await suspendWorkspace(workspace.id);
      if (!isCurrentQuerySession(session)) return;
      await loadWorkspaces();
    } catch (error: unknown) {
      if (isCurrentQuerySession(session)) setErrorMessage(getApiErrorPresentation(error));
    }
  };

  const handleDelete = async (workspace: WorkspaceSummaryDto) => {
    if (workspace.status === 5) return;
    const session = getQuerySession();
    setErrorMessage(null);
    try {
      await deleteWorkspace(workspace.id);
      if (!isCurrentQuerySession(session)) return;
      queryClient.setQueryData<WorkspaceSummaryDto[]>(queryKey, previous =>
        previous?.map(item => item.id === workspace.id ? { ...item, status: 5 } : item));
      if (!isCurrentQuerySession(session)) return;
      await loadWorkspaces();
    } catch (error: unknown) {
      if (isCurrentQuerySession(session)) setErrorMessage(getApiErrorPresentation(error));
    }
  };

  return { workspaces, isLoading, isProvisioning, loadWorkspaces, handleProvision, handleToggleStatus, handleDelete,
    errorMessage: errorMessage || (error ? getApiErrorPresentation(error) : null) };
};
