import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { ClipboardList, Clock, PlayCircle, CheckCircle, XCircle, FileText, Plus, MessageSquare } from 'lucide-react';
import { getRequests, updateRequestStatus } from '../services/request.service';
import type { RequestStatus, RequestType } from '../types/request.types';
import { useAuthStore } from '../../../core/store/useAuthStore';

export const RequestsPage = () => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const [filterStatus, setFilterStatus] = useState<RequestStatus | 'ALL'>('ALL');

  const { data: requests = [], isLoading, isError } = useQuery({
    queryKey: ['requests', workspaceId],
    queryFn: getRequests,
    enabled: !!workspaceId,
    refetchInterval: 30000,
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, status }: { id: string; status: RequestStatus }) => updateRequestStatus(id, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['requests', workspaceId] });
    },
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

  const handleStatusChange = (id: string, newStatus: RequestStatus) => {
    updateMutation.mutate({ id, status: newStatus });
  };

  const filteredRequests = filterStatus === 'ALL' 
    ? requests 
    : requests.filter(r => r.status === filterStatus);

  if (isError) return <div className="p-8 text-center text-red-500">Error al cargar las solicitudes.</div>;
  if (isLoading) return <div className="p-8 text-center text-gray-500 animate-pulse">Cargando solicitudes...</div>;

  return (
    <div className="max-w-7xl mx-auto animate-in fade-in">
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
            className="border border-gray-200 rounded-lg px-4 py-2 text-sm bg-white focus:ring-2 focus:ring-blue-500 outline-none"
          >
            <option value="ALL">Todas las solicitudes</option>
            <option value="Pending">Pendientes</option>
            <option value="InReview">En Revisión</option>
            <option value="Completed">Completadas</option>
          </select>
          <button className="flex items-center px-4 py-2 bg-blue-600 text-white text-sm font-medium rounded-lg hover:bg-blue-700 transition-colors">
            <Plus className="w-4 h-4 mr-2" /> Nueva Solicitud
          </button>
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        {filteredRequests.length === 0 ? (
          <div className="flex flex-col items-center justify-center py-16 text-gray-400">
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
                  <th className="px-6 py-4">Cliente</th>
                  <th className="px-6 py-4">Tipo</th>
                  <th className="px-6 py-4 w-1/3">Detalle</th>
                  <th className="px-6 py-4">Estado</th>
                  <th className="px-6 py-4 text-right">Acción</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {filteredRequests.map((req) => (
                  <tr key={req.id} className="hover:bg-gray-50 transition-colors">
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-600">
                      {new Date(req.createdAt).toLocaleDateString()} <br/>
                      <span className="text-xs text-gray-400">{new Date(req.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap font-medium text-gray-900">
                      {req.consumerPhone}
                      {req.conversationId && req.conversationId !== 'MANUAL_ENTRY' && (
                        <div className="flex items-center text-xs text-blue-600 mt-1 cursor-pointer hover:underline">
                          <MessageSquare className="w-3 h-3 mr-1" /> Ver chat
                        </div>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getTypeBadge(req.type)}
                    </td>
                    <td className="px-6 py-4 text-sm text-gray-600">
                      <span className="font-semibold block text-gray-900 mb-1">{req.title}</span>
                      <p className="line-clamp-2" title={req.description}>{req.description}</p>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {getStatusBadge(req.status)}
                    </td>
                    <td className="px-6 py-4 text-right">
                      <select
                        disabled={updateMutation.isPending}
                        value={req.status}
                        onChange={(e) => handleStatusChange(req.id, e.target.value as RequestStatus)}
                        className="text-sm border border-gray-200 rounded-lg px-2 py-1 bg-white hover:bg-gray-50 outline-none"
                      >
                        <option value="Pending">Marcar Pendiente</option>
                        <option value="InReview">En Revisión</option>
                        <option value="Approved">Aprobar</option>
                        <option value="Completed">Completar</option>
                        <option value="Rejected">Rechazar</option>
                        <option value="Cancelled">Cancelar</option>
                      </select>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};