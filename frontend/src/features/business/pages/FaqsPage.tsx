import { Button, IconButton } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { LoadingState, EmptyState, ErrorState } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/Toast';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { usePermissions } from '../../../core/auth/permissions';

import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { BookOpen, Plus, Trash2, Pencil, MessageSquare } from 'lucide-react';
import { faqService } from '../services/faq.service';
import type { FaqDto } from '../types/business.types';
import { FaqModal } from '../components/FaqModal';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const FaqsPage = () => {
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [faqToEdit, setFaqToEdit] = useState<FaqDto | null>(null);

  const [deleteConfirmId, setDeleteConfirmId] = useState<string | null>(null);
  const { data: faqs = [], isLoading: isFaqsLoading, isError, refetch } = useQuery({
    ...queryPolicies.stable,
    queryKey: queryKeys.faqs.all(workspaceId),
    queryFn: ({ signal }) => faqService.getFaqs('global', signal), // Reemplazamos la ubicación dinámica
    enabled: !!workspaceId && can('FAQ', 'READ'),

  });

  const saveMutation = useSessionMutation({
    mutationFn: faqService.saveFaq,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.faqs.all(workspaceId) });
      setIsModalOpen(false);
      toast.success('Pregunta guardada correctamente.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
  });

  const deleteMutation = useSessionMutation({
    mutationFn: faqService.deleteFaq,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.faqs.all(workspaceId) });
      setDeleteConfirmId(null);
      toast.success('Pregunta eliminada exitosamente.');
    },
    onError: (error: unknown) => toast.toastApiError(error)
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

  if (isFaqsLoading) return <LoadingState title="Cargando base de conocimiento..." />;
  if (isError) return <ErrorState onRetry={() => void refetch()} />;

  return (
    <div className="max-w-5xl mx-auto animate-in fade-in">

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

        <Button variant="ghost"
          onClick={handleOpenNew}
          disabled={faqs.length >= 20 || !can('FAQ', 'CREATE')}
          className="flex items-center px-5 py-2.5 bg-purple-700 text-white text-sm font-medium rounded-lg hover:bg-purple-800 transition-colors shadow-sm disabled:opacity-50 disabled:bg-gray-400"
        >
          <Plus className="w-4 h-4 mr-2" />
          Nueva Pregunta
        </Button>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <div className="flex justify-between items-center border-b border-gray-100 mb-4 pb-2">
          <h3 className="text-sm font-semibold text-gray-700">
            Preguntas Activas Globales ({faqs.length}/20)
          </h3>
        </div>

        <div className="space-y-3">
          {faqs.length === 0 ? (
            <EmptyState icon={<MessageSquare className="w-12 h-12" />} title="Tu asistente aún no tiene información pre-programada." description={'Haz clic en "Nueva Pregunta" para entrenarlo.'} />
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
                  <IconButton variant="ghost" label="Editar" disabled={!can('FAQ', 'UPDATE')} onClick={() => handleOpenEdit(faq)} className="p-2 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors" title="Editar">
                    <Pencil className="w-4 h-4" />
                  </IconButton>
                  <IconButton variant="ghost" label="Eliminar" disabled={!can('FAQ', 'DELETE')} onClick={() => setDeleteConfirmId(faq.id!)} className="p-2 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors" title="Eliminar">
                    <Trash2 className="w-4 h-4" />
                  </IconButton>

                </div>
              </div>
            ))
          )}
        </div>
      </div>

      <ConfirmDialog isOpen={deleteConfirmId !== null} title="Eliminar pregunta" description="¿Eliminar esta pregunta?" destructive confirmLabel="Eliminar" isLoading={deleteMutation.isPending} confirmDisabled={!can('FAQ', 'DELETE')} onClose={() => setDeleteConfirmId(null)} onConfirm={() => { if (deleteConfirmId && can('FAQ', 'DELETE')) deleteMutation.mutate(deleteConfirmId); }} />
      <FaqModal
        isOpen={isModalOpen && can('FAQ', faqToEdit ? 'UPDATE' : 'CREATE')}
        onClose={() => setIsModalOpen(false)}
        onSave={async (faq) => { await saveMutation.mutateAsync(faq); }}
        initialData={faqToEdit}
      />
    </div>
  );
};
