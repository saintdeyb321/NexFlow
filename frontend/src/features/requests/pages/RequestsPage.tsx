import { requestTransitions } from '../types/request.types';
import { RequestDetailModal } from '../components/RequestDetailModal';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ClipboardList, Clock, PlayCircle, CheckCircle, XCircle, FileText, Plus, MessageSquare, UserCircle } from 'lucide-react';
import { getRequests, updateRequestStatus, assignRequest, getAssignees } from '../services/request.service';
import type { RequestStatus, RequestType } from '../types/request.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { CreateRequestModal } from '../components/CreateRequestModal';

export const RequestsPage = () => {
  const queryClient = useQueryClient();
  const { can } = usePermissions();
  const navigate = useNavigate(); 
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  // 🔥 SPRINT 07: Nombre real usando el DTO correcto, no "any".
  const me = useAuthStore((state) => state.me);
  
  const [filterStatus, setFilterStatus] = useState<RequestStatus | 'ALL'>('ALL');
  const [detailId, setDetailId] = useState<string | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false); 
  const [limit, setLimit] = useState(50); 
  
  // 🔥 SPRINT 07: Estado para notificaciones de error
  const [notification, setNotification] = useState<{ msg: string, type: 'success' | 'error' } | null>(null);

  const { data: requests = [], isLoading, isError } = useQuery({
    queryKey: ['requests', workspaceId, limit, filterStatus],
    queryFn: () => getRequests(limit, filterStatus === 'ALL' ? undefined : filterStatus),
    enabled: !!workspaceId && can('REQUESTS', 'READ'),
    refetchInterval: 30000,
  });

  const { data: assignees = [], error: assigneesError } = useQuery({
    queryKey: ['requestAssignees', workspaceId], queryFn: getAssignees,
    enabled: !!workspaceId && can('REQUESTS', 'ASSIGN'),
  });
  const assigneeName = (userId: string) => {
    const member = assignees.find(member => member.userId === userId);
    return member ? `${member.firstName} ${member.lastName}`.trim() || member.userId : 'Asignado';
  };

  const updateMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: RequestStatus }) => updateRequestStatus(id, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests', workspaceId] });
      setNotification({ msg: 'Estado actualizado correctamente.', type: 'success' });
    },
    onError: (error: unknown) => {
      setNotification({ msg: getApiErrorPresentation(error), type: 'error' });
    }
  });

  const assignMutation = useMutation({
    mutationFn: ({ id, userId }: { id: string; userId: string }) => assignRequest(id, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests', workspaceId] });
      setNotification({ msg: 'Solicitud asignada correctamente.', type: 'success' });
    },
    onError: (error: unknown) => {
      setNotification({ msg: getApiErrorPresentation(error), type: 'error' });
    }
  });

  const getStatusBadge = (status: RequestStatus) => {
    switch (status) {
      case 'Pending': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-yellow-100 text-yellow-800 rounded-full w-fit"><Clock className="w-3 h-3 mr-1" /> Pendiente</span>;
      case 'InReview': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-blue-100 text-blue-800 rounded-full w-fit"><PlayCircle className="w-3 h-3 mr-1" /> En Revisión</span>;
      case 'Approved': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-emerald-100 text-emerald-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Aprobada</span>;
      case 'Completed': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-green-100 text-green-800 rounded-full w-fit"><CheckCircle className="w-3 h-3 mr-1" /> Completada</span>;
      case 'Rejected': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-red-100 text-red-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Rechazada</span>;
      case 'Cancelled': return <span className="flex items-center px-2 py-1 text-xs font-medium bg-gray-100 text-gray-800 rounded-full w-fit"><XCircle className="w-3 h-3 mr-1" /> Cancelada</span>;
      default: return <span className="bg-gray-100 text-gray-800 text-xs px-2 py-1 rounded-full w-fit">{status}</span>;
    }
  };

  const getTypeBadge = (type: RequestType) => {
    switch (type) {
      case 'Tramite': return <span className="px-2 py-1 text-xs font-medium bg-purple-100 text-purple-800 rounded-md">Trámite</span>;
      case 'CommercialInquiry': return <span className="px-2 py-1 text-xs font-medium bg-indigo-100 text-indigo-800 rounded-md">Comercial</span>;
      case 'Support': return <span className="px-2 py-1 text-xs font-medium bg-orange-100 text-orange-800 rounded-md">Soporte</span>;
      case 'HumanHandoff': return <span className="px-2 py-1 text-xs font-medium bg-pink-100 text-pink-800 rounded-md">Asesor Humano</span>;
      default: return <span className="px-2 py-1 text-xs font-medium bg-gray-100 text-gray-800 rounded-md">General</span>;
    }
  };

  if (isError) return <div className="p-8 text-center text-red-500">Error al cargar las solicitudes.</div>;
  if (isLoading) return <div className="p-8 text-center text-gray-500 animate-pulse">Cargando solicitudes...</div>;

  return (
    <div className="max-w-7xl mx-auto animate-in fade-in">
      {notification && (
        <div className={`mb-4 p-4 rounded-lg flex justify-between items-center ${notification.type === 'error' ? 'bg-red-50 text-red-700 border border-red-200' : 'bg-green-50 text-green-700 border border-green-200'}`}>
          <span>{notification.msg}</span>
          <button onClick={() => setNotification(null)} className="text-sm font-bold opacity-70 hover:opacity-100">X</button>
        </div>
      )}

      <div className="mb-8 flex flex-col md:flex-row justify-between items-start md:items-center gap-4">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 flex items-center">
            <ClipboardList className="w-6 h-6 mr-3 text-blue-600" /> Solicitudes y Requerimientos
          </h1>
          <p className="mt-1 text-sm text-gray-500">
            Gestiona trámites, consultas comerciales y pedidos de soporte clasificados por la IA.
          </p>
        </div>
        
        <div className="flex items-center gap-3">
          <select 
            value={filterStatus} 
            onChange={(e) => setFilterStatus(e.target.value as RequestStatus | 'ALL')}
            className="border border-gray-200 rounded-lg px-4 py-2 text-sm bg-white focus:ring-2 focus:ring-blue-500 outline-none shadow-sm"
          >
            <option value="ALL">Todas las solicitudes</option>
            <option value="Pending">Pendientes</option>
            <option value="InReview">En Revisión</option>
            <option value="Completed">Completadas</option>
          </select>
          <button 
            disabled={!can('REQUESTS', 'CREATE')} onClick={() => setIsModalOpen(true)}
            className="flex items-center px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 transition-colors shadow-sm"
          >
            <Plus className="w-4 h-4 mr-2" /> Nueva Solicitud
          </button>
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden min-h-[400px]">
        {requests.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-20 text-gray-400">
            <FileText className="w-12 h-12 mb-4 text-gray-300" />
            <p className="text-lg font-medium text-gray-600">No hay solicitudes</p>
            <p className="text-sm">Las solicitudes creadas por tus clientes aparecerán aquí.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left">
              <thead>
                <tr className="bg-gray-50 border-b border-gray-200 text-xs font-semibold text-gray-500 uppercase">
                  <th className="px-6 py-4">Fecha</th>
                  <th className="px-6 py-4">Cliente / Origen</th>
                  <th className="px-6 py-4">Tipo</th>
                  <th className="px-6 py-4 w-1/3">Detalle</th>
                  <th className="px-6 py-4">Asignado a</th>
                  <th className="px-6 py-4">Estado</th>
                  <th className="px-6 py-4 text-right">Acción</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {requests.map((req) => (
                  <tr key={req.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                      {new Date(req.createdAt).toLocaleDateString()} <br/>
                      <span className="text-xs text-gray-400">{new Date(req.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      <span className="font-medium text-gray-900 block">{req.consumerPhone}</span>
                      {req.conversationId && req.conversationId !== 'MANUAL_ENTRY' && (
                        <button 
                          onClick={() => navigate(`/inbox?conversation=${req.conversationId}`)}
                          className="flex items-center text-xs text-blue-600 mt-1 hover:underline font-medium"
                        >
                          <MessageSquare className="w-3 h-3 mr-1" /> Abrir chat
                        </button>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getTypeBadge(req.type)}
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-600">
                      <button onClick={() => setDetailId(req.id)} className="font-semibold block text-gray-900 mb-1">{req.title}</button>
                      <p className="line-clamp-2" title={req.description}>{req.description}</p>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm">
                      {req.assignedTo ? (
                        <span className="flex items-center text-gray-700 font-medium">
                          <UserCircle className="w-4 h-4 mr-1.5 text-gray-400" /> {assigneeName(req.assignedTo)}
                        </span>
                      ) : (
                        <button 
                          onClick={() => { if (me && can('REQUESTS', 'ASSIGN')) assignMutation.mutate({ id: req.id, userId: me.user.id }); }}
                          disabled={assignMutation.isPending || !can('REQUESTS', 'ASSIGN') || !me?.user.id}
                          className="text-xs text-blue-600 hover:text-blue-800 hover:underline disabled:opacity-50"
                        >
                          Asignarme
                        </button>
                      )}
                      {can('REQUESTS', 'ASSIGN') && <select aria-label="Responsable" value={req.assignedTo ?? ''}
                        disabled={assignMutation.isPending || Boolean(assigneesError)}
                        onChange={event => { if (event.target.value) assignMutation.mutate({ id: req.id, userId: event.target.value }); }}>
                        <option value="">Seleccionar responsable</option>
                        {assignees.map(member => <option key={member.userId} value={member.userId}>{`${member.firstName} ${member.lastName}`.trim() || member.userId}</option>)}
                      </select>}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(req.status)}
                    </td>
                    <td className="px-6 py-4 text-right">
                      <select
                        disabled={updateMutation.isPending || !can('REQUESTS', 'UPDATE_STATUS') || requestTransitions[req.status].length === 0}
                        value={req.status}
                        onChange={(e) => updateMutation.mutate({ id: req.id, status: e.target.value as RequestStatus })}
                        className="text-sm border border-gray-200 rounded-lg px-2 py-1.5 bg-white hover:bg-gray-50 outline-none font-medium cursor-pointer"
                      >
                        <option value={req.status}>{req.status}</option>
                        {requestTransitions[req.status].map(status => <option key={status} value={status}>{status}</option>)}
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
      
      {requests.length >= limit && (
        <div className="flex justify-center mt-6">
          <button 
            onClick={() => setLimit(l => l + 50)} 
            className="px-4 py-2 bg-white border border-gray-200 rounded-lg text-sm font-medium text-gray-700 hover:bg-gray-50 shadow-sm transition-colors"
          >
            Cargar más antiguas
          </button>
        </div>
      )}

      {assigneesError && <p role="alert">{getApiErrorPresentation(assigneesError)}</p>}
      {detailId && <RequestDetailModal id={detailId} onClose={() => setDetailId(null)} />}
      <CreateRequestModal 
        isOpen={isModalOpen && can('REQUESTS', 'CREATE')}
        onClose={() => setIsModalOpen(false)} 
      />
    </div>
  );
};
