import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';
import { useConversations } from '../hooks/useConversations';

export const InboxPage = () => {
  const {
    conversations,
    selectedChat,
    messages,
    isLoading,
    isError,
    isChangingMode,
    isSending,
    isDeleting,
    setSelectedChat,
    handleTakeOver,
    handleRelease,
    handleSendMessage,
    handleDelete
  } = useConversations();

  if (isError) {
    return (
      <div className="flex h-64 items-center justify-center text-red-500 bg-red-50 rounded-xl border border-red-100">
        No se pudo conectar con NexFlow. Revisa tu conexión o intenta recargar.
      </div>
    );
  }

  if (isLoading) {
    return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando bandeja de entrada...</div>;
  }

  return (
    <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
      <ConversationSidebar 
        conversations={conversations} 
        selectedChat={selectedChat} 
        onSelectChat={setSelectedChat} 
      />
      
      {selectedChat ? (
        <div className="w-2/3 flex flex-col relative">
          
          <div className="absolute top-4 right-4 z-10">
            <button 
              onClick={handleDelete}
              disabled={isDeleting}
              className="px-3 py-1 bg-red-50 text-red-600 border border-red-200 rounded hover:bg-red-100 transition-colors text-sm font-medium disabled:opacity-50"
              title="Eliminar permanentemente"
            >
              {isDeleting ? 'Eliminando...' : '🗑️ Eliminar Chat'}
            </button>
          </div>
          
          <ConversationThread
            chat={selectedChat}
            messages={messages}
            isChangingMode={isChangingMode}
            isSending={isSending}
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