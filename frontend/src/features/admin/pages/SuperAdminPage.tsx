import { useState, useEffect } from 'react';
import { ShieldAlert, Plus, Server } from 'lucide-react';
import { getSystemWorkspaces, provisionNewWorkspace } from '../services/admin.service';
import type { ProvisionWorkspaceRequest, WorkspaceSummaryDto } from '../types/admin.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const SuperAdminPage = () => {
  const { me } = useAuthStore();
  const isSuperAdmin = me?.user?.isSuperAdmin === true;

  const [workspaces, setWorkspaces] = useState<WorkspaceSummaryDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isProvisioning, setIsProvisioning] = useState(false);
  const [showModal, setShowModal] = useState(false);

  const [provisionMode, setProvisionMode] = useState<'template' | 'custom'>('template');
  const [customModulesStr, setCustomModulesStr] = useState('FAQ, RESERVATIONS, SERVICES');

  const defaultDate = new Date();
  defaultDate.setFullYear(defaultDate.getFullYear() + 1);

  // NUEVO: Agregamos maxLocations y cambiamos el template base al nuevo catálogo
  const [newWorkspace, setNewWorkspace] = useState<ProvisionWorkspaceRequest & { maxLocations: number }>({
    email: '',
    firstName: '',
    lastName: '',
    workspaceName: '',
    templateName: 'BOOKING', 
    expiresAt: defaultDate.toISOString().split('T')[0],
    maxLocations: 1
  });

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

  const handleProvision = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsProvisioning(true);
    try {
      const payload: any = {
        email: newWorkspace.email,
        firstName: newWorkspace.firstName,
        lastName: newWorkspace.lastName,
        workspaceName: newWorkspace.workspaceName,
        expiresAt: new Date(newWorkspace.expiresAt).toISOString(),
        maxLocations: Number(newWorkspace.maxLocations) // <-- Enviamos el candado físico
      };

      if (provisionMode === 'template') {
        payload.templateName = newWorkspace.templateName; 
      } else {
        payload.customModules = customModulesStr.split(',').map(s => s.trim().toUpperCase()).filter(s => s);
      }
      
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
              <tr><td colSpan={3} className="px-6 py-8 text-center text-gray-500">No hay clientes aprovisionados.</td></tr>
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
              
              <div className="pt-2 border-t">
                <label className="block text-sm font-bold mb-2">Modalidad de Licencia</label>
                <div className="flex gap-4 mb-3">
                  <label className="flex items-center cursor-pointer">
                    <input type="radio" name="mode" checked={provisionMode === 'template'} onChange={() => setProvisionMode('template')} className="mr-2" />
                    Por Plantilla
                  </label>
                  <label className="flex items-center cursor-pointer">
                    <input type="radio" name="mode" checked={provisionMode === 'custom'} onChange={() => setProvisionMode('custom')} className="mr-2" />
                    A la carta
                  </label>
                </div>

                {provisionMode === 'template' ? (
                  <div>
                    {/* SPRINT 12: Las 5 plantillas maestras reales */}
                    <select 
                      value={newWorkspace.templateName} 
                      onChange={e => setNewWorkspace({...newWorkspace, templateName: e.target.value})} 
                      className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500 text-sm"
                    >
                      <option value="SUPPORT">Atención Básica (FAQ)</option>
                      <option value="BOOKING">Asistente de Reservas</option>
                      <option value="COMMERCIAL">Asistente Comercial (Catálogo)</option>
                      <option value="REQUESTS">Asistente de Trámites</option>
                      <option value="FULL">Operaciones Completas</option>
                    </select>
                  </div>
                ) : (
                  <div>
                    <input type="text" placeholder="Ej: FAQ, RESERVATIONS, CATALOG" value={customModulesStr} onChange={e => setCustomModulesStr(e.target.value)} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500 text-sm uppercase" required={provisionMode === 'custom'} />
                    <p className="text-xs text-gray-500 mt-1">Separados por coma.</p>
                  </div>
                )}
              </div>
              
              <div className="grid grid-cols-2 gap-4 border-t pt-2 mt-2">
                <div>
                  <label className="block text-sm font-medium mb-1">Límite de Sedes</label>
                  <input type="number" min="1" max="50" value={newWorkspace.maxLocations} onChange={e => setNewWorkspace({...newWorkspace, maxLocations: parseInt(e.target.value)})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
                </div>
                <div>
                  <label className="block text-sm font-medium mb-1">Vencimiento</label>
                  <input type="date" value={newWorkspace.expiresAt} onChange={e => setNewWorkspace({...newWorkspace, expiresAt: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
                </div>
              </div>
              
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