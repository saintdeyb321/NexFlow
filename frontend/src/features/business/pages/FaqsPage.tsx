import { useState, useEffect } from 'react';
import { BookOpen, Plus, Trash2 } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { axiosClient } from '../../../core/api/axiosClient';
import type { FaqDto } from '../types/business.types';

export const FaqsPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;

  const [faqs, setFaqs] = useState<FaqDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isSaving, setIsSaving] = useState(false);
  
  const [newFaq, setNewFaq] = useState<Partial<FaqDto>>({
    question: '',
    answer: '',
    category: 'General'
  });

  useEffect(() => {
    if (workspaceId) loadFaqs();
  }, [workspaceId]);

  const loadFaqs = async () => {
    try {
      const { data } = await axiosClient.get<FaqDto[]>(`/workspaces/${workspaceId}/business/faqs`);
      setFaqs(data || []);
    } catch (error) {
      console.error("Error cargando FAQs:", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleAddFaq = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newFaq.question || !newFaq.answer) return;

    setIsSaving(true);
    try {
      const faqToSave: FaqDto = {
        id: crypto.randomUUID(), 
        question: newFaq.question,
        answer: newFaq.answer,
        category: newFaq.category!
      };
      
      await axiosClient.post(`/workspaces/${workspaceId}/business/faqs`, faqToSave);
      setFaqs([...faqs, faqToSave]);
      setNewFaq({ question: '', answer: '', category: 'General' }); 
    } catch (error) {
      console.error("Error guardando FAQ", error);
    } finally {
      setIsSaving(false);
    }
  };

  const handleDelete = async (faqId: string) => {
    if (!window.confirm("¿Eliminar esta pregunta?")) return;
    try {
      await axiosClient.delete(`/workspaces/${workspaceId}/business/faqs/${faqId}`);
      setFaqs(faqs.filter(f => f.id !== faqId));
    } catch (error) {
      console.error("Error eliminando FAQ", error);
    }
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando base de conocimiento...</div>;

  return (
    <div className="max-w-5xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">
          <BookOpen className="w-6 h-6 mr-3 text-blue-600" />
          Base de Conocimiento (IA)
        </h1>
        <p className="mt-1 text-sm text-gray-500">Agrega las preguntas frecuentes de tus clientes. Tu Asistente IA usará esta información para responder automáticamente en WhatsApp.</p>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
        {/* Formulario */}
        <div className="lg:col-span-1">
          <form onSubmit={handleAddFaq} className="bg-white shadow-sm border border-gray-200 rounded-xl p-5 sticky top-6">
            <h3 className="font-semibold text-gray-900 mb-4">Nueva Pregunta</h3>
            
            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-700 mb-1">Categoría</label>
              <select 
                value={newFaq.category}
                onChange={(e) => setNewFaq({ ...newFaq, category: e.target.value })}
                className="w-full px-3 py-2 border border-gray-300 rounded-lg outline-none focus:ring-2 focus:ring-blue-500"
              >
                <option value="General">General</option>
                <option value="Pagos">Pagos</option>
                <option value="Ubicación">Ubicación</option>
                <option value="Políticas">Políticas</option>
              </select>
            </div>

            <div className="mb-4">
              <label className="block text-sm font-medium text-gray-700 mb-1">Pregunta (Usuario)</label>
              <textarea
                rows={2}
                value={newFaq.question}
                onChange={(e) => setNewFaq({ ...newFaq, question: e.target.value })}
                placeholder="Ej: ¿Tienen parqueo?"
                className="w-full px-3 py-2 border border-gray-300 rounded-lg outline-none focus:ring-2 focus:ring-blue-500"
                required
              />
            </div>
            
            <div className="mb-6">
              <label className="block text-sm font-medium text-gray-700 mb-1">Respuesta (Asistente IA)</label>
              <textarea
                rows={4}
                value={newFaq.answer}
                onChange={(e) => setNewFaq({ ...newFaq, answer: e.target.value })}
                placeholder="Ej: Sí, contamos con parqueo gratuito en el sótano."
                className="w-full px-3 py-2 border border-gray-300 rounded-lg outline-none focus:ring-2 focus:ring-blue-500"
                required
              />
            </div>

            <button type="submit" disabled={isSaving} className="w-full flex items-center justify-center px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:opacity-50">
              <Plus className="w-4 h-4 mr-2" />
              {isSaving ? 'Guardando...' : 'Añadir a la IA'}
            </button>
          </form>
        </div>

        {/* Lista de FAQs */}
        <div className="lg:col-span-2 space-y-4">
          {faqs.length === 0 ? (
            <div className="text-center p-8 bg-gray-50 border border-dashed border-gray-300 rounded-xl text-gray-500">
              Tu IA aún no tiene conocimientos específicos de tu negocio.
            </div>
          ) : (
            faqs.map((faq) => (
              <div key={faq.id} className="p-5 bg-white border border-gray-200 rounded-xl hover:shadow-sm">
                <div className="flex justify-between items-start mb-2">
                  <span className="inline-block px-2 py-1 bg-gray-100 text-gray-600 text-xs font-medium rounded">
                    {faq.category}
                  </span>
                  <button onClick={() => handleDelete(faq.id)} className="text-gray-400 hover:text-red-600 transition-colors">
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
                <h4 className="font-semibold text-gray-900 mb-1">Q: {faq.question}</h4>
                <p className="text-gray-600 text-sm">A: {faq.answer}</p>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
};