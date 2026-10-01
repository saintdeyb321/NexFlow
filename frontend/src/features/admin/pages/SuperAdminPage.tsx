import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { FormField, Input } from '../../../components/ui/Form';
import { Alert, LoadingState, EmptyState } from '../../../components/ui/Feedback';
import { useState } from 'react';

import { ShieldAlert, Plus, Server } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { useSuperAdmin } from '../hooks/useSuperAdmin';
import { WorkspaceCard } from '../components/WorkspaceCard';
import { ProvisionWorkspaceModal } from '../components/ProvisionWorkspaceModal';
import { RenewLicenseModal } from '../components/RenewLicenseModal';
import { AssignModuleModal } from '../components/AssignModuleModal';
import type { WorkspaceSummaryDto } from '../types/admin.types';

export const SuperAdminPage = () => {
  const { me } = useAuthStore();
  const isSuperAdmin = me?.user?.isSuperAdmin === true;

  const [showProvisionModal, setShowProvisionModal] = useState(false);
  const [workspaceToRenew, setWorkspaceToRenew] = useState<WorkspaceSummaryDto | null>(null);
  const [workspaceToModule, setWorkspaceToModule] = useState<WorkspaceSummaryDto | null>(null);

  const { workspaces, isLoading, isProvisioning, errorMessage, handleProvision, handleToggleStatus, handleDelete } = useSuperAdmin();
  const [pendingAction, setPendingAction] = useState<{ kind: 'delete' | 'status'; workspace: WorkspaceSummaryDto } | null>(null);
  const [confirmation, setConfirmation] = useState('');
  const [isActing, setIsActing] = useState(false);
  const executeAction = async () => {
    if (!pendingAction || (pendingAction.kind === 'delete' && confirmation !== 'ELIMINAR')) return;
    const workspace = workspaces.find(item => item.id === pendingAction.workspace.id);
    if (!workspace || workspace.status === 5) return;
    setIsActing(true);
    try {
      if (pendingAction.kind === 'delete') await handleDelete(workspace);
      else await handleToggleStatus(workspace);
      setPendingAction(null);
      setConfirmation('');
    } finally { setIsActing(false); }
  };

  if (!isSuperAdmin) {
    return (
      <div className="flex flex-col items-center justify-center h-[60vh] text-red-500 animate-in fade-in">
        <ShieldAlert className="w-16 h-16 mb-4" />
        <h2 className="text-xl font-bold">Acceso Denegado</h2>
        <p className="text-gray-500 mt-2">No tienes privilegios de Super Administrador.</p>
      </div>
    );
  }

  if (isLoading) return <LoadingState title="Cargando inquilinos..." />;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      {errorMessage && <Alert tone="error" className="mb-4">{errorMessage}</Alert>}
      <ConfirmDialog isOpen={Boolean(pendingAction)} onClose={() => { setPendingAction(null); setConfirmation(''); }} title="Confirmar operación" description={pendingAction?.workspace.name} destructive={pendingAction?.kind === 'delete'} isLoading={isActing} confirmDisabled={workspaces.find(item => item.id === pendingAction?.workspace.id)?.status === 5 || (pendingAction?.kind === 'delete' && confirmation !== 'ELIMINAR')} onConfirm={executeAction}>
        {pendingAction?.kind === 'delete' ? <FormField label="Confirmación" helperText="La eliminación es irreversible. Escribe ELIMINAR para solicitarla."><Input value={confirmation} onChange={event => setConfirmation(event.target.value)} disabled={isActing} /></FormField> : <p>¿Confirmas el cambio de estado?</p>}
      </ConfirmDialog>

      {/* Cabecera */}
      <div className="mb-8 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Server className="w-6 h-6 mr-3 text-purple-600" /> Consola SuperAdmin
          </h1>
          <p className="text-gray-500 text-sm mt-1">Gestión centralizada de inquilinos y licencias operativas.</p>
        </div>
        <Button variant="primary"
          onClick={() => setShowProvisionModal(true)}
          className="flex items-center px-5 py-2.5 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4 mr-2" /> Aprovisionar Cliente
        </Button>
      </div>

      {/* Listado */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-gray-100 pb-2">
          Negocios Registrados ({workspaces.length})
        </h3>

        <div className="space-y-3">
          {workspaces.length === 0 ? (
            <EmptyState title="No hay negocios registrados en el sistema." />
          ) : (
            workspaces.map(ws => (
              <WorkspaceCard
                  key={ws.id}
                  workspace={ws}
                  onToggleStatus={workspace => setPendingAction({ kind: 'status', workspace })}
                  onDelete={workspace => { setConfirmation(''); setPendingAction({ kind: 'delete', workspace }); }}
                  onRenew={(w) => setWorkspaceToRenew(w)}
                  onAssignModule={(w) => setWorkspaceToModule(w)}
              />
            ))
          )}
        </div>
      </div>

      <ProvisionWorkspaceModal
        isOpen={showProvisionModal}
        onClose={() => setShowProvisionModal(false)}
        onProvision={handleProvision}
        isProvisioning={isProvisioning}
      />

      <RenewLicenseModal
        workspace={workspaces.find(workspace => workspace.id === workspaceToRenew?.id && workspace.status !== 5) ?? null}
        onClose={() => setWorkspaceToRenew(null)}
      />

      <AssignModuleModal
        workspace={workspaces.find(workspace => workspace.id === workspaceToModule?.id && workspace.status !== 5) ?? null}
        onClose={() => setWorkspaceToModule(null)}
      />

    </div>
  );
};
