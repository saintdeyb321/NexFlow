import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  getConversations, getMessages, takeOverConversation, 
  releaseConversation, sendManualMessage, deleteConversation 
} from '../services/conversation.service';
import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';

export const InboxPage = () => {
  const queryClient = useQueryClient();
  const [selectedChatId, setSelectedChatId] = useState<string | null>(null);

  // 🔥 Auditoría (Fase 5): Migración a TanStack Query con polling optimizado a 15 segundos[cite: 2].
  const { 
    data: conversations = [], 
    isLoading: isLoadingConversations, 
    isError: isErrorConversations 
  } = useQuery({
    queryKey: ['conversations'],
    queryFn: () => getConversations(),
    refetchInterval: 15000, 
  });

  const selectedChat = conversations.find(c => c.id === selectedChatId) || null;

  // 🔥 Auditoría (Fase 5): Dependencia estricta de selectedChatId en lugar del objeto completo[cite: 2].
  const { 
    data: messages = [], 
  } = useQuery({
    queryKey: ['messages', selectedChatId],
    queryFn: () => getMessages(selectedChatId!),
    enabled: !!selectedChatId,
    refetchInterval: 15000,
  });

  // Mutaciones gestionadas por caché
  const takeOverMutation = useMutation({
    mutationFn: (id: string) => takeOverConversation(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
  });

  const releaseMutation = useMutation({
    mutationFn: (id: string) => releaseConversation(id),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['conversations'] }),
  });

  const sendMsgMutation = useMutation({
    mutationFn: ({ id, content }: { id: string, content: string }) => sendManualMessage(id, content),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['messages', selectedChatId] });
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
    },
  });

  // 🔥 Auditoría (Fase 5): Mutación de eliminación conectada al endpoint de purga[cite: 2].
  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteConversation(id),
    onSuccess: () => {
      setSelectedChatId(null);
      queryClient.invalidateQueries({ queryKey: ['conversations'] });
    },
  });

  // Controladores de eventos
  const handleTakeOver = () => selectedChatId && takeOverMutation.mutate(selectedChatId);
  const handleRelease = () => selectedChatId && releaseMutation.mutate(selectedChatId);
  const handleSendMessage = (content: string) => selectedChatId && sendMsgMutation.mutate({ id: selectedChatId, content });
  
  const handleDelete = () => {
    if (selectedChatId && window.confirm('¿Estás seguro de eliminar esta conversación y todos sus mensajes de la base de datos?')) {
      deleteMutation.mutate(selectedChatId);
    }
  };

  // 🔥 Auditoría (Fase 5): Diferenciación de estados de error UI. Cero silenciamiento de fallos de red[cite: 2].
  if (isErrorConversations) {
    return (
      <div className="flex h-64 items-center justify-center text-red-500 bg-red-50 rounded-xl border border-red-100">
        No se pudo conectar con NexFlow. Revisa tu conexión o intenta recargar.
      </div>
    );
  }

  if (isLoadingConversations) {
    return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando bandeja de entrada...</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
      <ConversationSidebar 
        conversations={conversations} 
        selectedChat={selectedChat} 
        onSelectChat={(chat) => setSelectedChatId(chat.id)} 
      />
      
      {selectedChat ? (
        <div className="w-2/3 flex flex-col relative">
          
          {/* Botón de eliminación en la cabecera de la vista de mensajes */}
          <div className="absolute top-4 right-4 z-10">
            <button 
              onClick={handleDelete}
              disabled={deleteMutation.isPending}
              className="px-3 py-1 bg-red-50 text-red-600 border border-red-200 rounded hover:bg-red-100 transition-colors text-sm font-medium disabled:opacity-50"
              title="Eliminar permanentemente"
            >
              {deleteMutation.isPending ? 'Eliminando...' : '🗑️ Eliminar Chat'}
            </button>
          </div>
          
          <ConversationThread
            chat={selectedChat}
            messages={messages}
            isChangingMode={takeOverMutation.isPending || releaseMutation.isPending}
            isSending={sendMsgMutation.isPending}
            onTakeOver={handleTakeOver}
            onRelease={handleRelease}
            onSendMessage={handleSendMessage}
          />
        </div>
      ) : (
        <div className="w-2/3 flex flex-col items-center justify-center text-gray-500 bg-gray-50/50">
          {conversations.length === 0 ? (
            <p>No hay conversaciones activas en este momento.</p>
          ) : (
            <p>Selecciona una conversación para ver el historial.</p>
          )}
        </div>
      )}
    </div>
  );
};