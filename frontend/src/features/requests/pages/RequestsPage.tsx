import { useState, useEffect } from 'react';
import { ClipboardList, Clock, CheckCircle, XCircle, Loader2, ThumbsUp, Ban } from 'lucide-react';
import { getRequests, updateRequestStatus } from '../services/request.service';
import type { RequestRecord, RequestStatus } from '../types/request.types';

export const RequestsPage = () => {
  const [requests, setRequests] = useState<RequestRecord[]>([]);
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    loadRequests();
  }, []);

  const loadRequests = async () => {
    try {
      const data = await getRequests();
      setRequests(data);
    } catch (error) {
      console.error("Error al cargar solicitudes", error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleStatusChange = async (id: string, newStatus: string) => {
    try {
      await updateRequestStatus(id, newStatus);
      setRequests(requests.map(r => r.id === id ? { ...r, status: newStatus as RequestStatus } : r));
    } catch (error: any) {
      alert(`Error actualizando el estado: ${error.message || 'Error desconocido'}`);
    }
  };

  // 🔥 CORRECCIÓN: Badges mapeados exactamente al Enum de C#
  const getStatusBadge = (status: RequestStatus | string) => {
    switch (status) {
      case 'Pending': return <span className="flex items-center px-2.5 py-1 bg-yellow-100 text-yellow-800 rounded-full text-xs font-medium"><Clock className="w-3 h-3 mr-1" /> Pendiente</span>;
      case 'InReview': return <span className="flex items-center px-2.5 py-1 bg-blue-100 text-blue-800 rounded-full text-xs font-medium"><Loader2 className="w-3 h-3 mr-1 animate-spin" /> En Revisión</span>;
      case 'Approved': return <span className="flex items-center px-2.5 py-1 bg-teal-100 text-teal-800 rounded-full text-xs font-medium"><ThumbsUp className="w-3 h-3 mr-1" /> Aprobado</span>;
      case 'Rejected': return <span className="flex items-center px-2.5 py-1 bg-orange-100 text-orange-800 rounded-full text-xs font-medium"><Ban className="w-3 h-3 mr-1" /> Rechazado</span>;
      case 'Completed': return <span className="flex items-center px-2.5 py-1 bg-green-100 text-green-800 rounded-full text-xs font-medium"><CheckCircle className="w-3 h-3 mr-1" /> Completado</span>;
      case 'Cancelled': return <span className="flex items-center px-2.5 py-1 bg-red-100 text-red-800 rounded-full text-xs font-medium"><XCircle className="w-3 h-3 mr-1" /> Cancelado</span>;
      default: return <span>{status}</span>;
    }
  };

  if (isLoading) return <div className="animate-pulse p-8 text-center text-gray-500">Cargando solicitudes...</div>;

  return (
    <div className="max-w-6xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">
          <ClipboardList className="w-6 h-6 mr-3 text-blue-600" /> Bandeja de Trámites
        </h1>
        <p className="mt-1 text-sm text-gray-500">Gestiona las solicitudes, afiliaciones o requerimientos creados por la IA.</p>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl shadow-sm overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Fecha</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Cliente (Teléfono)</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Detalle</th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">Estado</th>
              <th className="px-6 py-3 text-center text-xs font-medium text-gray-500 uppercase">Acción</th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {requests.length === 0 ? (
              <tr><td colSpan={5} className="px-6 py-8 text-center text-gray-500">No hay trámites registrados.</td></tr>
            ) : (
              requests.map((req) => (
                <tr key={req.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 text-sm text-gray-500">{new Date(req.createdAt).toLocaleDateString()}</td>
                  <td className="px-6 py-4 font-medium text-gray-900">{req.consumerPhone}</td>
                  <td className="px-6 py-4 text-sm text-gray-600 max-w-xs truncate" title={req.description}>{req.description}</td>
                  <td className="px-6 py-4">{getStatusBadge(req.status)}</td>
                  <td className="px-6 py-4 text-center">
                    <select 
                      value={req.status} 
                      onChange={(e) => handleStatusChange(req.id, e.target.value)}
                      className="text-sm border border-gray-300 rounded-lg px-2 py-1 outline-none focus:ring-2 focus:ring-blue-500"
                    >
                      <option value="Pending">Pendiente</option>
                      <option value="InReview">En Revisión</option>
                      <option value="Approved">Aprobado</option>
                      <option value="Rejected">Rechazado</option>
                      <option value="Completed">Completado</option>
                      <option value="Cancelled">Cancelar</option>
                    </select>
                  </td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
};