import { Phone, Bot, User } from 'lucide-react';
import type { Conversation } from '../types/conversation.types';

interface ConversationSidebarProps {
  conversations: Conversation[];
  selectedChat: Conversation | null;
  onSelectChat: (chat: Conversation) => void;
}

export const ConversationSidebar = ({ conversations, selectedChat, onSelectChat }: ConversationSidebarProps) => {
  return (
    <div className="w-1/3 border-r border-gray-200 flex flex-col bg-gray-50">
      <div className="p-4 border-b border-gray-200 bg-white">
        <h2 className="text-lg font-bold text-gray-800">Bandeja de Entrada</h2>
      </div>
      <div className="overflow-y-auto flex-1">
        {conversations.length === 0 ? (
          <p className="p-6 text-center text-sm text-gray-500">No hay conversaciones activas.</p>
        ) : (
          conversations.map(chat => (
            <button
              key={chat.id}
              onClick={() => onSelectChat(chat)}
              className={`w-full text-left p-4 border-b border-gray-100 hover:bg-gray-100 transition-colors ${
                selectedChat?.id === chat.id ? 'bg-blue-50 border-blue-100' : ''
              }`}
            >
              <div className="flex justify-between items-start mb-1">
                <span className="font-medium text-gray-900 flex items-center">
                  <Phone className="w-4 h-4 mr-2 text-gray-400" />
                  {chat.consumerPhone}
                </span>
                <span className="text-xs text-gray-500">
                  {new Date(chat.lastMessageAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </span>
              </div>
              <div className="flex items-center mt-2">
                <span className={`text-xs px-2 py-0.5 rounded-full font-medium flex items-center ${
                  chat.mode === 'Automatic' ? 'bg-green-100 text-green-700' : 'bg-orange-100 text-orange-700'
                }`}>
                  {chat.mode === 'Automatic' ? <Bot className="w-3 h-3 mr-1" /> : <User className="w-3 h-3 mr-1" />}
                  {chat.mode === 'Automatic' ? 'IA' : 'Humano'}
                </span>
              </div>
            </button>
          ))
        )}
      </div>
    </div>
  );
};