import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { X, Save } from 'lucide-react';
import { createRequest } from '../services/request.service';
import type { RequestType } from '../types/request.types';

interface CreateRequestModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export const CreateRequestModal = ({ isOpen, onClose }: CreateRequestModalProps) => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  
  const [formData, setFormData] = useState({
    type: 'Tramite' as RequestType,
    title: '',
    description: '',
    consumerPhone: ''
  });

  const createMutation = useMutation({
    mutationFn: createRequest,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests', workspaceId] });
      onClose();
      setFormData({ type: 'Tramite', title: '', description: '', consumerPhone: '' });
      setErrorMessage(null);
    },
    onError: (error: unknown) => {
      setErrorMessage(getApiErrorPresentation(error));
    }
  });

  if (!isOpen) return null;

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!can('REQUESTS', 'CREATE')) return;
    setErrorMessage(null);
    createMutation.mutate({
      ...formData,
      conversationId: 'MANUAL_ENTRY'
    });
  };

  return (
    <div className="fixed inset-0 bg-black/50 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden animate-in fade-in zoom-in-95">
        <div className="flex justify-between items-center px-6 py-4 border-b bg-gray-50">
          <h3 className="text-lg font-bold text-gray-800">Nueva Solicitud Manual</h3>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600 transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {errorMessage && (
          <div className="mx-6 mt-4 p-3 bg-red-50 border border-red-200 text-red-700 text-sm rounded-lg">
            {errorMessage}
          </div>
        )}

        <form onSubmit={handleSubmit} className="p-6 space-y-4">
          <div>
            <label className="block text-sm font-medium mb-1">Tipo de Solicitud</label>
            <select 
              value={formData.type} 
              onChange={e => setFormData({...formData, type: e.target.value as RequestType})} 
              className="w-full border border-gray-300 rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm bg-white"
            >
              <option value="Tramite">Trámite Administrativo</option>
              <option value="CommercialInquiry">Consulta Comercial</option>
              <option value="Support">Soporte Técnico</option>
              <option value="Other">Otro / General</option>
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Teléfono del Cliente (Referencia)</label>
            <input 
              type="text" 
              value={formData.consumerPhone} 
              onChange={e => setFormData({...formData, consumerPhone: e.target.value})} 
              placeholder="Ej: +51987654321" 
              className="w-full border border-gray-300 rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" 
              required 
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Título corto</label>
            <input 
              type="text" 
              value={formData.title} 
              onChange={e => setFormData({...formData, title: e.target.value})} 
              placeholder="Ej: Solicitud de cotización mayorista" 
              className="w-full border border-gray-300 rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm" 
              required 
            />
          </div>

          <div>
            <label className="block text-sm font-medium mb-1">Descripción detallada</label>
            <textarea 
              rows={4}
              value={formData.description} 
              onChange={e => setFormData({...formData, description: e.target.value})} 
              placeholder="Describe el requerimiento del cliente..." 
              className="w-full border border-gray-300 rounded-lg px-3 py-2 outline-none focus:ring-2 focus:ring-blue-500 text-sm resize-none" 
              required 
            />
          </div>

          <div className="pt-4 flex justify-end space-x-3 border-t border-gray-100">
            <button type="button" onClick={onClose} className="px-4 py-2 text-gray-600 hover:bg-gray-100 rounded-lg text-sm font-medium transition-colors">
              Cancelar
            </button>
            <button type="submit" disabled={createMutation.isPending || !can('REQUESTS', 'CREATE')} className="flex items-center px-4 py-2 bg-blue-600 text-white rounded-lg text-sm font-medium hover:bg-blue-700 disabled:opacity-50 transition-colors shadow-sm">
              <Save className="w-4 h-4 mr-2" />
              {createMutation.isPending ? 'Guardando...' : 'Crear Solicitud'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
