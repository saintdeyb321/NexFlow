import { Badge, EmptyState } from '../../../components/ui/Feedback';
import { Phone, Bot, User, BellRing, MessageSquare } from 'lucide-react';
import type { Conversation } from '../types/conversation.types';

interface ConversationSidebarProps {
  conversations: Conversation[];
  selectedChat: Conversation | null;
  onSelectChat: (chat: Conversation) => void;
}

export const ConversationSidebar = ({ conversations, selectedChat, onSelectChat }: ConversationSidebarProps) => {
  return (
    <div className="w-full h-full min-h-0 flex flex-col bg-surface">
      <div className="p-5 border-b border-line bg-surface">
        <h2 className="text-base font-semibold text-foreground">Bandeja de Entrada</h2>
      </div>
      <div className="overflow-y-auto flex-1 min-h-0 p-2 space-y-1">
        {conversations.length === 0 ? (
          <EmptyState title="No hay chats recientes" icon={<MessageSquare aria-hidden="true" className="w-6 h-6" />} />
        ) : (
          conversations.map(chat => (
            <button
              key={chat.id}
              onClick={() => onSelectChat(chat)}
              aria-pressed={selectedChat?.id === chat.id}
              className={`w-full text-left p-3 rounded-xl border transition-colors min-h-20 ${
                selectedChat?.id === chat.id ? 'bg-indigo-50 border-indigo-200 shadow-sm' : 'bg-surface border-transparent hover:bg-surface-soft'
              }`}
            >
              <div className="flex justify-between items-start mb-1">
                <span className="font-semibold text-foreground flex items-center min-w-0 break-all">
                  <Phone aria-hidden="true" className="w-4 h-4 mr-2 text-gray-400" />
                  {chat.consumerPhone}
                </span>
                <span className="text-[11px] text-muted font-medium shrink-0 ml-2">
                  {new Date(chat.lastMessageAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
              </div>
              <div className="flex items-center mt-2 justify-between">
                <Badge tone={chat.mode === 'Automatic' ? 'info' : 'neutral'}>
                  {chat.mode === 'Automatic' ? <Bot aria-hidden="true" className="w-3 h-3 mr-1" /> : <User aria-hidden="true" className="w-3 h-3 mr-1" />}
                  {chat.mode === 'Automatic' ? 'IA' : 'Humano'}
                </Badge>

                {chat.mode === 'Human' && chat.handoffReason === 'AiEscalation' && (
                  <span className="flex items-center text-[10px] text-amber-800 font-medium bg-amber-50 px-2 py-1 rounded-full">
                    <BellRing aria-hidden="true" className="w-3 h-3 mr-1" /> Requiere Atención
                  </span>
                )}
              </div>
            </button>
          ))
        )}
      </div>
    </div>
  );
};