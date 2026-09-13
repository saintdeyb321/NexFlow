import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { FileText, RefreshCw, CheckCircle2, AlertCircle, Clock } from 'lucide-react';
import { getArtifactStatus, generateArtifact } from '../services/catalog.service';
import { useAuthStore } from '../../../core/store/useAuthStore';

interface ArtifactGeneratorProps {
  scope: 'PRODUCT' | 'SERVICE';
  title: string;
}

export const ArtifactGenerator = ({ scope, title }: ArtifactGeneratorProps) => {
  const queryClient = useQueryClient();
  const workspaceId = useAuthStore((state) => state.me?.workspace?.id);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const { data: artifact, isLoading } = useQuery({
    // 🔥 Separar el caché por scope para evitar choques
    queryKey: ['catalogArtifact', workspaceId, scope], 
    queryFn: () => getArtifactStatus(scope),
    enabled: !!workspaceId,
    // 🔥 Corrección v5: usar la forma segura del query para evitar loops
    refetchInterval: (query) => (query.state.data?.status === 'GENERATING' ? 5000 : false)
  });

  const generateMutation = useMutation({
    mutationFn: () => generateArtifact(scope),
    onSuccess: () => {
      setErrorMessage(null);
      queryClient.invalidateQueries({ queryKey: ['catalogArtifact', workspaceId, scope] });
    },
    onError: (error: any) => {
      setErrorMessage(error.message || 'Error al solicitar la generación.');
    }
  });

  if (isLoading) return <div className="animate-pulse h-24 bg-gray-100 rounded-xl mb-6"></div>;

  const status = artifact?.status || 'NOT_GENERATED';
  const isGenerating = status === 'GENERATING' || generateMutation.isPending;

  return (
    <div className="bg-white border border-gray-200 rounded-xl p-5 mb-6 shadow-sm flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
      <div className="flex items-start">
        <div className={`p-3 rounded-lg mr-4 ${scope === 'PRODUCT' ? 'bg-blue-100 text-blue-600' : 'bg-purple-100 text-purple-600'}`}>
          <FileText className="w-6 h-6" />
        </div>
        <div>
          <h3 className="font-bold text-gray-900">{title}</h3>
          <div className="flex items-center mt-1 text-sm">
            {status === 'CURRENT' && <span className="text-green-600 flex items-center font-medium"><CheckCircle2 className="w-4 h-4 mr-1"/> Actualizado</span>}
            {status === 'STALE' && <span className="text-orange-500 flex items-center font-medium"><AlertCircle className="w-4 h-4 mr-1"/> Hay cambios sin publicar</span>}
            {status === 'NOT_GENERATED' && <span className="text-gray-500 flex items-center">Nunca generado</span>}
            {status === 'FAILED' && <span className="text-red-600 flex items-center font-medium"><AlertCircle className="w-4 h-4 mr-1"/> Falló última generación</span>}
            {isGenerating && <span className="text-blue-600 flex items-center font-medium animate-pulse"><RefreshCw className="w-4 h-4 mr-1 animate-spin"/> Generando...</span>}
            
            {artifact?.lastGeneratedAt && (
              <span className="text-gray-400 ml-3 flex items-center text-xs">
                <Clock className="w-3 h-3 mr-1"/> 
                Última vez: {new Date(artifact.lastGeneratedAt).toLocaleDateString()}
              </span>
            )}
          </div>
          {errorMessage && <p className="text-red-500 text-xs mt-1">{errorMessage}</p>}
        </div>
      </div>

      <div className="flex gap-3 w-full md:w-auto">
        {artifact?.pdfUrl && status !== 'GENERATING' && (
          <a 
            href={artifact.pdfUrl} 
            target="_blank" 
            rel="noreferrer"
            className="flex-1 md:flex-none text-center px-4 py-2 bg-gray-100 hover:bg-gray-200 text-gray-700 font-medium rounded-lg text-sm transition-colors"
          >
            Ver Publicación
          </a>
        )}
        <button 
          onClick={() => generateMutation.mutate()}
          disabled={isGenerating || status === 'CURRENT'}
          className={`flex-1 md:flex-none flex items-center justify-center px-4 py-2 text-white font-medium rounded-lg text-sm transition-colors disabled:opacity-50 ${scope === 'PRODUCT' ? 'bg-blue-600 hover:bg-blue-700' : 'bg-purple-600 hover:bg-purple-700'}`}
        >
          {isGenerating ? (
            <><RefreshCw className="w-4 h-4 mr-2 animate-spin" /> Procesando</>
          ) : status === 'CURRENT' ? (
            <><CheckCircle2 className="w-4 h-4 mr-2" /> Al día</>
          ) : (
            <><FileText className="w-4 h-4 mr-2" /> {status === 'STALE' ? 'Regenerar PDF' : 'Generar PDF'}</>
          )}
        </button>
      </div>
    </div>
  );
};