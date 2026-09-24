import { useState, useRef, useEffect } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Bell, Calendar, MessageCircle, ClipboardList, AlertTriangle } from 'lucide-react'; // 🔥 Quitamos 'Check'
import { getNotifications, markNotificationAsRead } from '../../features/notifications/services/notification.service';
import type { NotificationDto } from '../../features/notifications/services/notification.service'; // 🔥 Importamos el tipo correctamente
import { useAuthStore } from '../../core/store/useAuthStore';
import { useNavigate } from 'react-router-dom';

export const NotificationBell = () => {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data: notifications = [] } = useQuery({
    queryKey: ['notifications', workspaceId],
    queryFn: getNotifications,
    enabled: !!workspaceId,
    refetchInterval: 15000, // Consulta cada 15 segundos
  });

  const readMutation = useMutation({
    mutationFn: markNotificationAsRead,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['notifications'] }),
  });

  const unreadCount = notifications.filter(n => !n.isRead).length;

  useEffect(() => {
    const handleClickOutside = (event: MouseEvent) => {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) setIsOpen(false);
    };
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const getIcon = (moduleCode: string) => {
    switch (moduleCode) {
      case 'RESERVATIONS': return <Calendar className="w-5 h-5 text-blue-500" />;
      case 'CONVERSATIONS': return <MessageCircle className="w-5 h-5 text-orange-500" />;
      case 'REQUESTS': return <ClipboardList className="w-5 h-5 text-purple-500" />;
      default: return <AlertTriangle className="w-5 h-5 text-yellow-500" />;
    }
  };

  const handleNotificationClick = (n: NotificationDto) => {
    if (!n.isRead) readMutation.mutate(n.id);
    setIsOpen(false);
    if (n.actionUrl) navigate(n.actionUrl);
  };

  return (
    <div className="relative" ref={dropdownRef}>
      <button 
        onClick={() => setIsOpen(!isOpen)}
        className="relative p-2 text-gray-500 hover:bg-gray-100 rounded-full transition-colors focus:outline-none"
      >
        <Bell className="w-6 h-6" />
        {unreadCount > 0 && (
          <span className="absolute top-1 right-1 flex h-4 w-4 items-center justify-center rounded-full bg-red-500 text-[10px] font-bold text-white border-2 border-white animate-in zoom-in">
            {unreadCount > 9 ? '9+' : unreadCount}
          </span>
        )}
      </button>

      {isOpen && (
        <div className="absolute right-0 mt-2 w-80 bg-white border border-gray-200 rounded-xl shadow-xl z-50 animate-in fade-in slide-in-from-top-2 origin-top-right">
          <div className="flex justify-between items-center p-4 border-b border-gray-100 bg-gray-50/50 rounded-t-xl">
            <h3 className="font-bold text-gray-800">Notificaciones</h3>
            {unreadCount > 0 && (
              <span className="text-xs text-blue-600 bg-blue-50 px-2 py-1 rounded-full font-medium">
                {unreadCount} nuevas
              </span>
            )}
          </div>
          
          <div className="max-h-96 overflow-y-auto">
            {notifications.length === 0 ? (
              <div className="p-8 text-center text-gray-500 text-sm">
                <Bell className="w-8 h-8 mx-auto text-gray-300 mb-2" />
                No tienes notificaciones
              </div>
            ) : (
              <div className="divide-y divide-gray-100">
                {notifications.map(n => (
                  <div 
                    key={n.id} 
                    onClick={() => handleNotificationClick(n)}
                    className={`flex items-start p-4 cursor-pointer hover:bg-gray-50 transition-colors ${!n.isRead ? 'bg-blue-50/30' : ''}`}
                  >
                    <div className="flex-shrink-0 mr-3 mt-1">
                      {getIcon(n.moduleCode)}
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className={`text-sm ${!n.isRead ? 'font-bold text-gray-900' : 'font-medium text-gray-700'}`}>
                        {n.title}
                      </p>
                      <p className="text-sm text-gray-500 line-clamp-2 mt-0.5">{n.message}</p>
                      <p className="text-xs text-gray-400 mt-1">
                        {new Date(n.createdAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                      </p>
                    </div>
                    {!n.isRead && (
                      <div className="flex-shrink-0 ml-2">
                        <span className="flex h-2 w-2 rounded-full bg-blue-600"></span>
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
};