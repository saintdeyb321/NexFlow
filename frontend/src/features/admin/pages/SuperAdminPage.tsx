import { PageHeader } from '../../../components/ui/Layout';
import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { Alert, LoadingState, EmptyState } from '../../../components/ui/Feedback';
import { useRef, useState } from 'react';

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

  const { workspaces, isLoading, isProvisioning, errorMessage, handleProvision, handleToggleStatus } = useSuperAdmin();
  const [pendingAction, setPendingAction] = useState<{ workspace: WorkspaceSummaryDto } | null>(null);
  const actionInFlight = useRef(false);
  const [isActing, setIsActing] = useState(false);
  const executeAction = async () => {
    if (!pendingAction || actionInFlight.current) return;
    const workspace = workspaces.find(item => item.id === pendingAction.workspace.id);
    if (!workspace || workspace.status === 5) return;
    actionInFlight.current = true;
    setIsActing(true);
    try {
      await handleToggleStatus(workspace);
      setPendingAction(null);
    } finally { actionInFlight.current = false; setIsActing(false); }
  };

  if (!isSuperAdmin) {
    return (
      <div className="flex flex-col items-center justify-center h-[60vh] text-red-500 animate-in fade-in">
        <ShieldAlert aria-hidden="true" className="w-16 h-16 mb-4" />
        <h2 className="text-xl font-bold">Acceso Denegado</h2>
        <p className="text-muted mt-2">No tienes privilegios de Super Administrador.</p>
      </div>
    );
  }

  if (isLoading) return <LoadingState title="Cargando inquilinos..." />;

  return (
    <div className="nf-page">
      {errorMessage && <Alert tone="error" className="mb-4">{errorMessage}</Alert>}
      <Alert tone="info" className="mb-4">La eliminación definitiva está temporalmente deshabilitada. Puedes suspender o reactivar el negocio.</Alert>
      <ConfirmDialog isOpen={Boolean(pendingAction)} onClose={() => setPendingAction(null)} title="Confirmar operación" description={pendingAction?.workspace.name} isLoading={isActing} confirmDisabled={workspaces.find(item => item.id === pendingAction?.workspace.id)?.status === 5} onConfirm={executeAction}>
        <p>¿Confirmas el cambio de estado?</p>
      </ConfirmDialog>

      {/* Cabecera */}
      <PageHeader title="Consola SuperAdmin" description="Gestiona workspaces, licencias y módulos desde un solo lugar." icon={<Server aria-hidden="true" className="w-5 h-5" />} actions={<><Button variant="primary"
          onClick={() => setShowProvisionModal(true)}
          className="flex items-center font-medium transition-colors"
        >
          <Plus aria-hidden="true" className="w-4 h-4 mr-2" /> Aprovisionar Cliente
        </Button></>} />

      {/* Listado */}
      <div className="bg-surface rounded-xl shadow-sm border border-line p-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-line pb-2">
          Negocios Registrados ({workspaces.length})
        </h3>

        <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
          {workspaces.length === 0 ? (
            <EmptyState className="col-span-full" title="No hay negocios registrados en el sistema." />
          ) : (
            workspaces.map(ws => (
              <WorkspaceCard
                  key={ws.id}
                  workspace={ws}
                  onToggleStatus={workspace => setPendingAction({ workspace })}
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
