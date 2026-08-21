import { useState, useEffect } from 'react';
import { ShieldAlert, Plus, Server } from 'lucide-react';
import { getSystemWorkspaces, provisionNewWorkspace } from '../services/admin.service';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto } from '../types/admin.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const SuperAdminPage = () => {
  const { me } = useAuthStore();
  const isSuperAdmin = me?.user?.email === 'deyvidparionaramos@gmail.com';

  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isProvisioning, setIsProvisioning] = useState(false);
  const [showModal, setShowModal] = useState(false);

  // Fecha por defecto: 1 año a partir de hoy (Formato YYYY-MM-DD para el input de fecha)
  const defaultDate = new Date();
  defaultDate.setFullYear(defaultDate.getFullYear() + 1);

  const [newWorkspace, setNewWorkspace] = useState<ProvisionWorkspaceRequest>({
    email: '',
    firstName: '',
    lastName: '',
    workspaceName: '',
    templateId: '', // Debe ser un Guid válido de tu tabla Templates
    expiresAt: defaultDate.toISOString().split('T')[0]
  });

  useEffect(() => {
    if (isSuperAdmin) loadWorkspaces();
    else setIsLoading(false);
  }, [isSuperAdmin]);

  const loadWorkspaces = async () => {
    try {
      // Si tu backend aún no tiene el GET /superadmin/clients, esto fallará silenciosamente,
      // dejando la tabla vacía, lo cual está bien para poder probar el POST primero.
      const data = await getSystemWorkspaces().catch(() => []);
      setWorkspaces(data || []);
    } finally {
      setIsLoading(false);
    }
  };

  const handleProvision = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsProvisioning(true);
    try {
      // Al enviar, convertimos la fecha al formato completo ISO que C# DateTime espera
      const payload = {
        ...newWorkspace,
        expiresAt: new Date(newWorkspace.expiresAt).toISOString()
      };
      
      await provisionNewWorkspace(payload);
      alert('¡Entorno aprovisionado con éxito! Revisa tu base de datos.');
      setShowModal(false);
      loadWorkspaces();
    } catch (error: any) {
      // Capturamos el 400 Bad Request que armaste con tu patrón Result
      const errorMsg = error.response?.data?.message || 'Error aprovisionando el entorno';
      alert(`Fallo en el aprovisionamiento:\n${errorMsg}`);
    } finally {
      setIsProvisioning(false);
    }
  };

  if (!isSuperAdmin) {
    return (
      <div className="flex flex-col items-center justify-center h-64 text-red-500">
        <ShieldAlert className="w-16 h-16 mb-4" />
        <h2 className="text-xl font-bold">Acceso Denegado</h2>
      </div>
    );
  }

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando...</div>;

  return (
    <div className="max-w-6xl mx-auto">
      <div className="mb-8 flex justify-between items-end">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <Server className="w-6 h-6 mr-3 text-purple-600" /> Consola SuperAdmin
          </h1>
        </div>
        <button onClick={() => setShowModal(true)} className="flex items-center px-4 py-2 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700">
          <Plus className="w-4 h-4 mr-2" /> Aprovisionar Cliente
        </button>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Negocio</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Dueño</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Estado</th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {workspaces.length === 0 ? (
              <tr><td colSpan={3} className="px-6 py-8 text-center text-gray-500">No hay clientes o el endpoint GET aún no existe.</td></tr>
            ) : (
              workspaces.map((ws) => (
                <tr key={ws.id}>
                  <td className="px-6 py-4 font-medium">{ws.name}</td>
                  <td className="px-6 py-4 text-sm text-gray-500">{ws.ownerEmail}</td>
                  <td className="px-6 py-4"><span className="px-2 text-xs font-semibold rounded-full bg-green-100 text-green-800">Activo</span></td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {showModal && (
        <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
          <div className="bg-white rounded-xl p-6 w-full max-w-md shadow-2xl overflow-y-auto max-h-[90vh]">
            <h3 className="text-xl font-bold text-gray-900 mb-4">Nuevo Inquilino (Tenant)</h3>
            <form onSubmit={handleProvision} className="space-y-4">
              <div><label className="block text-sm font-medium mb-1">Nombre del Negocio</label><input type="text" value={newWorkspace.workspaceName} onChange={e => setNewWorkspace({...newWorkspace, workspaceName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
              
              <div className="grid grid-cols-2 gap-4">
                <div><label className="block text-sm font-medium mb-1">Nombres</label><input type="text" value={newWorkspace.firstName} onChange={e => setNewWorkspace({...newWorkspace, firstName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
                <div><label className="block text-sm font-medium mb-1">Apellidos</label><input type="text" value={newWorkspace.lastName} onChange={e => setNewWorkspace({...newWorkspace, lastName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
              </div>
              
              <div><label className="block text-sm font-medium mb-1">Correo (Google Auth)</label><input type="email" value={newWorkspace.email} onChange={e => setNewWorkspace({...newWorkspace, email: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
              
              <div>
                <label className="block text-sm font-medium mb-1">ID de Plantilla (UUID)</label>
                <input type="text" placeholder="Ej: 550e8400-e29b-41d4-a716-446655440000" value={newWorkspace.templateId} onChange={e => setNewWorkspace({...newWorkspace, templateId: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500 font-mono text-sm" required />
                <p className="text-xs text-gray-500 mt-1">Debe ser un TemplateId válido en la base de datos.</p>
              </div>

              <div><label className="block text-sm font-medium mb-1">Vencimiento de Licencia</label><input type="date" value={newWorkspace.expiresAt} onChange={e => setNewWorkspace({...newWorkspace, expiresAt: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required /></div>
              
              <div className="flex justify-end space-x-3 mt-6">
                <button type="button" onClick={() => setShowModal(false)} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg">Cancelar</button>
                <button type="submit" disabled={isProvisioning} className="px-4 py-2 bg-purple-600 text-white rounded-lg hover:bg-purple-700 disabled:opacity-50">
                  {isProvisioning ? 'Creando...' : 'Aprovisionar'}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </div>
  );
};