import { useLocation } from 'react-router-dom';
import { IconButton, Button } from '../../../components/ui/Button';
import { useState } from 'react';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { Alert, ErrorState, EmptyState, Skeleton } from '../../../components/ui/Feedback';
import { ConversationSidebar } from '../components/ConversationSidebar';
import { ConversationThread } from '../components/ConversationThread';
import { useConversations } from '../hooks/useConversations';
import { usePermissions } from '../../../core/auth/permissions';
import { ChevronLeft, Trash2, MessageSquare } from 'lucide-react';

export const InboxPage = () => {
  const { can } = usePermissions();
  const { search } = useLocation();
  const showMobileList = !new URLSearchParams(search).get('conversation');
  const {
    conversations,
    selectedChat,
    messages,
    identityKey, accessDenied, sessionExpired, canInteract,
    isLoadingConversations, isLoadingMessages, isErrorConversations, isErrorMessages,
    isFetchingConversations, isFetchingMessages, retryConversations, retryMessages, retryAuthorization,
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
  const chatIdentity = `${identityKey}:${selectedChat?.id}`;
  const [lastChatId, setLastChatId] = useState(chatIdentity);
  if (lastChatId !== chatIdentity) {
    setLastChatId(chatIdentity);
    setDeleteTarget(null);
  }

  if (accessDenied) {
    return (
      <ErrorState className="nf-panel h-[calc(100dvh-10rem)]" title={sessionExpired ? 'Tu sesión ha expirado' : 'Acceso no disponible'}
        description="No se mostrará información almacenada hasta verificar el acceso a las conversaciones." onRetry={retryAuthorization} />
    );
  }

  return (
    <>
    <ConfirmDialog isOpen={deleteTarget !== null && deleteTarget === selectedChat?.id} title="Eliminar conversación" description="¿Eliminar permanentemente esta conversación y su historial?" destructive confirmLabel="Eliminar" isLoading={isDeleting} confirmDisabled={!canInteract || !can('CONVERSATIONS', 'DELETE')} onClose={() => setDeleteTarget(null)} onConfirm={handleDelete} />
    <div className="nf-panel flex h-[calc(100dvh-6.5rem)] sm:h-[calc(100dvh-8.5rem)] min-h-80 overflow-hidden">

      <div className={`w-full md:w-[320px] xl:w-[350px] md:shrink-0 ${selectedChat && !showMobileList ? 'hidden md:flex' : 'flex'} flex-col border-r border-line h-full min-h-0`}>
        {isErrorConversations && <QueryNotice label="conversaciones" hasData={conversations.length > 0} fetching={isFetchingConversations} onRetry={retryConversations} />}
        {isLoadingConversations && <Skeleton className="m-4 h-16" />}
        <div className="flex-1 min-h-0">
        <ConversationSidebar
          conversations={conversations}
          selectedChat={selectedChat}
          onSelectChat={setSelectedChat}
        />
        </div>
      </div>

      <div className={`w-full min-w-0 md:flex-1 flex flex-col relative h-full ${!selectedChat || showMobileList ? 'hidden md:flex' : 'flex'}`}>

        {isErrorMessages && <QueryNotice label="mensajes" hasData={messages.length > 0} fetching={isFetchingMessages} onRetry={retryMessages} />}
        {isLoadingMessages && <Skeleton className="m-4 h-12" />}
        {selectedChat ? (
          <>
            <div className="flex items-center justify-between px-3 py-1 border-b border-line bg-surface-soft">
              <IconButton label="Volver a conversaciones" onClick={() => setSelectedChat(null)} className="md:hidden"><ChevronLeft aria-hidden="true" className="w-5 h-5" /></IconButton>
              <span className="hidden md:block text-xs text-muted px-2">Conversación</span>
              <Button variant="ghost" onClick={() => setDeleteTarget(selectedChat.id)} disabled={!canInteract || isDeleting || !can('CONVERSATIONS', 'DELETE')} className="text-danger" title="Eliminar permanentemente">
                <Trash2 aria-hidden="true" className="w-4 h-4" /> {isDeleting ? 'Eliminando...' : 'Eliminar'}
              </Button>
            </div>
            <ConversationThread
              key={chatIdentity}
              chat={selectedChat}
              messages={messages}
              isChangingMode={isChangingMode}
              isSending={isSending}
              readOnly={!canInteract}
              onTakeOver={handleTakeOver}
              onRelease={handleRelease}
              onSendMessage={handleSendMessage}
            />
          </>
        ) : (
          <div className="flex-1 flex flex-col items-center justify-center text-muted bg-surface-soft/50">
            {isErrorConversations && conversations.length === 0 ? (
              <EmptyState title="Conversaciones no disponibles" description="Reintenta la lista para recuperar la bandeja." />
            ) : conversations.length === 0 ? (
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

const QueryNotice = ({ label, hasData, fetching, onRetry }: { label: string; hasData: boolean; fetching: boolean; onRetry: () => void }) => (
  <Alert tone="warning" className="m-2 shrink-0">
    <p>No se pudieron actualizar los {label}. {hasData ? 'Los datos pueden estar desactualizados.' : 'No hay datos disponibles para esta consulta.'} Las acciones están pausadas hasta actualizar.</p>
    <Button variant="secondary" className="mt-2" onClick={onRetry} disabled={fetching} aria-label={`Reintentar ${label}`}>
      {fetching ? 'Actualizando…' : `Reintentar ${label}`}
    </Button>
  </Alert>
);
