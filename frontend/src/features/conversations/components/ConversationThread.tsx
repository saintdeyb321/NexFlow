import { useState } from 'react';
import { Bot, User, Send, Clock } from 'lucide-react';
import type { Conversation, Message } from '../types/conversation.types';

interface ConversationThreadProps {
  chat: Conversation;
  messages: Message[];
  isChangingMode: boolean;
  isSending: boolean;
  onTakeOver: () => void;
  onRelease: () => void;
  onSendMessage: (content: string) => void;
}

export const ConversationThread = ({
  chat, messages, isChangingMode, isSending, onTakeOver, onRelease, onSendMessage
}: ConversationThreadProps) => {
  const [newMessage, setNewMessage] = useState('');

  const handleSend = () => {
    if (!newMessage.trim()) return;
    onSendMessage(newMessage.trim());
    setNewMessage('');
  };

  return (
    <div className="w-2/3 flex flex-col bg-gray-50/50">
      {/* Header del Chat */}
      <div className="p-4 border-b border-gray-200 bg-white flex justify-between items-center shadow-sm z-10">
        <div>
          <h3 className="font-bold text-gray-900">{chat.consumerPhone}</h3>
          <p className="text-xs text-gray-500 flex items-center mt-0.5">
            <Clock className="w-3 h-3 mr-1" /> Inicio: {new Date(chat.startedAt).toLocaleDateString()}
          </p>
        </div>
        <div>
          {chat.mode === 'Automatic' ? (
            <button onClick={onTakeOver} disabled={isChangingMode} className="flex items-center px-4 py-2 bg-orange-500 text-white text-sm font-medium rounded-lg hover:bg-orange-600 transition-colors">
              <User className="w-4 h-4 mr-2" /> {isChangingMode ? 'Procesando...' : 'Asumir Control'}
            </button>
          ) : (
            <button onClick={onRelease} disabled={isChangingMode} className="flex items-center px-4 py-2 bg-green-500 text-white text-sm font-medium rounded-lg hover:bg-green-600 transition-colors">
              <Bot className="w-4 h-4 mr-2" /> {isChangingMode ? 'Procesando...' : 'Reactivar IA'}
            </button>
          )}
        </div>
      </div>

      {/* Historial de Mensajes */}
      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {messages.length === 0 ? (
          <div className="text-center text-gray-500 text-sm mt-10">Sin mensajes en el historial.</div>
        ) : (
          messages.map(msg => (
            <div key={msg.id} className={`flex ${msg.direction === 'inbound' ? 'justify-start' : 'justify-end'}`}>
              <div className={`max-w-[70%] rounded-2xl px-4 py-2 ${
                msg.direction === 'inbound' 
                  ? 'bg-white border border-gray-200 text-gray-800 rounded-tl-sm' 
                  : msg.sender === 'AI' 
                    ? 'bg-blue-100 text-blue-900 border border-blue-200 rounded-tr-sm'
                    : 'bg-green-500 text-white rounded-tr-sm'
              }`}>
                <p className="text-sm whitespace-pre-wrap">{msg.content}</p>
                <div className={`text-[10px] mt-1 flex items-center justify-end ${
                  msg.direction === 'inbound' ? 'text-gray-400' : (msg.sender === 'AI' ? 'text-blue-500' : 'text-green-200')
                }`}>
                  {msg.sender === 'AI' && <Bot className="w-3 h-3 mr-1" />}
                  {new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                </div>
              </div>
            </div>
          ))
        )}
      </div>

      {/* Input Manual */}
      <div className="p-4 bg-white border-t border-gray-200">
        <div className="flex items-center">
          <input
            type="text"
            value={newMessage}
            onChange={(e) => setNewMessage(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder={chat.mode === 'Automatic' ? 'Toma el control para enviar un mensaje...' : 'Escribe un mensaje al cliente...'}
            disabled={chat.mode === 'Automatic' || isSending}
            className="flex-1 border border-gray-300 rounded-lg px-4 py-2.5 outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed"
          />
          <button 
            onClick={handleSend}
            disabled={chat.mode === 'Automatic' || isSending || !newMessage.trim()}
            className="ml-3 p-2.5 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
          >
            <Send className="w-5 h-5" />
          </button>
        </div>
      </div>
    </div>
  );
};