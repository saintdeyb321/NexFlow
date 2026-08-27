import { useState, useEffect } from 'react';
import { BookOpen, Plus, Trash2, Pencil, MessageSquare } from 'lucide-react';
import { faqService } from '../services/faq.service';
import type { FaqDto } from '../types/business.types';
import { FaqModal } from '../components/FaqModal';
import { useCacheStore } from '../../../core/store/useCacheStore';

export const FaqsPage = () => {
  const { faqs, isFaqsLoading, fetchFaqs, setFaqs } = useCacheStore();
  
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [faqToEdit, setFaqToEdit] = useState<FaqDto | null>(null);

  useEffect(() => {
    fetchFaqs();
  }, []);

  const handleOpenNew = () => {
    setFaqToEdit(null);
    setIsModalOpen(true);
  };

  const handleOpenEdit = (faq: FaqDto) => {
    setFaqToEdit(faq);
    setIsModalOpen(true);
  };

  const handleSaveFaq = async (faq: FaqDto) => {
    try {
      await faqService.saveFaq(faq);
      
      const currentFaqs = faqs || [];
      const exists = currentFaqs.find(f => f.id === faq.id);
      
      if (exists) {
        setFaqs(currentFaqs.map(f => f.id === faq.id ? faq : f));
      } else {
        setFaqs([...currentFaqs, faq]);
      }
    } catch (error: any) {
      alert(`No se pudo guardar: ${error.response?.data?.error || error.message}`);
      throw error; 
    }
  };

  const handleDelete = async (faqId: string) => {
    if (!window.confirm("¿Seguro que deseas eliminar esta pregunta de la IA?")) return;
    try {
      await faqService.deleteFaq(faqId);
      // 🔥 Actualización optimista en caché
      setFaqs((faqs || []).filter(f => f.id !== faqId));
    } catch (error: any) {
      alert(`Error al eliminar: ${error.response?.data?.error || error.message}`);
    }
  };

  const getCategoryColor = (category: string) => {
    switch (category) {
      case 'Pagos': return 'bg-green-100 text-green-700';
      case 'Cómo llegar': return 'bg-orange-100 text-orange-700';
      case 'Políticas': return 'bg-red-100 text-red-700';
      default: return 'bg-blue-100 text-blue-700';
    }
  };

  if (isFaqsLoading && !faqs) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando base de conocimiento...</div>;

  const displayFaqs = faqs || [];

  return (
    <div className="max-w-5xl">
      <div className="flex flex-col md:flex-row justify-between items-start md:items-center mb-8 gap-4">
        <div className="flex items-center">
          <div className="w-10 h-10 bg-blue-100 rounded-lg flex items-center justify-center mr-4">
             <BookOpen className="w-5 h-5 text-blue-600" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900">Base de Conocimiento</h1>
            <p className="text-sm text-gray-500 mt-0.5">La información que tu IA usará para responder.</p>
          </div>
        </div>
        
        <button 
          onClick={handleOpenNew}
          className="flex items-center px-5 py-2.5 bg-purple-700 text-white text-sm font-medium rounded-lg hover:bg-purple-800 transition-colors shadow-sm"
        >
          <Plus className="w-4 h-4 mr-2" />
          Nueva Pregunta
        </button>
      </div>

      <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6">
        <div className="flex justify-between items-center border-b border-gray-100 mb-4 pb-2">
          <h3 className="text-sm font-semibold text-gray-700">
            Preguntas Activas ({displayFaqs.length}/30)
          </h3>
        </div>
        
        <div className="space-y-3">
          {displayFaqs.length === 0 ? (
            <div className="text-center py-10 text-gray-500 flex flex-col items-center">
              <MessageSquare className="w-12 h-12 text-gray-200 mb-3" />
              <p>Tu IA aún no tiene conocimientos específicos.</p>
              <p className="text-sm mt-1">Haz clic en "Nueva Pregunta" para entrenarla.</p>
            </div>
          ) : (
            displayFaqs.map((faq) => (
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

                <div className="flex items-center space-x-2 md:self-start">
                  <button
                    onClick={() => handleOpenEdit(faq)}
                    className="p-2 text-gray-500 bg-gray-50 hover:bg-blue-50 hover:text-blue-600 rounded-full transition-colors"
                    title="Editar"
                  >
                    <Pencil className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => handleDelete(faq.id!)}
                    className="p-2 text-gray-400 bg-gray-50 hover:bg-red-50 hover:text-red-600 rounded-full transition-colors"
                    title="Eliminar"
                  >
                    <Trash2 className="w-4 h-4" />
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </div>

      <FaqModal 
        isOpen={isModalOpen} 
        onClose={() => setIsModalOpen(false)}
        onSave={handleSaveFaq}
        initialData={faqToEdit}
      />
    </div>
  );
};