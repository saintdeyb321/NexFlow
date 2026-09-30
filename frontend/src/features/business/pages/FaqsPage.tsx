import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { BookOpen, Plus, Trash2, Pencil, MessageSquare } from 'lucide-react';
import { faqService } from '../services/faq.service';
import type { FaqDto } from '../types/business.types';
import { FaqModal } from '../components/FaqModal';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const FaqsPage = () => {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [faqToEdit, setFaqToEdit] = useState<FaqDto | null>(null);
  
  const [notification, setNotification] = useState<{ msg: string, type: 'success' | 'error' } | null>(null);
  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);

  // 🔥 SPRINT 06: La IA usa la base de conocimiento de manera global. No se filtra por sede.
  const { data: faqs = [], isLoading: isFaqsLoading } = useQuery({
    queryKey: ['faqs', workspaceId],
    queryFn: () => faqService.getFaqs('global'), // Reemplazamos la ubicación dinámica
    enabled: !!workspaceId && can('FAQ', 'READ'),
    staleTime: 1000 * 60 * 10,
  });

  const saveMutation = useMutation({
    mutationFn: faqService.saveFaq,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['faqs', workspaceId] });
      setIsModalOpen(false);
      setNotification({ msg: 'Pregunta guardada correctamente.', type: 'success' });
    },
    onError: (error: unknown) => setNotification({ msg: getApiErrorPresentation(error), type: 'error' })
  });

  const deleteMutation = useMutation({
    mutationFn: faqService.deleteFaq,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['faqs', workspaceId] });
      setDeleteConfirmId(null);
      setNotification({ msg: 'Pregunta eliminada exitosamente.', type: 'success' });
    },
    onError: (error: unknown) => setNotification({ msg: getApiErrorPresentation(error), type: 'error' })
  });

  const handleOpenNew = () => {
    setFaqToEdit(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (faq: FaqDto) => {
    setFaqToEdit(faq);
    setIsModalOpen(true);
  };

  const getCategoryColor = (category: string) => {
    switch (category) {
      case 'Pagos': return 'bg-green-100 text-green-700';
      case 'Cómo llegar': return 'bg-orange-100 text-orange-700';
      case 'Políticas': return 'bg-red-100 text-red-700';
      default: return 'bg-blue-100 text-blue-700';
    }
  };

  if (isFaqsLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando base de conocimiento...</div>;

  return (
    <div className="max-w-5xl mx-auto animate-in fade-in">
      
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

      <div className="flex flex-col md:flex-row justify-between items-start md:items-center mb-8 gap-4">
        <div className="flex items-center">
          <div className="w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center mr-4">
             <BookOpen className="w-5 h-5 text-blue-600" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Base de Conocimiento</h1>
            <p className="text-sm text-gray-500 mt-0.5">Entrena al asistente virtual con preguntas frecuentes de tu negocio.</p>
          </div>
        </div>
        
        <button 
          onClick={handleOpenNew}
          disabled={faqs.length >= 20 || !can('FAQ', 'CREATE')}
          className="flex items-center px-5 py-2.5 bg-purple-700 text-white text-sm font-medium rounded-lg hover:bg-purple-800 transition-colors shadow-sm disabled:opacity-50 disabled:bg-gray-400"
        >
          <Plus className="w-4 h-4 mr-2" />
          Nueva Pregunta
        </button>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <div className="flex justify-between items-center border-b border-gray-100 mb-4 pb-2">
          <h3 className="text-sm font-semibold text-gray-700">
            Preguntas Activas Globales ({faqs.length}/20)
          </h3>
        </div>
        
        <div className="space-y-3">
          {faqs.length === 0 ? (
            <div className="text-center py-10 text-gray-500 flex flex-col items-center">
              <MessageSquare className="w-12 h-12 text-gray-200 mb-3" />
              <p>Tu asistente aún no tiene información pre-programada.</p>
              <p className="text-sm mt-1">Haz clic en "Nueva Pregunta" para entrenarlo.</p>
            </div>
          ) : (
            faqs.map((faq) => (
              <div 
                key={faq.id} 
                className="flex flex-col md:flex-row md:items-start justify-between p-5 bg-white border border-gray-200 rounded-xl hover:border-blue-200 hover:shadow-sm transition-all gap-4"
              >
                <div className="flex-1">
                  <div className="flex items-center gap-3 mb-2">
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-semibold ${getCategoryColor(faq.category || '')}`}>
                      {faq.category}
                    </span>
                  </div>
                  <h4 className="font-bold text-gray-900 text-sm md:text-base mb-1">
                    P: {faq.question}
                  </h4>
                  <p className="text-sm text-gray-600 line-clamp-2 md:line-clamp-none">
                    <span className="font-semibold text-gray-800">R:</span> {faq.answer}
                  </p>
                </div>

                <div className="flex items-center space-x-2 md:self-start relative">
                  <button disabled={!can('FAQ', 'UPDATE')} onClick={() => handleOpenEdit(faq)} className="p-2 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors" title="Editar">
                    <Pencil className="w-4 h-4" />
                  </button>
                  <button disabled={!can('FAQ', 'DELETE')} onClick={() => setDeleteConfirmId(faq.id!)} className="p-2 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors" title="Eliminar">
                    <Trash2 className="w-4 h-4" />
                  </button>

                  {deleteConfirmId === faq.id && (
                    <div className="absolute right-0 top-12 bg-white border border-red-200 shadow-xl p-3 rounded-lg z-10 w-48">
                      <p className="text-xs text-red-600 font-medium mb-2">¿Eliminar pregunta?</p>
                      <div className="flex justify-between gap-2">
                        <button onClick={() => setDeleteConfirmId(null)} className="flex-1 text-xs bg-gray-100 py-1 rounded">No</button>
                        <button onClick={() => deleteMutation.mutate(faq.id!)} disabled={deleteMutation.isPending || !can('FAQ', 'DELETE')} className="flex-1 text-xs bg-red-600 text-white py-1 rounded">Sí, borrar</button>
                      </div>
                    </div>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      <FaqModal 
        isOpen={isModalOpen && can('FAQ', faqToEdit ? 'UPDATE' : 'CREATE')}
        onClose={() => setIsModalOpen(false)}
        onSave={async (faq) => { await saveMutation.mutateAsync(faq); }}
        initialData={faqToEdit}
      />
    </div>
  );
};
