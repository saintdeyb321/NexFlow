import { useState, useEffect } from 'react';
import { Save } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import type { ProvisionWorkspaceRequest } from '../types/admin.types';

interface ProvisionModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProvision: (payload: ProvisionWorkspaceRequest, onSuccess: () => void) => Promise<void>;
  isProvisioning: boolean;
}

export const ProvisionWorkspaceModal = ({ isOpen, onClose, onProvision, isProvisioning }: ProvisionModalProps) => {
  const [provisionMode, setProvisionMode] = useState<'template' | 'custom'>('template');
  const [customModulesStr, setCustomModulesStr] = useState('FAQ, RESERVATIONS, SERVICES');

  const [formData, setFormData] = useState({
    email: '',
    firstName: '',
    lastName: '',
    workspaceName: '',
    templateCode: 'BOOKING', 
    expiresAt: '',
    maxLocations: 1
  });

  // Generar fecha por defecto al abrir el modal (1 año)
  useEffect(() => {
    if (isOpen) {
      const defaultDate = new Date();
      defaultDate.setFullYear(defaultDate.getFullYear() + 1);
      setFormData(prev => ({ ...prev, expiresAt: defaultDate.toISOString().split('T')[0] }));
    }
  }, [isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    // Construcción estricta basada en el C# ProvisionClientCommand
    const payload: ProvisionWorkspaceRequest = {
      email: formData.email,
      firstName: formData.firstName,
      lastName: formData.lastName,
      workspaceName: formData.workspaceName,
      expiresAt: new Date(formData.expiresAt).toISOString(),
      maxLocations: Number(formData.maxLocations)
    };

    if (provisionMode === 'template') {
      payload.templateCode = formData.templateCode; 
    } else {
      payload.customModules = customModulesStr.split(',').map(s => s.trim().toUpperCase()).filter(s => s);
    }
    
    await onProvision(payload, () => {
      setFormData({ email: '', firstName: '', lastName: '', workspaceName: '', templateCode: 'BOOKING', expiresAt: '', maxLocations: 1 });
      setProvisionMode('template');
      onClose();
    });
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Nuevo Inquilino (Tenant)" maxWidth="max-w-lg">
      <form onSubmit={handleSubmit} className="space-y-4">
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Nombre del Negocio</label>
          <input type="text" required value={formData.workspaceName} onChange={e => setFormData({...formData, workspaceName: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Nombres</label>
            <input type="text" required value={formData.firstName} onChange={e => setFormData({...formData, firstName: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Apellidos</label>
            <input type="text" required value={formData.lastName} onChange={e => setFormData({...formData, lastName: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
          </div>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Correo (Google Auth)</label>
          <input type="email" required value={formData.email} onChange={e => setFormData({...formData, email: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
        </div>

        <div className="pt-4 border-t border-gray-100">
          <label className="block text-sm font-bold mb-3 text-gray-800">Modalidad de Licencia</label>
          <div className="flex gap-4 mb-4">
            <label className="flex items-center cursor-pointer text-sm font-medium text-gray-700">
              <input type="radio" name="mode" checked={provisionMode === 'template'} onChange={() => setProvisionMode('template')} className="mr-2 w-4 h-4 text-purple-600 focus:ring-purple-500 border-gray-300" />
              Por Plantilla
            </label>
            <label className="flex items-center cursor-pointer text-sm font-medium text-gray-700">
              <input type="radio" name="mode" checked={provisionMode === 'custom'} onChange={() => setProvisionMode('custom')} className="mr-2 w-4 h-4 text-purple-600 focus:ring-purple-500 border-gray-300" />
              A la carta
            </label>
          </div>

          {provisionMode === 'template' ? (
            <div className="animate-in fade-in slide-in-from-top-1">
              <select 
                value={formData.templateCode} 
                onChange={e => setFormData({...formData, templateCode: e.target.value})} 
                className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all cursor-pointer"
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
              <input 
                type="text" 
                placeholder="Ej: FAQ, RESERVATIONS, CATALOG" 
                value={customModulesStr} 
                onChange={e => setCustomModulesStr(e.target.value)} 
                className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all uppercase" 
                required={provisionMode === 'custom'} 
              />
              <p className="text-xs text-gray-500 mt-1.5">Módulos separados por coma.</p>
            </div>
          )}
        </div>

        <div className="grid grid-cols-2 gap-4 border-t border-gray-100 pt-4 mt-2">
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Límite de Sedes</label>
            <input type="number" min="1" max="50" required value={formData.maxLocations} onChange={e => setFormData({...formData, maxLocations: parseInt(e.target.value)})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700 mb-1">Vencimiento</label>
            <input type="date" required value={formData.expiresAt} onChange={e => setFormData({...formData, expiresAt: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" />
          </div>
        </div>

        <div className="pt-6 border-t border-gray-100 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-5 py-2.5 text-sm font-medium text-gray-600 hover:bg-gray-100 rounded-xl transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={isProvisioning} className="flex items-center px-5 py-2.5 text-sm font-medium text-white bg-purple-600 hover:bg-purple-700 rounded-xl transition-colors disabled:opacity-50 shadow-sm">
            <Save className="w-4 h-4 mr-2" />
            {isProvisioning ? 'Procesando...' : 'Aprovisionar Cliente'}
          </button>
        </div>
      </form>
    </Modal>
  );
};