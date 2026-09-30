import { useQuery } from '@tanstack/react-query';
import { Modal } from '../../../components/ui/Modal';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { getRequest } from '../services/request.service';

export const RequestDetailModal = ({ id, onClose }: { id: string; onClose: () => void }) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  const { data, isLoading, error } = useQuery({
    queryKey: ['requests', workspaceId, 'detail', id], queryFn: () => getRequest(id),
    enabled: Boolean(workspaceId) && can('REQUESTS', 'READ'),
  });
  return <Modal isOpen onClose={onClose} title="Detalle de solicitud" maxWidth="max-w-2xl">
    {isLoading && <p>Cargando solicitud...</p>}
    {error && <p role="alert">{getApiErrorPresentation(error)}</p>}
    {data && <div className="space-y-3">
      <h3 className="font-semibold">{data.title}</h3>
      <p>{data.consumerPhone} · {data.type} · {data.status}</p>
      <p className="whitespace-pre-wrap">{data.description}</p>
      <p>{new Date(data.createdAt).toLocaleString()}</p>
      {Object.entries(data.metadata ?? {}).map(([key, value]) => <p key={key}>{key}: {typeof value === 'string' ? value : JSON.stringify(value)}</p>)}
    </div>}
  </Modal>;
};
