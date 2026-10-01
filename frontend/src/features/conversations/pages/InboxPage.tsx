import { useLocation } from 'react-router-dom';
import { IconButton, Button } from '../../../components/ui/Button';
import { useState, useEffect } from 'react';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { ErrorState, EmptyState, Skeleton } from '../../../components/ui/Feedback';
import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';
import { useConversations } from '../hooks/useConversations';
import { usePermissions } from '../../../core/auth/permissions';
import { ChevronLeft, Trash2, MessageSquare } from 'lucide-react';

export const InboxPage = () => {
  const { can } = usePermissions();
  const { search } = useLocation();
  // View state only: the stable hook keeps conversation selection and URL ownership.
  const [showMobileList, setShowMobileList] = useState(() => !new URLSearchParams(search).has('conversation'));
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
      <ErrorState className="nf-panel h-[calc(100dvh-10rem)]" title="Pérdida de Conexión" description="No se pudo conectar con el servidor de NexFlow. Revisa tu conexión e intenta nuevamente." onRetry={retry} />
    );
  }

  if (isLoading) {
    return (
      <div className="nf-panel flex h-[calc(100dvh-10rem)] overflow-hidden">
        <div className="hidden md:flex w-[320px] shrink-0 border-r border-line flex-col bg-surface-soft">
          <div className="p-4 border-b border-line bg-surface"><Skeleton className="h-6 bg-gray-200 rounded w-1/2" /></div>
          <div className="p-4"><Skeleton className="h-16 bg-gray-200 rounded-lg mb-2" /><Skeleton className="h-16 bg-gray-200 rounded-lg mb-2" /></div>
        </div>
        <div className="w-full min-w-0 md:flex-1 flex flex-col p-6 justify-end">
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
    <div className="nf-panel flex h-[calc(100dvh-6.5rem)] sm:h-[calc(100dvh-8.5rem)] min-h-80 overflow-hidden">

      <div className={`w-full md:w-[320px] xl:w-[350px] md:shrink-0 ${selectedChat && !showMobileList ? 'hidden md:block' : 'block'} border-r border-line h-full`}>
        <ConversationSidebar
          conversations={conversations}
          selectedChat={selectedChat}
          onSelectChat={chat => { setSelectedChat(chat); setShowMobileList(false); }}
        />
      </div>

      <div className={`w-full min-w-0 md:flex-1 flex flex-col relative h-full ${!selectedChat || showMobileList ? 'hidden md:flex' : 'flex'}`}>

        {selectedChat ? (
          <>
            <div className="flex items-center justify-between px-3 py-1 border-b border-line bg-surface-soft">
              <IconButton label="Volver a conversaciones" onClick={() => setShowMobileList(true)} className="md:hidden"><ChevronLeft aria-hidden="true" className="w-5 h-5" /></IconButton>
              <span className="hidden md:block text-xs text-muted px-2">Conversación</span>
              <Button variant="ghost" onClick={() => setDeleteTarget(selectedChat.id)} disabled={isDeleting || !can('CONVERSATIONS', 'DELETE')} className="text-danger" title="Eliminar permanentemente">
                <Trash2 aria-hidden="true" className="w-4 h-4" /> {isDeleting ? 'Eliminando...' : 'Eliminar'}
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
          <div className="flex-1 flex flex-col items-center justify-center text-muted bg-surface-soft/50">
            {conversations.length === 0 ? (
              <EmptyState icon={<span className="text-2xl">📭</span>} title="Bandeja Vacía" description="No hay conversaciones activas en este momento." />
            ) : (
              <EmptyState icon={<MessageSquare aria-hidden="true" className="w-6 h-6" />} title="Selecciona una conversación" description="Elige un chat del panel lateral para ver el historial." />
            )}
          </div>
        )}
      </div>
    </div>
    </>
  );
};
