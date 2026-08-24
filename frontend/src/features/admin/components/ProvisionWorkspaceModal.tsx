import { useState } from 'react';
import type { ProvisionWorkspaceRequest } from '../types/admin.types';

interface ProvisionWorkspaceModalProps {
  onClose: () => void;
  onProvision: (payload: any) => Promise<void>;
  isProvisioning: boolean;
}

export const ProvisionWorkspaceModal = ({ onClose, onProvision, isProvisioning }: ProvisionWorkspaceModalProps) => {
  const defaultDate = new Date();
  defaultDate.setFullYear(defaultDate.getFullYear() + 1);

  const [provisionMode, setProvisionMode] = useState<'template' | 'custom'>('template');
  const [customModulesStr, setCustomModulesStr] = useState('FAQ, RESERVATIONS, SERVICES');

  const [newWorkspace, setNewWorkspace] = useState<ProvisionWorkspaceRequest & { maxLocations: number }>({
    email: '',
    firstName: '',
    lastName: '',
    workspaceName: '',
    templateName: 'BOOKING', 
    expiresAt: defaultDate.toISOString().split('T')[0],
    maxLocations: 1
  });

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const payload: any = {
      email: newWorkspace.email,
      firstName: newWorkspace.firstName,
      lastName: newWorkspace.lastName,
      workspaceName: newWorkspace.workspaceName,
      expiresAt: new Date(newWorkspace.expiresAt).toISOString(),
      maxLocations: Number(newWorkspace.maxLocations)
    };

    if (provisionMode === 'template') {
      payload.templateName = newWorkspace.templateName; 
    } else {
      payload.customModules = customModulesStr.split(',').map(s => s.trim().toUpperCase()).filter(s => s);
    }
    
    await onProvision(payload);
  };

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 animate-in fade-in">
      <div className="bg-white rounded-xl p-6 w-full max-w-md shadow-2xl overflow-y-auto max-h-[90vh]">
        <h3 className="text-xl font-bold text-gray-900 mb-4">Nuevo Inquilino (Tenant)</h3>
        
        <form onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Nombre del Negocio</label>
            <input type="text" value={newWorkspace.workspaceName} onChange={e => setNewWorkspace({...newWorkspace, workspaceName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
          </div>
          
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium mb-1">Nombres</label>
              <input type="text" value={newWorkspace.firstName} onChange={e => setNewWorkspace({...newWorkspace, firstName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Apellidos</label>
              <input type="text" value={newWorkspace.lastName} onChange={e => setNewWorkspace({...newWorkspace, lastName: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
            </div>
          </div>
          
          <div>
            <label className="block text-sm font-medium mb-1">Correo (Google Auth)</label>
            <input type="email" value={newWorkspace.email} onChange={e => setNewWorkspace({...newWorkspace, email: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
          </div>
          
          <div className="pt-4 border-t">
            <label className="block text-sm font-bold mb-3 text-gray-700">Modalidad de Licencia</label>
            <div className="flex gap-4 mb-4">
              <label className="flex items-center cursor-pointer text-sm font-medium">
                <input type="radio" name="mode" checked={provisionMode === 'template'} onChange={() => setProvisionMode('template')} className="mr-2 text-purple-600 focus:ring-purple-500" />
                Por Plantilla
              </label>
              <label className="flex items-center cursor-pointer text-sm font-medium">
                <input type="radio" name="mode" checked={provisionMode === 'custom'} onChange={() => setProvisionMode('custom')} className="mr-2 text-purple-600 focus:ring-purple-500" />
                A la carta
              </label>
            </div>

            {provisionMode === 'template' ? (
              <div className="animate-in fade-in slide-in-from-top-1">
                <select 
                  value={newWorkspace.templateName} 
                  onChange={e => setNewWorkspace({...newWorkspace, templateName: e.target.value})} 
                  className="w-full border border-gray-300 rounded-lg px-3 py-2.5 outline-none focus:ring-2 focus:ring-purple-500 text-sm bg-white"
                >
                  <option value="SUPPORT">Atención Básica (FAQ)</option>
                  <option value="BOOKING">Asistente de Reservas</option>
                  <option value="COMMERCIAL">Asistente Comercial (Catálogo)</option>
                  <option value="REQUESTS">Asistente de Trámites</option>
                  <option value="FULL">Operaciones Completas</option>
                </select>
              </div>
            ) : (
              <div className="animate-in fade-in slide-in-from-top-1">
                <input type="text" placeholder="Ej: FAQ, RESERVATIONS, CATALOG" value={customModulesStr} onChange={e => setCustomModulesStr(e.target.value)} className="w-full border border-gray-300 rounded-lg px-3 py-2.5 outline-none focus:ring-2 focus:ring-purple-500 text-sm uppercase" required={provisionMode === 'custom'} />
                <p className="text-xs text-gray-500 mt-1.5">Módulos separados por coma.</p>
              </div>
            )}
          </div>
          
          <div className="grid grid-cols-2 gap-4 border-t pt-4 mt-2">
            <div>
              <label className="block text-sm font-medium mb-1">Límite de Sedes</label>
              <input type="number" min="1" max="50" value={newWorkspace.maxLocations} onChange={e => setNewWorkspace({...newWorkspace, maxLocations: parseInt(e.target.value)})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
            </div>
            <div>
              <label className="block text-sm font-medium mb-1">Vencimiento</label>
              <input type="date" value={newWorkspace.expiresAt} onChange={e => setNewWorkspace({...newWorkspace, expiresAt: e.target.value})} className="w-full border rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-purple-500" required />
            </div>
          </div>
          
          <div className="flex justify-end space-x-3 mt-8 pt-2">
            <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 font-medium hover:bg-gray-100 rounded-lg transition-colors">Cancelar</button>
            <button type="submit" disabled={isProvisioning} className="px-5 py-2 bg-purple-600 text-white font-medium rounded-lg hover:bg-purple-700 disabled:opacity-50 transition-colors flex items-center">
              {isProvisioning ? 'Procesando...' : 'Aprovisionar Cliente'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};