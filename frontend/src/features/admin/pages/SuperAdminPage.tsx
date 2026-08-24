import { useState, useEffect } from 'react';
import { ShieldAlert, Plus, Server } from 'lucide-react';
import { getSystemWorkspaces, provisionNewWorkspace } from '../services/admin.service';
import type { WorkspaceSummaryDto } from '../types/admin.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { WorkspaceTable } from '../components/WorkspaceTable';
import { ProvisionWorkspaceModal } from '../components/ProvisionWorkspaceModal';

export const SuperAdminPage = () => {
  const { me } = useAuthStore();
  const isSuperAdmin = me?.user?.isSuperAdmin === true;

  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isProvisioning, setIsProvisioning] = useState(false);
  const [showModal, setShowModal] = useState(false);

  useEffect(() => {
    if (isSuperAdmin) loadWorkspaces();
    else setIsLoading(false);
  }, [isSuperAdmin]);

  const loadWorkspaces = async () => {
    try {
      const data = await getSystemWorkspaces().catch(() => []);
      setWorkspaces(data || []);
    } finally {
      setIsLoading(false);
    }
  };

  const handleProvision = async (payload: any) => {
    setIsProvisioning(true);
    try {
      await provisionNewWorkspace(payload);
      alert('¡Entorno aprovisionado con éxito!');
      setShowModal(false);
      loadWorkspaces();
    } catch (error: any) {
      alert(`Fallo en el aprovisionamiento:\n${error.response?.data?.detail || 'Error interno'}`);
    } finally {
      setIsProvisioning(false);
    }
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

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando inquilinos...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      <div className="mb-8 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Server className="w-6 h-6 mr-3 text-purple-600" /> Consola SuperAdmin
          </h1>
          <p className="text-gray-500 text-sm mt-1">Gestión centralizada de inquilinos y licencias operativas.</p>
        </div>
        <button 
          onClick={() => setShowModal(true)} 
          className="flex items-center px-4 py-2.5 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4 mr-2" /> Aprovisionar Cliente
        </button>
      </div>

      <WorkspaceTable workspaces={workspaces} />

      {showModal && (
        <ProvisionWorkspaceModal 
          onClose={() => setShowModal(false)} 
          onProvision={handleProvision} 
          isProvisioning={isProvisioning} 
        />
      )}
    </div>
  );
};