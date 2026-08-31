import { useState, useEffect } from 'react';
import { Save, HelpCircle } from 'lucide-react';
import { Modal } from '../../../components/ui/Modal';
import type { FaqDto } from '../types/business.types';

interface FaqModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (faq: FaqDto) => Promise<void>;
  initialData?: FaqDto | null;
}

export const FaqModal = ({ isOpen, onClose, onSave, initialData }: FaqModalProps) => {
  const [isSaving, setIsSaving] = useState(false);
  const [formData, setFormData] = useState<Partial<FaqDto>>({
    question: '',
    answer: '',
    category: 'General'
  });

  // Cargar datos si estamos en modo edición
  useEffect(() => {
    if (initialData) {
      setFormData(initialData);
    } else {
      setFormData({ question: '', answer: '', category: 'General' });
    }
  }, [initialData, isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.question || !formData.answer) return;

    setIsSaving(true);
    try {
      const faqToSave: FaqDto = {
        id: formData.id || crypto.randomUUID(), // El backend respetará el ID si es edición
        question: formData.question,
        answer: formData.answer,
        category: formData.category || 'General'
      };
      
      await onSave(faqToSave);
      onClose();
    } catch (error) {
      console.error(error);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal 
      isOpen={isOpen} 
      onClose={onClose} 
      title={initialData ? 'Editar Pregunta' : 'Nueva Pregunta'}
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Categoría</label>
          <select 
            value={formData.category || ''}
            onChange={(e) => setFormData({ ...formData, category: e.target.value })}
            className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all cursor-pointer"
          >
            <option value="General">General</option>
            <option value="Pagos">Pagos</option>
            <option value="Cómo llegar">Indicaciones / Cómo llegar</option>
            <option value="Políticas">Políticas</option>
          </select>
        </div>

        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Pregunta (Lo que diría el usuario) *</label>
          <div className="relative">
            <div className="absolute top-3 left-3 text-gray-400">
              <HelpCircle className="w-5 h-5" />
            </div>
            <textarea
              rows={2}
              value={formData.question}
              onChange={(e) => setFormData({ ...formData, question: e.target.value })}
              placeholder="Ej: ¿Tienen estacionamiento disponible?"
              className="w-full pl-10 pr-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
              required
            />
          </div>
        </div>
        
        <div>
          <label className="block text-sm font-medium text-gray-700 mb-1">Respuesta (Lo que dirá la IA) *</label>
          <textarea
            rows={4}
            value={formData.answer}
            onChange={(e) => setFormData({ ...formData, answer: e.target.value })}
            placeholder="Ej: Sí, contamos con estacionamiento gratuito para clientes en el sótano del edificio."
            className="w-full px-4 py-2 bg-gray-50 border border-gray-200 rounded-xl focus:bg-white focus:ring-2 focus:ring-blue-500 outline-none transition-all"
            required
          />
        </div>

        <div className="pt-6 border-t border-gray-100 flex justify-end gap-3">
          <button type="button" onClick={onClose} className="px-5 py-2.5 text-sm font-medium text-gray-600 hover:bg-gray-100 rounded-xl transition-colors">
            Cancelar
          </button>
          <button type="submit" disabled={isSaving} className="flex items-center px-5 py-2.5 text-sm font-medium text-white bg-blue-600 hover:bg-blue-700 rounded-xl transition-colors disabled:opacity-50">
            <Save className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </button>
        </div>
      </form>
    </Modal>
  );
};