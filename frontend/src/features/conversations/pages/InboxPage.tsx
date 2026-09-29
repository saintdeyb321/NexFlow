import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';
import { useConversations } from '../hooks/useConversations';
import { ChevronLeft } from 'lucide-react'; // 🔥 Nuevo icono para volver

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
      <div className="flex h-[calc(100vh-8rem)] items-center justify-center bg-white border border-gray-200 rounded-xl shadow-sm mx-4 md:mx-0">
        <div className="text-center max-w-md">
          <div className="w-16 h-16 bg-red-50 text-red-500 rounded-full flex items-center justify-center mx-auto mb-4">
            <svg xmlns="http://www.w3.org/2000/svg" className="h-8 w-8" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" /></svg>
          </div>
          <h3 className="text-lg font-bold text-gray-900 mb-1">Pérdida de Conexión</h3>
          <p className="text-sm text-gray-500 mb-4">No se pudo conectar con el servidor de NexFlow. Revisa tu conexión a internet e intenta recargar la página.</p>
          <button onClick={() => window.location.reload()} className="px-4 py-2 bg-blue-600 text-white rounded-lg font-medium hover:bg-blue-700 transition-colors">Recargar Bandeja</button>
        </div>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm animate-pulse mx-4 md:mx-0">
        <div className="hidden md:flex w-1/3 border-r border-gray-200 flex-col bg-gray-50">
          <div className="p-4 border-b border-gray-200 bg-white"><div className="h-6 bg-gray-200 rounded w-1/2"></div></div>
          <div className="p-4"><div className="h-16 bg-gray-200 rounded-lg mb-2"></div><div className="h-16 bg-gray-200 rounded-lg mb-2"></div></div>
        </div>
        <div className="w-full md:w-2/3 flex flex-col p-6 justify-end">
          <div className="h-12 bg-gray-100 rounded-lg w-1/2 mb-4 self-start"></div>
          <div className="h-12 bg-gray-200 rounded-lg w-1/2 mb-4 self-end"></div>
          <div className="h-12 bg-gray-100 rounded-lg w-full mt-4"></div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-[calc(100dvh-5rem)] md:h-[calc(100vh-8rem)] bg-white md:border md:border-gray-200 md:rounded-xl overflow-hidden shadow-sm -mx-4 md:mx-0">
      
      {/* 🔥 SPRINT 09: Master-Detail. Si hay chat seleccionado, ocultamos la lista en Móviles */}
      <div className={`w-full md:w-1/3 ${selectedChat ? 'hidden md:block' : 'block'} border-r border-gray-200 h-full`}>
        <ConversationSidebar 
          conversations={conversations} 
          selectedChat={selectedChat} 
          onSelectChat={setSelectedChat} 
        />
      </div>
      
      {/* 🔥 SPRINT 09: Si no hay chat seleccionado, ocultamos el Thread en Móviles */}
      <div className={`w-full md:w-2/3 flex flex-col relative h-full ${!selectedChat ? 'hidden md:flex' : 'flex'}`}>
        
        {selectedChat ? (
          <>
            {/* Botón Volver para Móviles */}
            <div className="md:hidden absolute top-4 left-4 z-20">
              <button 
                onClick={() => setSelectedChat(null as any)} 
                className="p-1 bg-white border border-gray-200 text-gray-600 rounded-full shadow-sm hover:bg-gray-50 flex items-center"
              >
                <ChevronLeft className="w-6 h-6" />
              </button>
            </div>

            <div className="absolute top-4 right-4 z-10 hidden md:block">
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
          </>
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center text-gray-500 bg-gray-50/50">
            {conversations.length === 0 ? (
              <div className="text-center">
                <div className="w-16 h-16 bg-gray-100 rounded-full flex items-center justify-center mx-auto mb-4 text-2xl">📭</div>
                <p className="font-medium text-gray-700">Bandeja Vacía</p>
                <p className="text-sm mt-1">No hay conversaciones activas en este momento.</p>
              </div>
            ) : (
              <div className="text-center">
                <div className="w-16 h-16 bg-blue-50 text-blue-500 rounded-full flex items-center justify-center mx-auto mb-4">
                  <svg xmlns="http://www.w3.org/2000/svg" className="h-8 w-8" fill="none" viewBox="0 0 24 24" stroke="currentColor"><path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" /></svg>
                </div>
                <p className="font-medium text-gray-700">Selecciona una conversación</p>
                <p className="text-sm mt-1">Elige un chat del panel lateral para ver el historial.</p>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
};