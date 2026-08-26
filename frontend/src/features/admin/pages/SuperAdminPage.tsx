import { useState, useEffect } from 'react';
import { ShieldAlert, Plus, Server } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { useSuperAdmin } from '../hooks/useSuperAdmin';
import { WorkspaceCard } from '../components/WorkspaceCard';
import { ProvisionWorkspaceModal } from '../components/ProvisionWorkspaceModal';

export const SuperAdminPage = () => {
  const { me } = useAuthStore();
  const isSuperAdmin = me?.user?.isSuperAdmin === true;
  
  const [showModal, setShowModal] = useState(false);
  const { workspaces, isLoading, isProvisioning, loadWorkspaces, handleProvision, handleToggleStatus, handleDelete } = useSuperAdmin();

  useEffect(() => {
    if (isSuperAdmin) loadWorkspaces();
  }, [isSuperAdmin, loadWorkspaces]);

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
      
      {/* Cabecera */}
      <div className="mb-8 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Server className="w-6 h-6 mr-3 text-purple-600" /> Consola SuperAdmin
          </h1>
          <p className="text-gray-500 text-sm mt-1">Gestión centralizada de inquilinos y licencias operativas.</p>
        </div>
        <button 
          onClick={() => setShowModal(true)} 
          className="flex items-center px-5 py-2.5 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4 mr-2" /> Aprovisionar Cliente
        </button>
      </div>

      {/* Listado */}
      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <h3 className="text-sm font-semibold text-gray-700 mb-4 border-b border-gray-100 pb-2">
          Negocios Registrados ({workspaces.length})
        </h3>
        
        <div className="space-y-3">
          {workspaces.length === 0 ? (
            <div className="text-center py-10 text-gray-500">No hay negocios registrados en el sistema.</div>
          ) : (
            workspaces.map(ws => (
              <WorkspaceCard 
                key={ws.id} 
                workspace={ws} 
                onToggleStatus={handleToggleStatus} 
                onDelete={handleDelete} 
              />
            ))
          )}
        </div>
      </div>

      {/* Modal Desacoplado */}
      <ProvisionWorkspaceModal 
        isOpen={showModal}
        onClose={() => setShowModal(false)} 
        onProvision={handleProvision} 
        isProvisioning={isProvisioning} 
      />
      
    </div>
  );
};