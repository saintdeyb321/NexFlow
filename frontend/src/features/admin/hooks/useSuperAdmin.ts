import { useState, useCallback } from 'react';
import { getSystemWorkspaces, provisionNewWorkspace, suspendWorkspace, reactivateWorkspace, deleteWorkspace } from '../services/admin.service';
import type { WorkspaceSummaryDto, ProvisionWorkspaceRequest } from '../types/admin.types';

export const useSuperAdmin = () => {
  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isProvisioning, setIsProvisioning] = useState(false);

  const loadWorkspaces = useCallback(async () => {
    setIsLoading(true);
    try {
      const data = await getSystemWorkspaces();
      setWorkspaces(data || []);
    } catch (error) {
      console.error(error);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const handleProvision = async (payload: ProvisionWorkspaceRequest, onSuccess: () => void) => {
    setIsProvisioning(true);
    try {
      await provisionNewWorkspace(payload);
      await loadWorkspaces();
      onSuccess();
    } catch (error: any) {
      alert(`Fallo en el aprovisionamiento:\n${error.response?.data?.message || 'Error interno'}`);
    } finally {
      setIsProvisioning(false);
    }
  };

  const handleToggleStatus = async (ws: WorkspaceSummaryDto) => {
    const isSuspended = ws.status === 2; // 0=Pending, 1=Active, 2=Suspended
    if (!window.confirm(`¿Seguro que deseas ${isSuspended ? 'REACTIVAR' : 'SUSPENDER'} el negocio ${ws.name}?`)) return;

    try {
      if (isSuspended) await reactivateWorkspace(ws.id);
      else await suspendWorkspace(ws.id);
      await loadWorkspaces();
    } catch (error: any) {
      alert(`Error al cambiar estado: ${error.response?.data?.error || error.message}`);
    }
  };

  const handleDelete = async (ws: WorkspaceSummaryDto) => {
    const confirm = window.prompt(`ESTO ES IRREVERSIBLE. Escribe "ELIMINAR" para borrar completamente a ${ws.name}.`);
    if (confirm !== 'ELIMINAR') return;

    try {
      await deleteWorkspace(ws.id);
      setWorkspaces(prev => prev.filter(w => w.id !== ws.id));
    } catch (error: any) {
      alert(`Error al eliminar: ${error.response?.data?.error || error.message}`);
    }
  };

  return {
    workspaces,
    isLoading,
    isProvisioning,
    loadWorkspaces,
    handleProvision,
    handleToggleStatus,
    handleDelete
  };
};