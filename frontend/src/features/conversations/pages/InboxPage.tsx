import { useEffect, useState } from 'react';
import { Bot, User, Send, Clock, Phone } from 'lucide-react';
import { getConversations, getMessages, takeOverConversation, releaseConversation, sendManualMessage } from '../services/conversation.service';
import type { Conversation, Message } from '../types/conversation.types';

export const InboxPage = () => {
  const [conversations, setConversations] = useState<Conversation[]>([]);
  const [selectedChat, setSelectedChat] = useState<Conversation | null>(null);
  const [messages, setMessages] = useState<Message[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [isChangingMode, setIsChangingMode] = useState(false);
  
  // NUEVO: Estados para el input de texto
  const [newMessage, setNewMessage] = useState('');
  const [isSending, setIsSending] = useState(false);

  useEffect(() => {
    loadConversations();
  }, []);

  useEffect(() => {
    if (selectedChat) {
      loadMessages(selectedChat.id);
    }
  }, [selectedChat?.id]);

  const loadConversations = async () => {
    try {
      const data = await getConversations();
      setConversations(data);
      if (data.length > 0 && !selectedChat) setSelectedChat(data[0]);
    } catch (error) {
      console.error('Error cargando conversaciones', error);
    } finally {
      setIsLoading(false);
    }
  };

  const loadMessages = async (conversationId: string) => {
    try {
      const data = await getMessages(conversationId);
      setMessages(data);
    } catch (error) {
      console.error('Error cargando mensajes', error);
    }
  };

  const handleTakeOver = async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await takeOverConversation(selectedChat.id);
      setSelectedChat({ ...selectedChat, mode: 'Human' });
      setConversations(conversations.map(c => c.id === selectedChat.id ? { ...c, mode: 'Human' } : c));
    } finally {
      setIsChangingMode(false);
    }
  };

  const handleRelease = async () => {
    if (!selectedChat) return;
    setIsChangingMode(true);
    try {
      await releaseConversation(selectedChat.id);
      setSelectedChat({ ...selectedChat, mode: 'Automatic' });
      setConversations(conversations.map(c => c.id === selectedChat.id ? { ...c, mode: 'Automatic' } : c));
    } finally {
      setIsChangingMode(false);
    }
  };

  // NUEVO: Función para enviar el mensaje
  const handleSendMessage = async () => {
    if (!selectedChat || !newMessage.trim()) return;
    
    setIsSending(true);
    try {
      const sentMessage = await sendManualMessage(selectedChat.id, selectedChat.consumerPhone, newMessage.trim());
      // Agregamos el mensaje nuevo al final de la lista visualmente
      setMessages([...messages, sentMessage]); 
      setNewMessage(''); // Limpiar el input
    } catch (error) {
      console.error('Error enviando mensaje', error);
    } finally {
      setIsSending(false);
    }
  };

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando bandeja...</div>;

  return (
    <div className="flex h-[calc(100vh-8rem)] bg-white border border-gray-200 rounded-xl overflow-hidden shadow-sm">
      {/* PANEL IZQUIERDO */}
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
                onClick={() => setSelectedChat(chat)}
                className={`w-full text-left p-4 border-b border-gray-100 hover:bg-gray-100 transition-colors ${selectedChat?.id === chat.id ? 'bg-blue-50 border-blue-100' : ''}`}
              >
                <div className="flex justify-between items-start mb-1">
                  <span className="font-medium text-gray-900 flex items-center">
                    <Phone className="w-4 h-4 mr-2 text-gray-400" />
                    {chat.consumerPhone}
                  </span>
                  <span className="text-xs text-gray-500">{new Date(chat.lastMessageAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</span>
                </div>
                <div className="flex items-center mt-2">
                  <span className={`text-xs px-2 py-0.5 rounded-full font-medium flex items-center ${chat.mode === 'Automatic' ? 'bg-green-100 text-green-700' : 'bg-orange-100 text-orange-700'}`}>
                    {chat.mode === 'Automatic' ? <Bot className="w-3 h-3 mr-1" /> : <User className="w-3 h-3 mr-1" />}
                    {chat.mode === 'Automatic' ? 'IA' : 'Humano'}
                  </span>
                </div>
              </button>
            ))
          )}
        </div>
      </div>

      {/* PANEL DERECHO */}
      <div className="w-2/3 flex flex-col bg-gray-50/50">
        {selectedChat ? (
          <>
            <div className="p-4 border-b border-gray-200 bg-white flex justify-between items-center shadow-sm z-10">
              <div>
                <h3 className="font-bold text-gray-900">{selectedChat.consumerPhone}</h3>
                <p className="text-xs text-gray-500 flex items-center mt-0.5">
                  <Clock className="w-3 h-3 mr-1" /> Inicio: {new Date(selectedChat.startedAt).toLocaleDateString()}
                </p>
              </div>
              <div>
                {selectedChat.mode === 'Automatic' ? (
                  <button onClick={handleTakeOver} disabled={isChangingMode} className="flex items-center px-4 py-2 bg-orange-500 text-white text-sm font-medium rounded-lg hover:bg-orange-600 transition-colors">
                    <User className="w-4 h-4 mr-2" /> {isChangingMode ? 'Procesando...' : 'Asumir Control'}
                  </button>
                ) : (
                  <button onClick={handleRelease} disabled={isChangingMode} className="flex items-center px-4 py-2 bg-green-500 text-white text-sm font-medium rounded-lg hover:bg-green-600 transition-colors">
                    <Bot className="w-4 h-4 mr-2" /> {isChangingMode ? 'Procesando...' : 'Reactivar IA'}
                  </button>
                )}
              </div>
            </div>

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
                      <div className={`text-[10px] mt-1 flex items-center justify-end ${msg.direction === 'inbound' ? 'text-gray-400' : (msg.sender === 'AI' ? 'text-blue-500' : 'text-green-200')}`}>
                        {msg.sender === 'AI' && <Bot className="w-3 h-3 mr-1" />}
                        {new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </div>
                    </div>
                  </div>
                ))
              )}
            </div>

            {/* NUEVO: Input de Mensaje Manual Activo */}
            <div className="p-4 bg-white border-t border-gray-200">
              <div className="flex items-center">
                <input
                  type="text"
                  value={newMessage}
                  onChange={(e) => setNewMessage(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && handleSendMessage()}
                  placeholder={selectedChat.mode === 'Automatic' ? 'Toma el control para enviar un mensaje...' : 'Escribe un mensaje al cliente...'}
                  disabled={selectedChat.mode === 'Automatic' || isSending}
                  className="flex-1 border border-gray-300 rounded-lg px-4 py-2.5 outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100 disabled:cursor-not-allowed"
                />
                <button 
                  onClick={handleSendMessage}
                  disabled={selectedChat.mode === 'Automatic' || isSending || !newMessage.trim()}
                  className="ml-3 p-2.5 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-400 disabled:cursor-not-allowed transition-colors"
                >
                  <Send className="w-5 h-5" />
                </button>
              </div>
            </div>
          </>
        ) : (
          <div className="flex-1 flex items-center justify-center text-gray-500">
            Selecciona una conversación para ver el historial
          </div>
        )}
      </div>
    </div>
  );
};