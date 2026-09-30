import { useState, useRef, useEffect } from 'react';
import { Bot, User, Send, Clock, AlertTriangle, AlertOctagon, Loader2, Image as ImageIcon } from 'lucide-react';
import type { Conversation, Message, ManualMessageResult } from '../types/conversation.types';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';

interface ConversationThreadProps {
  chat: Conversation;
  messages: Message[];
  isChangingMode: boolean;
  isSending: boolean;
  onTakeOver: () => void;
  onRelease: () => void;
  onSendMessage: (content: string) => Promise<ManualMessageResult>;
}

export const ConversationThread = ({
  chat, messages, isChangingMode, isSending, onTakeOver, onRelease, onSendMessage
}: ConversationThreadProps) => {
  const { can } = usePermissions();
  const [newMessage, setNewMessage] = useState('');
  const [sendError, setSendError] = useState<string | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null); 

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  // 🔥 SPRINT 05: Solo limpiamos el input si el envío fue exitoso. Si falla, retenemos el texto.
  const handleSend = async () => {
    if (!newMessage.trim()) return;
    setSendError(null);
    try {
      const result = await onSendMessage(newMessage.trim());
      setNewMessage(''); 
      if (result.message.status === 'UnknownDelivery') setSendError('Entrega sin confirmar. Revisa el estado antes de reenviar.');
      else if (result.accepted) setSendError('Envío aceptado; la entrega aún no está confirmada.');
    } catch (error: unknown) {
      setSendError(getApiErrorPresentation(error));
    }
  };

  return (
    <div className="w-full flex flex-col h-full bg-gray-50/50 relative">
      <div className="p-4 border-b border-gray-200 bg-white flex justify-between items-center shadow-sm z-10">
        <div>
          <h3 className="font-bold text-gray-900 text-lg">{chat.consumerPhone}</h3>
          <p className="text-xs text-gray-500 flex items-center mt-0.5">
            <Clock className="w-3 h-3 mr-1" /> Inicio: {new Date(chat.startedAt).toLocaleDateString()}
          </p>
        </div>
        <div>
          {chat.mode === 'Automatic' ? (
            <button onClick={onTakeOver} disabled={isChangingMode || !can('CONVERSATIONS', 'TAKEOVER')} className="flex items-center px-4 py-2 bg-orange-500 text-white text-sm font-medium rounded-lg hover:bg-orange-600 transition-colors shadow-sm disabled:opacity-50">
              {isChangingMode ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <User className="w-4 h-4 mr-2" />} 
              {isChangingMode ? 'Procesando...' : 'Asumir Control'}
            </button>
          ) : (
            <button onClick={onRelease} disabled={isChangingMode || !can('CONVERSATIONS', 'RELEASE')} className="flex items-center px-4 py-2 bg-green-500 text-white text-sm font-medium rounded-lg hover:bg-green-600 transition-colors shadow-sm disabled:opacity-50">
              {isChangingMode ? <Loader2 className="w-4 h-4 mr-2 animate-spin" /> : <Bot className="w-4 h-4 mr-2" />} 
              {isChangingMode ? 'Procesando...' : 'Reactivar IA'}
            </button>
          )}
        </div>
      </div>

      {chat.mode === 'Automatic' ? (
        <div className="bg-blue-50 border-b border-blue-200 px-4 py-2.5 flex items-center justify-center text-blue-700 text-sm font-medium">
          <Bot className="w-4 h-4 mr-2 animate-pulse" />
          IA en Piloto Automático. El asistente virtual está gestionando al cliente.
        </div>
      ) : (
        <div className={`border-b px-4 py-2.5 flex items-center justify-center text-sm font-medium ${
          chat.handoffReason === 'AiEscalation' || chat.handoffReason === 'SystemError'
            ? 'bg-red-50 border-red-200 text-red-700' 
            : 'bg-orange-50 border-orange-200 text-orange-700'
        }`}>
          {chat.handoffReason === 'SystemError' ? (
             <>
               <AlertOctagon className="w-4 h-4 mr-2" />
               Error del Sistema: La IA falló o se desconectó. Asume el control para continuar.
             </>
          ) : chat.handoffReason === 'AiEscalation' ? (
            <>
              <AlertOctagon className="w-4 h-4 mr-2" />
              Alerta de la IA: El bot necesita tu asistencia para resolver esta solicitud.
            </>
          ) : (
            <>
              <AlertTriangle className="w-4 h-4 mr-2" />
              Modo Manual Activo. Estás chateando directamente; la IA está en pausa.
            </>
          )}
        </div>
      )}

      <div className="flex-1 overflow-y-auto p-6 space-y-4">
        {messages.length === 0 ? (
          <div className="text-center text-gray-500 text-sm mt-10">Sin mensajes en el historial.</div>
        ) : (
          messages.map(msg => (
            <div key={msg.id} className={`flex ${msg.direction === 'inbound' ? 'justify-start' : 'justify-end'}`}>
              <div className={`max-w-[70%] rounded-2xl px-4 py-2 ${
                msg.direction === 'inbound' 
                  ? 'bg-white border border-gray-200 text-gray-800 rounded-tl-sm shadow-sm' 
                  : msg.sender === 'AI' 
                    ? 'bg-blue-100 text-blue-900 border border-blue-200 rounded-tr-sm shadow-sm'
                    : 'bg-green-500 text-white rounded-tr-sm shadow-sm'
              }`}>
                <p className="text-sm whitespace-pre-wrap break-words">
                  {msg.content ? msg.content : (
                    <span className="italic flex items-center opacity-80">
                      <ImageIcon className="w-4 h-4 mr-1" /> [Contenido Multimedia]
                    </span>
                  )}
                </p>
                <div className={`text-[10px] mt-1 flex items-center justify-end ${
                  msg.direction === 'inbound' ? 'text-gray-400' : (msg.sender === 'AI' ? 'text-blue-500' : 'text-green-100')
                }`}>
                  {msg.sender === 'AI' && <Bot className="w-3 h-3 mr-1" />}
                  {new Date(msg.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                  {/* 🔥 SPRINT 05: Mostramos si falló la entrega */}
                  {msg.status === 'Failed' && <span className="ml-2 text-red-500 font-bold">Error de envío</span>}
                  {msg.direction === 'outbound' && msg.status !== 'Failed' && <span className="ml-2">{{ Pending: 'Pendiente', Attempting: 'Enviando', Sent: 'Enviado', UnknownDelivery: 'Entrega sin confirmar' }[msg.status]}</span>}
                </div>
              </div>
            </div>
          ))
        )}
        <div ref={messagesEndRef} />
      </div>

      <div className="p-4 bg-white border-t border-gray-200">
        {sendError && <div className="text-red-500 text-xs font-medium mb-2">{sendError}</div>}
        <div className="flex items-center">
          <input
            type="text"
            value={newMessage}
            onChange={(e) => setNewMessage(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleSend()}
            placeholder={chat.mode === 'Automatic' ? 'Bloqueado. Asume el control manual para enviar un mensaje...' : 'Escribe un mensaje al cliente...'}
            disabled={chat.mode === 'Automatic' || isSending || isChangingMode || !can('CONVERSATIONS', 'SEND_MESSAGE')}
            className="flex-1 border border-gray-300 rounded-lg px-4 py-2.5 outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100 disabled:text-gray-400 disabled:cursor-not-allowed"
          />
          <button 
            onClick={handleSend}
            disabled={chat.mode === 'Automatic' || isSending || isChangingMode || !newMessage.trim() || !can('CONVERSATIONS', 'SEND_MESSAGE')}
            className="ml-3 p-2.5 bg-blue-600 text-white rounded-lg hover:bg-blue-700 disabled:bg-gray-300 disabled:cursor-not-allowed transition-colors"
          >
            {isSending ? <Loader2 className="w-5 h-5 animate-spin" /> : <Send className="w-5 h-5" />}
          </button>
        </div>
      </div>
    </div>
  );
};
