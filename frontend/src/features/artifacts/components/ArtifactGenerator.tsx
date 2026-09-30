import { getApiErrorPresentation } from '../../../core/api/axiosClient';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { FileText, RefreshCw, Download, Loader2 } from 'lucide-react';
import { getArtifactStatus, generateArtifact } from '../services/artifact.service'; // 🔥 Invocamos el servicio real
import { useAuthStore } from '../../../core/store/useAuthStore';

interface ArtifactGeneratorProps {
  scope: 'PRODUCT' | 'SERVICE';
  title: string;
}

export const ArtifactGenerator = ({ scope, title }: ArtifactGeneratorProps) => {
  const isPageVisible = usePageVisible();
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  
  // 🔥 SPRINT 11: Estado local para manejar el error sin usar alert()
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data: artifact, isLoading } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.artifacts.byScope(workspaceId, scope),
    queryFn: ({ signal }) => getArtifactStatus(scope, signal),
    enabled: !!workspaceId,
    // 🔥 SPRINT 11: Refetch seguro comprobando en MAYÚSCULAS
    refetchInterval: (query) => (isPageVisible && query.state.data?.status === 'GENERATING' ? 5000 : false),
  });

  const generateMutation = useSessionMutation({
    mutationFn: () => generateArtifact(scope),
    onSuccess: () => {
      setErrorMessage(null);
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, scope) });
    },
    onError: (error: unknown) => {
      setErrorMessage(getApiErrorPresentation(error));
    }
  });

  if (isLoading) return <div className="animate-pulse h-16 bg-gray-100 rounded-xl mb-6"></div>;

  // 🔥 SPRINT 11: Validaciones estrictas unificadas
  const isStale = artifact?.status === 'STALE';
  const isGenerating = artifact?.status === 'GENERATING';
  const isCurrent = artifact?.status === 'CURRENT';
  const hasError = artifact?.status === 'FAILED';
  const isNotGenerated = !artifact || artifact.status === 'NOT_GENERATED';

  return (
    <div className="mb-8 p-5 bg-white border border-gray-200 rounded-xl shadow-sm flex flex-col items-start gap-4">
      
      {/* Mensaje de error no intrusivo */}
      {errorMessage && (
        <div className="w-full p-3 text-sm text-red-700 bg-red-50 border border-red-200 rounded-lg">
          {errorMessage}
        </div>
      )}

      <div className="w-full flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
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
              {isNotGenerated && 'No se ha generado ningún documento aún.'}
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
              // 🔥 SPRINT 11: Si no está generado, dice "Generar", de lo contrario "Actualizar"
              <><RefreshCw className="w-4 h-4 mr-2" /> {isNotGenerated ? 'Generar PDF' : 'Actualizar PDF'}</>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};