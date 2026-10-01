import { IconButton, Button } from '../../../components/ui/Button';
import { useState, useEffect } from 'react';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { ErrorState, EmptyState, Skeleton } from '../../../components/ui/Feedback';
import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';
import { useConversations } from '../hooks/useConversations';
import { usePermissions } from '../../../core/auth/permissions';
import { ChevronLeft } from 'lucide-react';

export const InboxPage = () => {
  const { can } = usePermissions();
  const {
    conversations,
    selectedChat,
    messages,
    isLoading,
    isError,
    retry,
    isChangingMode,
    isSending,
    isDeleting,
    setSelectedChat,
    handleTakeOver,
    handleRelease,
    handleSendMessage,
    handleDelete
  } = useConversations();

  const [deleteTarget, setDeleteTarget] = useState<string | null>(null);
  useEffect(() => { setDeleteTarget(null); }, [selectedChat?.id]);

  if (isError) {
    return (
      <ErrorState className="h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl mx-4 md:mx-0" title="Pérdida de Conexión" description="No se pudo conectar con el servidor de NexFlow. Revisa tu conexión e intenta nuevamente." onRetry={retry} />
    );
  }

  if (isLoading) {
    return (
      <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm animate-pulse mx-4 md:mx-0">
        <div className="hidden md:flex w-1/3 border-r border-gray-200 flex-col bg-gray-50">
          <div className="p-4 border-b border-gray-200 bg-white"><Skeleton className="h-6 bg-gray-200 rounded w-1/2" /></div>
          <div className="p-4"><Skeleton className="h-16 bg-gray-200 rounded-lg mb-2" /><Skeleton className="h-16 bg-gray-200 rounded-lg mb-2" /></div>
        </div>
        <div className="w-full md:w-2/3 flex flex-col p-6 justify-end">
          <Skeleton className="h-12 bg-gray-100 rounded-lg w-1/2 mb-4 self-start" />
          <Skeleton className="h-12 bg-gray-200 rounded-lg w-1/2 mb-4 self-end" />
          <Skeleton className="h-12 bg-gray-100 rounded-lg w-full mt-4" />
        </div>
      </div>
    );
  }

  return (
    <>
    <ConfirmDialog isOpen={deleteTarget !== null && deleteTarget === selectedChat?.id} title="Eliminar conversación" description="¿Eliminar permanentemente esta conversación y su historial?" destructive confirmLabel="Eliminar" isLoading={isDeleting} confirmDisabled={!can('CONVERSATIONS', 'DELETE')} onClose={() => setDeleteTarget(null)} onConfirm={handleDelete} />
    <div className="flex h-[calc(100dvh-5rem)] md:h-[calc(100vh-8rem)] bg-white md:border md:border-gray-200 md:rounded-xl overflow-hidden shadow-sm -mx-4 md:mx-0">

      <div className={`w-full md:w-1/3 ${selectedChat ? 'hidden md:block' : 'block'} border-r border-gray-200 h-full`}>
        <ConversationSidebar
          conversations={conversations}
          selectedChat={selectedChat}
          onSelectChat={setSelectedChat}
        />
      </div>

      <div className={`w-full md:w-2/3 flex flex-col relative h-full ${!selectedChat ? 'hidden md:flex' : 'flex'}`}>

        {selectedChat ? (
          <>
            {/* Botón Volver para Móviles */}
            <div className="md:hidden absolute top-4 left-4 z-20">
              <IconButton variant="ghost" label="Volver a conversaciones"
                 onClick={() => setSelectedChat(null)}
                className="p-1 bg-white border border-gray-200 text-gray-600 rounded-full shadow-sm hover:bg-gray-50 flex items-center"
              >
                <ChevronLeft className="w-6 h-6" />
              </IconButton>
            </div>

            <div className="absolute top-20 md:top-4 right-4 z-10">
              <Button variant="ghost"
                onClick={() => setDeleteTarget(selectedChat.id)}
                disabled={isDeleting || !can('CONVERSATIONS', 'DELETE')}
                className="px-3 py-1 bg-red-50 text-red-600 border border-red-200 rounded hover:bg-red-100 transition-colors text-sm font-medium disabled:opacity-50"
                title="Eliminar permanentemente"
              >
                {isDeleting ? 'Eliminando...' : '🗑️ Eliminar Chat'}
              </Button>
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
              <EmptyState icon={<span className="text-2xl">📭</span>} title="Bandeja Vacía" description="No hay conversaciones activas en este momento." />
            ) : (
              <EmptyState title="Selecciona una conversación" description="Elige un chat del panel lateral para ver el historial." />
            )}
          </div>
        )}
      </div>
    </div>
    </>
  );
};
