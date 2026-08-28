import { useState, useEffect } from 'react';
import { Save } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import { getSystemTemplates, getSystemModules } from '../services/admin.service';
import type { ProvisionWorkspaceRequest } from '../types/admin.types';

interface ProvisionModalProps {
  isOpen: boolean;
  onClose: () => void;
  onProvision: (payload: ProvisionWorkspaceRequest, onSuccess: () => void) => Promise<void>;
  isProvisioning: boolean;
}

export const ProvisionWorkspaceModal = ({ isOpen, onClose, onProvision, isProvisioning }: ProvisionModalProps) => {
  const [provisionMode, setProvisionMode] = useState<'template' | 'custom'>('template');
  
  const [dbTemplates, setDbTemplates] = useState<any[]>([]);
  const [dbModules, setDbModules] = useState<any[]>([]);
  const [selectedCustomModules, setSelectedCustomModules] = useState<string[]>([]);

  const [formData, setFormData] = useState({
    email: '',
    templateCode: '', 
    expiresAt: '',
    maxLocations: 1
  });

  useEffect(() => {
    const fetchCatalog = async () => {
      try {
        const [tplRes, modRes] = await Promise.all([
          getSystemTemplates(),
          getSystemModules()
        ]);
        setDbTemplates(tplRes);
        setDbModules(modRes);
        if (tplRes.length > 0) {
          setFormData(prev => ({ ...prev, templateCode: tplRes[0].code }));
        }
      } catch (error) {
        console.error("Error cargando catálogo", error);
      }
    };

    if (isOpen) {
      fetchCatalog();
      const defaultDate = new Date();
      defaultDate.setFullYear(defaultDate.getFullYear() + 1);
      setFormData(prev => ({ ...prev, expiresAt: defaultDate.toISOString().split('T')[0] }));
    }
  }, [isOpen]);

  const handleModuleToggle = (code: string) => {
    setSelectedCustomModules(prev => 
      prev.includes(code) ? prev.filter(c => c !== code) : [...prev, code]
    );
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    
    // 🔥 SPRINT 1 (Auditoría #38): Limpiamos la inyección forzada de Nombres y Apellidos
    const payload: ProvisionWorkspaceRequest = {
      email: formData.email,
      workspaceName: "Negocio por Configurar",
      expiresAt: new Date(formData.expiresAt).toISOString(),
      maxLocations: Number(formData.maxLocations)
    };

    if (provisionMode === 'template') {
      payload.templateCode = formData.templateCode; 
    } else {
      if (selectedCustomModules.length === 0) return alert('Selecciona al menos 1 módulo custom');
      payload.customModules = selectedCustomModules;
    }
    
    await onProvision(payload, () => {
      setFormData({ email: '', templateCode: dbTemplates[0]?.code || '', expiresAt: '', maxLocations: 1 });
      setProvisionMode('template');
      setSelectedCustomModules([]);
      onClose();
    });
  };

  return (
    <Modal isOpen={isOpen} onClose={onClose} title="Nuevo Inquilino (Tenant)" maxWidth="max-w-lg">
      <form onSubmit={handleSubmit} className="space-y-4">
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Correo del Dueño (Google Auth)</label>
          <input type="email" required value={formData.email} onChange={e => setFormData({...formData, email: e.target.value})} className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl outline-none focus:bg-white focus:ring-2 focus:ring-purple-500 transition-all" placeholder="cliente@gmail.com" />
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
                {dbTemplates.map(t => (
                  <option key={t.code} value={t.code}>{t.name} ({t.code})</option>
                ))}
              </select>
            </div>
          ) : (
            <div className="animate-in fade-in slide-in-from-top-1 bg-gray-50 p-4 rounded-xl border border-gray-200">
              <label className="block text-xs font-semibold text-gray-500 uppercase tracking-wider mb-3">Módulos Disponibles</label>
              <div className="grid grid-cols-2 gap-3">
                {dbModules.map(m => (
                  <label key={m.code} className="flex items-center text-sm text-gray-700 cursor-pointer">
                    <input 
                      type="checkbox" 
                      checked={selectedCustomModules.includes(m.code)}
                      onChange={() => handleModuleToggle(m.code)}
                      className="mr-2 rounded text-purple-600 focus:ring-purple-500"
                    />
                    {m.name}
                  </label>
                ))}
              </div>
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