import { Button } from '../../../components/ui/Button';
import { Input, Select, FormField } from '../../../components/ui/Form';
import { Alert } from '../../../components/ui/Feedback';
import { useState, useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
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

  const me = useAuthStore(state => state.me);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const { data: dbTemplates = [], error: templatesError } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.system.templates(me?.user.id),
    queryFn: ({ signal }) => getSystemTemplates(signal),
    enabled: isOpen && me?.user.isSuperAdmin === true,
  });
  const { data: dbModules = [], error: modulesError } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.system.modules(me?.user.id),
    queryFn: ({ signal }) => getSystemModules(signal),
    enabled: isOpen && me?.user.isSuperAdmin === true,
  });
  const [selectedCustomModules, setSelectedCustomModules] = useState<string[]>([]);

  const [formData, setFormData] = useState({
    email: '',
    templateCode: '',
    expiresAt: '',
    maxLocations: 1
  });

  useEffect(() => {
    if (isOpen) {
      setErrorMessage(null);
      const defaultDate = new Date();
      defaultDate.setFullYear(defaultDate.getFullYear() + 1);
      setFormData(prev => ({ ...prev, expiresAt: defaultDate.toISOString().split('T')[0] }));
    }
  }, [isOpen]);

  const templateCode = formData.templateCode || dbTemplates[0]?.code || '';

  const handleModuleToggle = (code: string) => {
    setSelectedCustomModules(prev =>
      prev.includes(code) ? prev.filter(c => c !== code) : [...prev, code]
    );
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    const payload: ProvisionWorkspaceRequest = {
      email: formData.email,
      workspaceName: "Negocio por Configurar",
      expiresAt: new Date(formData.expiresAt).toISOString(),
      maxLocations: Number(formData.maxLocations)
    };

    if (provisionMode === 'template') {
      payload.templateCode = templateCode;
    } else {
      if (selectedCustomModules.length === 0) { setErrorMessage('Selecciona al menos 1 módulo custom'); return; }
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
    <Modal isOpen={isOpen} onClose={onClose} title="Nuevo Inquilino (Tenant)" closeDisabled={isProvisioning} maxWidth="max-w-lg">
      <form onSubmit={handleSubmit} className="space-y-4">

        <FormField label="Correo del Dueño (Google Auth)">
          <Input type="email" required value={formData.email} onChange={e => setFormData({...formData, email: e.target.value})} className="w-full border focus:ring-purple-500 transition-all" placeholder="cliente@gmail.com" />
        </FormField>

        <div role="group" aria-label="Modalidad de Licencia" className="pt-4 border-t border-line">
          <p className="block text-sm font-bold mb-3 text-foreground">Modalidad de Licencia</p>
          <div className="flex gap-4 mb-4">
            <label className="flex items-center cursor-pointer text-sm font-medium text-gray-700">
              <Input type="radio" name="mode" checked={provisionMode === 'template'} onChange={() => setProvisionMode('template')} className="mr-2 w-4 h-4 focus:ring-purple-500" />
              Por Plantilla
            </label>
            <label className="flex items-center cursor-pointer text-sm font-medium text-gray-700">
              <Input type="radio" name="mode" checked={provisionMode === 'custom'} onChange={() => setProvisionMode('custom')} className="mr-2 w-4 h-4 focus:ring-purple-500" />
              A la carta
            </label>
          </div>

          {provisionMode === 'template' ? (
            <div className="animate-in fade-in slide-in-from-top-1">
              <Select aria-label="Plantilla de licencia"
                value={templateCode}
                onChange={e => setFormData({...formData, templateCode: e.target.value})}
                className="w-full border focus:ring-purple-500 transition-all cursor-pointer"
              >
                {dbTemplates.map(t => (
                  <option key={t.code} value={t.code}>{t.name} ({t.code})</option>
                ))}
              </Select>
            </div>
          ) : (
            <div role="group" aria-label="Módulos Disponibles" className="animate-in fade-in slide-in-from-top-1 bg-surface-soft p-4 rounded-xl border border-line">
              <p className="block text-xs font-semibold text-muted uppercase tracking-wider mb-3">Módulos Disponibles</p>
              <div className="grid gap-3 grid-cols-1 sm:grid-cols-2">
                {dbModules.map(m => (
                  <label key={m.code} className="flex items-center text-sm text-gray-700 cursor-pointer">
                    <Input
                      type="checkbox"
                      checked={selectedCustomModules.includes(m.code)}
                      onChange={() => handleModuleToggle(m.code)}
                      className="mr-2 focus:ring-purple-500"
                    />
                    {m.name}
                  </label>
                ))}
              </div>
            </div>
          )}
        </div>

        <div className="grid gap-4 border-t border-line pt-4 mt-2 grid-cols-1 sm:grid-cols-2">
          <FormField label="Límite de Sedes">
            <Input type="number" min="1" max="50" required value={formData.maxLocations} onChange={e => setFormData({...formData, maxLocations: parseInt(e.target.value)})} className="w-full border focus:ring-purple-500 transition-all" />
          </FormField>
          <FormField label="Vencimiento">
            <Input type="date" required value={formData.expiresAt} onChange={e => setFormData({...formData, expiresAt: e.target.value})} className="w-full border focus:ring-purple-500 transition-all" />
          </FormField>
        </div>

        {(errorMessage || templatesError || modulesError) && <Alert tone="error">{errorMessage || getApiErrorPresentation(templatesError || modulesError)}</Alert>}
        <div className="pt-6 border-t border-line flex justify-end gap-3">
          <Button variant="secondary" type="button" disabled={isProvisioning} onClick={onClose} className="text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={isProvisioning} type="submit" disabled={isProvisioning || (provisionMode === 'template' && !templateCode)} className="flex items-center text-sm font-medium transition-colors disabled:opacity-50">
            <Save aria-hidden="true" className="w-4 h-4 mr-2" />
            {isProvisioning ? 'Procesando...' : 'Aprovisionar Cliente'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
