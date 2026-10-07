import { useState } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Copy, Eye, FileText, Loader2, Plus, RefreshCw, Upload } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { useToast } from '../../../components/ui/useToast';
import { Skeleton, ErrorState } from '../../../components/ui/Feedback';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { useSessionMutation } from '../../../core/query/useSessionMutation';
import { queryKeys } from '../../../core/query/queryKeys';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { getArtifactStatus, generateArtifact, uploadArtifact } from '../services/artifact.service';
import { ArtifactCreationModal } from './ArtifactCreationModal';
import type { ArtifactCreationMode } from './ArtifactCreationModal';
import { PdfPreviewModal } from './PdfPreviewModal';
import type { ArtifactScope } from '../types/artifact.types';

interface ArtifactGeneratorProps {
  scope: ArtifactScope;
  title: string;
}

export const ArtifactGenerator = (props: ArtifactGeneratorProps) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const userId = useAuthStore(state => state.me?.user.id);
  return <WorkspaceArtifactGenerator key={`${userId}:${workspaceId}:${props.scope}`} {...props} workspaceId={workspaceId} />;
};

const WorkspaceArtifactGenerator = ({ scope, title, workspaceId }: ArtifactGeneratorProps & { workspaceId?: string }) => {
  const isPageVisible = usePageVisible();
  const queryClient = useQueryClient();
  const toast = useToast();
  const { can } = usePermissions();
  const module = scope === 'PRODUCT' ? 'CATALOG' : 'SERVICES';
  const canGenerate = can(module, 'GENERATE');
  const [creationMode, setCreationMode] = useState<ArtifactCreationMode | null>(null);
  const [previewOpen, setPreviewOpen] = useState(false);
  const artifactKey = queryKeys.artifacts.byScope(workspaceId, scope);

  const { data: artifact, isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: artifactKey,
    queryFn: ({ signal }) => getArtifactStatus(scope, signal),
    enabled: !!workspaceId && can(module, 'READ'),
    refetchInterval: query => (isPageVisible && query.state.data?.status === 'GENERATING' ? 5000 : false),
  });

  const invalidateArtifact = () => queryClient.invalidateQueries({ queryKey: artifactKey });
  const generateMutation = useSessionMutation({
    mutationFn: generateArtifact,
    onSuccess: async () => {
      setCreationMode(null);
      toast.info('Estamos creando tu nuevo folleto.');
      await invalidateArtifact();
    },
    onError: async (error: unknown) => {
      toast.toastApiError(error);
      await invalidateArtifact();
    },
  });
  const uploadMutation = useSessionMutation({
    mutationFn: uploadArtifact,
    onSuccess: async result => {
      queryClient.setQueryData(artifactKey, result);
      setCreationMode(null);
      toast.success('Tu PDF está listo para compartir.');
      await invalidateArtifact();
    },
    onError: async (error: unknown) => {
      toast.toastApiError(error);
      await invalidateArtifact();
    },
  });

  const pdfUrl = artifact?.pdfUrl && /^https?:\/\//i.test(artifact.pdfUrl) ? artifact.pdfUrl : null;
  const copyLink = async () => {
    if (!pdfUrl) return;
    try {
      await navigator.clipboard.writeText(pdfUrl);
      toast.success('Enlace copiado.');
    } catch {
      toast.error('No pudimos copiar el enlace. Revisa el permiso del portapapeles.');
    }
  };

  if (isLoading) return <Skeleton className="mb-6 h-20 rounded-xl" />;
  if (isError) return <ErrorState onRetry={() => void refetch()} />;
  const isStale = artifact?.status === 'STALE';
  const isGenerating = artifact?.status === 'GENERATING';
  const isCurrent = artifact?.status === 'CURRENT';
  const hasError = artifact?.status === 'FAILED';
  const isNotGenerated = !artifact || artifact.status === 'NOT_GENERATED';
  const isSubmitting = generateMutation.isPending || uploadMutation.isPending;
  const blocked = isGenerating || !canGenerate;

  return (
    <div className="mb-5 rounded-xl border border-line bg-surface px-4 py-4">
      <div className="flex flex-col justify-between gap-4 md:flex-row md:items-center">
        <div className="flex items-start gap-3">
          <div className="rounded-lg bg-primary/10 p-2 text-primary"><FileText aria-hidden="true" className="h-5 w-5" /></div>
          <div>
            <h3 className="text-sm font-semibold text-foreground">{title}</h3>
            <p className={`mt-1 text-sm ${hasError ? 'text-danger' : 'text-muted'}`} role={hasError ? 'alert' : 'status'}>
              {isCurrent && 'Tu folleto está actualizado y listo para compartir.'}
              {isStale && 'Tu folleto puede estar desactualizado.'}
              {isGenerating && 'Estamos creando tu nuevo folleto.'}
              {hasError && 'No pudimos crear tu nueva versión. Puedes reintentar o subir tu PDF.'}
              {isNotGenerated && 'Crea un folleto con NexFlow o sube tu propio PDF.'}
            </p>
            {pdfUrl && <p className="mt-1 text-xs text-muted">{artifact?.origin === 'UPLOADED' ? 'PDF propio' : 'Creado con NexFlow'}</p>}
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {pdfUrl && <Button variant="secondary" onClick={() => setPreviewOpen(true)}>
            <Eye aria-hidden="true" className="h-4 w-4" />
            {isGenerating || hasError ? 'Ver versión anterior' : isStale ? 'Previsualizar versión actual' : 'Previsualizar'}
          </Button>}
          {isCurrent && pdfUrl && <Button variant="secondary" onClick={() => void copyLink()}><Copy aria-hidden="true" className="h-4 w-4" /> Copiar enlace</Button>}
          {isGenerating ? <span className="inline-flex items-center gap-2 text-sm text-muted"><Loader2 aria-hidden="true" className="h-4 w-4 animate-spin" /> En proceso</span> : <>
            <Button variant="secondary" disabled={blocked || isSubmitting} onClick={() => setCreationMode('generate')}>
              {isNotGenerated || isCurrent ? <Plus aria-hidden="true" className="h-4 w-4" /> : <RefreshCw aria-hidden="true" className="h-4 w-4" />}
              {isNotGenerated ? 'Crear folleto' : isCurrent ? 'Crear nueva versión' : isStale ? 'Actualizar con NexFlow' : 'Reintentar'}
            </Button>
            {(isStale || hasError) && <Button variant="secondary" disabled={blocked || isSubmitting} onClick={() => setCreationMode('upload')}>
              <Upload aria-hidden="true" className="h-4 w-4" /> {isStale ? 'Subir nueva versión' : 'Subir PDF'}
            </Button>}
          </>}
        </div>
      </div>
      {creationMode && <ArtifactCreationModal scope={scope} artifact={artifact} initialMode={creationMode}
        isSubmitting={isSubmitting} blocked={blocked} onClose={() => setCreationMode(null)}
        onGenerate={(design, replaceCurrent) => { if (!blocked && !isSubmitting) generateMutation.mutate({ scope, design, replaceCurrent }); }}
        onUpload={(file, replaceCurrent) => { if (!blocked && !isSubmitting) uploadMutation.mutate({ scope, file, replaceCurrent }); }} />}
      {previewOpen && pdfUrl && <PdfPreviewModal pdfUrl={pdfUrl} title={title} onClose={() => setPreviewOpen(false)} onCopy={() => void copyLink()} />}
    </div>
  );
};
