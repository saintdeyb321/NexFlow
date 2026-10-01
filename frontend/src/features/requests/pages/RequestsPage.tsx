import { getStatusTone, getStatusLabel } from '../../../components/ui/statusPresentation';
import { PageHeader } from '../../../components/ui/Layout';
import { Button } from '../../../components/ui/Button';
import { LoadingState, EmptyState, ErrorState, Badge, StatusBadge } from '../../../components/ui/Feedback';
import { useToast } from '../../../components/ui/useToast';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { requestTransitions } from '../types/request.types';
import { RequestDetailModal } from '../components/RequestDetailModal';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { ClipboardList, FileText, Plus, MessageSquare, UserCircle } from 'lucide-react';
import { getRequests, updateRequestStatus, assignRequest, getAssignees } from '../services/request.service';
import type { RequestStatus, RequestType } from '../types/request.types';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { CreateRequestModal } from '../components/CreateRequestModal';

export const RequestsPage = () => {
  const isPageVisible = usePageVisible();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const navigate = useNavigate();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const me = useAuthStore((state) => state.me);

  const [filterStatus, setFilterStatus] = useState<RequestStatus | 'ALL'>('ALL');
  const [detailId, setDetailId] = useState<string | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [limit, setLimit] = useState(50);

  const { data: requests = [], isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.requests.list(workspaceId, limit, filterStatus),
    queryFn: ({ signal }) => getRequests(limit, filterStatus === 'ALL' ? undefined : filterStatus, signal),
    enabled: !!workspaceId && can('REQUESTS', 'READ'),
    refetchInterval: isPageVisible ? 30000 : false,
  });

  const { data: assignees = [], error: assigneesError } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.requests.assignees(workspaceId), queryFn: ({ signal }) => getAssignees(signal),
    enabled: !!workspaceId && can('REQUESTS', 'ASSIGN'),
  });
  const assigneeName = (userId: string) => {
    const member = assignees.find(member => member.userId === userId);
    return member ? `${member.firstName} ${member.lastName}`.trim() || member.userId : 'Asignado';
  };

  const updateMutation = useSessionMutation({
    mutationFn: ({ id, status }: { id: string; status: RequestStatus }) => updateRequestStatus(id, status),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.requests.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.requests.detail(workspaceId, variables.id) });
      toast.success('Estado actualizado correctamente.');
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  const assignMutation = useSessionMutation({
    mutationFn: ({ id, userId }: { id: string; userId: string }) => assignRequest(id, userId),
    onSuccess: (_, variables) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.requests.lists(workspaceId) });
      queryClient.invalidateQueries({ queryKey: queryKeys.requests.detail(workspaceId, variables.id) });
      toast.success('Solicitud asignada correctamente.');
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  const getStatusBadge = (status: RequestStatus) => <StatusBadge tone={getStatusTone(status)} label={{ Pending: 'Pendiente', InReview: 'En revisión', Approved: 'Aprobada', Completed: 'Completada', Rejected: 'Rechazada', Cancelled: 'Cancelada' }[status]} />;
  const getTypeBadge = (type: RequestType) => {
    switch (type) {
      case 'Tramite': return <Badge className="px-2 py-1 text-xs font-medium bg-purple-100 text-purple-800 rounded-md">Trámite</Badge>;
      case 'CommercialInquiry': return <Badge className="px-2 py-1 text-xs font-medium bg-indigo-100 text-indigo-800 rounded-md">Comercial</Badge>;
      case 'Support': return <Badge className="px-2 py-1 text-xs font-medium bg-orange-100 text-orange-800 rounded-md">Soporte</Badge>;
      case 'HumanHandoff': return <Badge className="px-2 py-1 text-xs font-medium bg-pink-100 text-pink-800 rounded-md">Asesor Humano</Badge>;
      default: return <Badge className="px-2 py-1 text-xs font-medium bg-gray-100 text-foreground rounded-md">General</Badge>;
    }
  };

  if (isError) return <ErrorState title="Error al cargar las solicitudes." onRetry={() => void refetch()} />;
  if (isLoading) return <LoadingState title="Cargando solicitudes..." />;

  return (
    <div className="nf-page">

      <PageHeader title="Solicitudes" description="Gestiona los trámites, consultas y solicitudes de tus clientes." icon={<ClipboardList aria-hidden="true" className="w-5 h-5" />} actions={<><div className="flex flex-wrap items-center gap-3">
          <select aria-label="Filtrar solicitudes por estado"
            value={filterStatus}
            onChange={(e) => setFilterStatus(e.target.value as RequestStatus | 'ALL')}
            className="nf-control max-w-full"
          >
            <option value="ALL">Todas las solicitudes</option>
            <option value="Pending">Pendientes</option>
            <option value="InReview">En Revisión</option>
            <option value="Completed">Completadas</option>
          </select>
          <Button variant="primary"
            disabled={!can('REQUESTS', 'CREATE')} onClick={() => setIsModalOpen(true)}
            className="flex items-center text-sm font-medium transition-colors"
          >
            <Plus aria-hidden="true" className="w-4 h-4 mr-2" /> Nueva Solicitud
          </Button>
        </div></>} />

      <div className="bg-surface border border-line rounded-xl shadow-sm overflow-hidden min-h-[400px]">
        {requests.length === 0 ? (
          <EmptyState className="py-20" icon={<FileText aria-hidden="true" className="w-12 h-12 text-gray-300" />} title="No hay solicitudes" description="Las solicitudes creadas por tus clientes aparecerán aquí." />
        ) : (
          <div className="overflow-x-auto">
            <table className="nf-table nf-responsive-table">
              <thead>
                <tr className="bg-surface-soft border-b border-line text-xs font-semibold text-muted uppercase">
                  <th className="px-6 py-4">Fecha</th>
                  <th className="px-6 py-4">Cliente / Origen</th>
                  <th className="px-6 py-4">Tipo</th>
                  <th className="px-6 py-4">Detalle</th>
                  <th className="px-6 py-4">Asignado a</th>
                  <th className="px-6 py-4">Estado</th>
                  <th className="px-6 py-4 text-right">Acción</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-gray-100">
                {requests.map((req) => (
                  <tr key={req.id} className="hover:bg-surface-soft transition-colors">
                    <td data-label="Fecha" className="px-6 py-4 text-sm text-muted">
                      {new Date(req.createdAt).toLocaleDateString()} <br/>
                      <span className="text-xs text-gray-400">{new Date(req.createdAt).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}</span>
                    </td>
                    <td data-label="Cliente / Origen" className="px-6 py-4"><div className="min-w-0">
                      <span className="font-medium text-foreground block">{req.consumerPhone}</span>
                      {req.conversationId && req.conversationId !== 'MANUAL_ENTRY' && (
                        <Button variant="ghost"
                          onClick={() => navigate(`/inbox?conversation=${req.conversationId}`)}
                          className="flex items-center text-xs mt-1 hover:underline font-medium"
                        >
                          <MessageSquare aria-hidden="true" className="w-3 h-3 mr-1" /> Abrir chat
                        </Button>
                      )}</div>
                    </td>
                    <td data-label="Tipo" className="px-6 py-4">
                      {getTypeBadge(req.type)}
                    </td>
                    <td data-label="Detalle" className="px-6 py-4 text-sm text-muted"><div className="min-w-0">
                      <Button variant="ghost" onClick={() => setDetailId(req.id)} className="font-semibold block mb-1">{req.title}</Button>
                      <p className="line-clamp-2" title={req.description}>{req.description}</p></div>
                    </td>
                    <td data-label="Asignación" className="px-6 py-4 text-sm"><div className="min-w-0 max-w-full">
                      {req.assignedTo ? (
                        <span className="flex items-center text-gray-700 font-medium">
                          <UserCircle aria-hidden="true" className="w-4 h-4 mr-1.5 text-gray-400" /> {assigneeName(req.assignedTo)}
                        </span>
                      ) : (
                        <Button variant="ghost" isLoading={assignMutation.isPending}
                          onClick={() => { if (me && can('REQUESTS', 'ASSIGN')) assignMutation.mutate({ id: req.id, userId: me.user.id }); }}
                          disabled={assignMutation.isPending || !can('REQUESTS', 'ASSIGN') || !me?.user.id}
                          className="text-xs hover:underline disabled:opacity-50"
                        >
                          Asignarme
                        </Button>
                      )}
                      {can('REQUESTS', 'ASSIGN') && <select className="nf-control mt-2" aria-label="Responsable" value={req.assignedTo ?? ''}
                        disabled={assignMutation.isPending || Boolean(assigneesError)}
                        onChange={event => { if (event.target.value) assignMutation.mutate({ id: req.id, userId: event.target.value }); }}>
                        <option value="">Seleccionar responsable</option>
                        {assignees.map(member => <option key={member.userId} value={member.userId}>{`${member.firstName} ${member.lastName}`.trim() || member.userId}</option>)}
                      </select>}</div>
                    </td>
                    <td data-label="Estado" className="px-6 py-4">
                      {getStatusBadge(req.status)}
                    </td>
                    <td data-label="Acciones" className="px-6 py-4 text-right">
                      <select aria-label="Cambiar estado de solicitud"
                        disabled={updateMutation.isPending || !can('REQUESTS', 'UPDATE_STATUS') || requestTransitions[req.status].length === 0}
                        value={req.status}
                        onChange={(e) => updateMutation.mutate({ id: req.id, status: e.target.value as RequestStatus })}
                        className="nf-control max-w-full"
                      >
                        <option value={req.status}>{getStatusLabel(req.status)}</option>
                        {requestTransitions[req.status].map(status => <option key={status} value={status}>{getStatusLabel(status)}</option>)}
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
          <Button variant="ghost"
            onClick={() => setLimit(l => l + 50)}
            className="border text-sm font-medium transition-colors"
          >
            Cargar más antiguas
          </Button>
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
