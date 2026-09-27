import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { FileText, RefreshCw, Download, Loader2 } from 'lucide-react';
import { axiosClient } from '../../../core/api/axiosClient';
import { useAuthStore } from '../../../core/store/useAuthStore';

interface ArtifactGeneratorProps {
  scope: 'PRODUCT' | 'SERVICE';
  title: string;
}

export const ArtifactGenerator = ({ scope, title }: ArtifactGeneratorProps) => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const { data: artifact, isLoading } = useQuery({
    queryKey: ['artifact', workspaceId, scope],
    queryFn: async () => {
      // Opción A Pragmática: Reutilizamos el endpoint existente pero forzamos el scope estricto
      const response = await axiosClient.get(`/catalog/artifact?scope=${scope}`);
      return response.data;
    },
    enabled: !!workspaceId,
    refetchInterval: (query) => (query.state.data?.status === 'Generating' ? 5000 : false),
  });

  const generateMutation = useMutation({
    mutationFn: async () => {
      const response = await axiosClient.post('/catalog/artifact/generate', { scope });
      return response.data;
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['artifact', workspaceId, scope] });
    },
    onError: () => {
      alert(`Error al solicitar la generación del documento de ${scope.toLowerCase()}s.`);
    }
  });

  if (isLoading) return <div className="animate-pulse h-16 bg-gray-100 rounded-xl mb-6"></div>;

  const isStale = artifact?.status === 'Stale';
  const isGenerating = artifact?.status === 'Generating';
  const isCurrent = artifact?.status === 'Current';
  const hasError = artifact?.status === 'Error';

  return (
    <div className="mb-8 p-5 bg-white border border-gray-200 rounded-xl shadow-sm flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
      <div className="flex items-start gap-3">
        <div className={`p-3 rounded-lg ${isCurrent ? 'bg-green-100 text-green-700' : isStale ? 'bg-yellow-100 text-yellow-700' : isGenerating ? 'bg-blue-100 text-blue-700' : 'bg-red-100 text-red-700'}`}>
          <FileText className="w-6 h-6" />
        </div>
        <div>
          <h3 className="font-bold text-gray-900">{title}</h3>
          <p className="text-sm text-gray-500 mt-0.5">
            {isCurrent && 'El documento PDF está actualizado y listo para enviarse a los clientes.'}
            {isStale && 'Se detectaron cambios recientes. Necesitas actualizar el documento.'}
            {isGenerating && 'Generando documento mediante IA. Esto puede tomar unos minutos...'}
            {hasError && 'Hubo un error en la última generación. Intenta nuevamente.'}
            {!artifact && 'No se ha generado ningún documento aún.'}
          </p>
        </div>
      </div>

      <div className="flex items-center gap-3 w-full md:w-auto">
        {artifact?.pdfUrl && (
          <a 
            href={artifact.pdfUrl} 
            target="_blank" 
            rel="noreferrer"
            className="flex-1 md:flex-none flex items-center justify-center px-4 py-2 bg-gray-100 text-gray-700 font-medium rounded-lg hover:bg-gray-200 transition-colors"
          >
            <Download className="w-4 h-4 mr-2" /> Ver PDF
          </a>
        )}

        <button
          onClick={() => generateMutation.mutate()}
          disabled={isGenerating || (isCurrent && !isStale)}
          className={`flex-1 md:flex-none flex items-center justify-center px-4 py-2 font-medium rounded-lg transition-colors ${
            isGenerating || (isCurrent && !isStale)
              ? 'bg-gray-100 text-gray-400 cursor-not-allowed'
              : 'bg-blue-600 text-white hover:bg-blue-700'
          }`}
        >
          {isGenerating ? (
            <><Loader2 className="w-4 h-4 mr-2 animate-spin" /> Procesando...</>
          ) : (
            <><RefreshCw className="w-4 h-4 mr-2" /> {artifact ? 'Actualizar PDF' : 'Generar PDF'}</>
          )}
        </button>
      </div>
    </div>
  );
};
