import { Button } from '../../../components/ui/Button';
import { Select, Textarea, FormField } from '../../../components/ui/Form';
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
    category: 'General',
    isActive: true
  });

  useEffect(() => {
    if (initialData) {
      setFormData(initialData);
    } else {
      setFormData({ question: '', answer: '', category: 'General', isActive: true });
    }
  }, [initialData, isOpen]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.question || !formData.answer) return;

    setIsSaving(true);
    try {
      const faqToSave: FaqDto = {
        ...(formData.id ? { id: formData.id } : {}),
        question: formData.question,
        answer: formData.answer,
        category: formData.category || 'General',
        isActive: formData.isActive ?? true
      };

      await onSave(faqToSave);
      onClose();
    } catch {
      // The save mutation owner reports API failures through Toast.
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={initialData ? 'Editar Pregunta' : 'Nueva Pregunta'} closeDisabled={isSaving}
    >
      <form onSubmit={handleSubmit} className="space-y-4">
        <FormField label="Categoría">
          <Select
            value={formData.category || ''}
            onChange={(e) => setFormData({ ...formData, category: e.target.value })}
            className="w-full border focus:ring-primary transition-all cursor-pointer"
          >
            <option value="General">General</option>
            <option value="Pagos">Pagos</option>
            <option value="Cómo llegar">Indicaciones / Cómo llegar</option>
            <option value="Políticas">Políticas</option>
          </Select>
        </FormField>

        <FormField required label="Pregunta (Lo que diría el usuario)">
          {control => (<div className="relative">
            <div className="absolute top-3 left-3 text-gray-400">
              <HelpCircle aria-hidden="true" className="w-5 h-5" />
            </div>
            <Textarea {...control}
              rows={2}
              value={formData.question}
              onChange={(e) => setFormData({ ...formData, question: e.target.value })}
              placeholder="Ej: ¿Tienen estacionamiento disponible?"
              className="w-full pl-10 pr-4 border focus:ring-primary transition-all"
              required
            />
          </div>)}
        </FormField>

        <FormField label="Respuesta (Lo que dirá la IA)">
          <Textarea
            rows={4}
            value={formData.answer}
            onChange={(e) => setFormData({ ...formData, answer: e.target.value })}
            placeholder="Ej: Sí, contamos con estacionamiento gratuito para clientes en el sótano del edificio."
            className="w-full border focus:ring-primary transition-all"
            required
          />
        </FormField>

        <div className="pt-6 border-t border-line flex justify-end gap-3">
          <Button variant="secondary" type="button" disabled={isSaving} onClick={onClose} className="text-sm font-medium transition-colors">
            Cancelar
          </Button>
          <Button variant="primary" isLoading={isSaving} type="submit" disabled={isSaving} className="flex items-center text-sm font-medium transition-colors disabled:opacity-50">
            <Save aria-hidden="true" className="w-4 h-4 mr-2" />
            {isSaving ? 'Guardando...' : 'Guardar'}
          </Button>
        </div>
      </form>
    </Modal>
  );
};
