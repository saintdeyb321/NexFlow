import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/Toast';
import { Skeleton, ErrorState } from '../../../components/ui/Feedback';

import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';

import { useQuery, useQueryClient } from '@tanstack/react-query';
import { FileText, RefreshCw, Download, Loader2 } from 'lucide-react';
import { getArtifactStatus, generateArtifact } from '../services/artifact.service';
import { useAuthStore } from '../../../core/store/useAuthStore';

interface ArtifactGeneratorProps {
  scope: 'PRODUCT' | 'SERVICE';
  title: string;
}

export const ArtifactGenerator = ({ scope, title }: ArtifactGeneratorProps) => {
  const isPageVisible = usePageVisible();
  const queryClient = useQueryClient();
  const toast = useToast();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);

  const { data: artifact, isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.artifacts.byScope(workspaceId, scope),
    queryFn: ({ signal }) => getArtifactStatus(scope, signal),
    enabled: !!workspaceId,
    refetchInterval: (query) => (isPageVisible && query.state.data?.status === 'GENERATING' ? 5000 : false),
  });

  const generateMutation = useSessionMutation({
    mutationFn: () => generateArtifact(scope),
    onSuccess: () => {
      toast.info('Generación solicitada.');
      queryClient.invalidateQueries({ queryKey: queryKeys.artifacts.byScope(workspaceId, scope) });
    },
    onError: (error: unknown) => {
      toast.toastApiError(error);
    }
  });

  if (isLoading) return <Skeleton className="h-16 rounded-xl mb-6" />;

  if (isError) return <ErrorState onRetry={() => void refetch()} />;
  const isStale = artifact?.status === 'STALE';
  const isGenerating = artifact?.status === 'GENERATING';
  const isCurrent = artifact?.status === 'CURRENT';
  const hasError = artifact?.status === 'FAILED';
  const isNotGenerated = !artifact || artifact.status === 'NOT_GENERATED';

  return (
    <div className="mb-5 px-4 py-3 bg-indigo-50/50 border border-indigo-100 rounded-xl flex flex-col items-start gap-3">

      <div className="w-full flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
        <div className="flex items-start gap-3">
          <div className={`p-2 rounded-lg ${isCurrent ? 'bg-green-100 text-green-700' : isStale ? 'bg-yellow-100 text-yellow-700' : isGenerating ? 'bg-blue-100 text-blue-700' : 'bg-red-100 text-red-700'}`}>
            <FileText aria-hidden="true" className="w-5 h-5" />
          </div>
          <div>
            <h3 className="font-semibold text-foreground text-sm">{title}</h3>
            <p className="text-sm text-muted mt-0.5">
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
              <Download aria-hidden="true" className="w-4 h-4 mr-2" /> Ver PDF
            </a>
          )}

          <Button variant="secondary"
            onClick={() => generateMutation.mutate()}
            disabled={isGenerating || (isCurrent && !isStale)}
            className="flex-1 md:flex-none"
          >
            {isGenerating ? (
              <><Loader2 aria-hidden="true" className="w-4 h-4 mr-2 animate-spin" /> Procesando...</>
            ) : (
              <><RefreshCw aria-hidden="true" className="w-4 h-4 mr-2" /> {isNotGenerated ? 'Generar PDF' : 'Actualizar PDF'}</>
            )}
          </Button>
        </div>
      </div>
    </div>
  );
};
