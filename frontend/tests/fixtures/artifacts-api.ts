import axios from 'axios';
import { useAuthStore } from './reservations-store';
import type { ArtifactStatusDto, ArtifactScope } from '../../src/features/artifacts/types/artifact.types';

export class ApiError extends Error {
  constructor(public status: number, public code: string, message: string) { super(message); }
}
export const getApiErrorPresentation = (error: unknown) => error instanceof Error ? error.message : 'Error de prueba';
export const states = new Map<string, ArtifactStatusDto>();
export const quota = new Map<string, number>();
export const calls: { method: string; path: string; scope: ArtifactScope; workspace: string; generationId?: string }[] = [];
export const fixture = { outcome: 'accepted', getError: false, delay: 0 };
let sequence = 0;
export const key = (workspace: string, scope: ArtifactScope) => `${workspace}:${scope}`;
export const initial = (): ArtifactStatusDto => ({ status: 'NOT_GENERATED', pdfUrl: null, lastGeneratedAt: null, origin: 'GENERATED', visualStyle: null, palette: null, creativity: null, generationId: null, failureCode: null });
export const axiosClient = axios.create({ adapter: async config => {
  const workspace = useAuthStore.getState().me?.workspace?.id ?? '';
  const path = config.url!.split('?')[0];
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  const scope = (data?.scope ?? new URLSearchParams(config.url!.split('?')[1]).get('scope')) as ArtifactScope;
  const artifactKey = key(workspace, scope);
  let artifact = structuredClone(states.get(artifactKey) ?? initial());
  calls.push({ method: config.method!, path, workspace, scope, generationId: data?.generationId });
  const delay = fixture.delay;
  if (delay) await new Promise(resolve => setTimeout(resolve, delay));
  if (config.method === 'get') {
    if (fixture.getError) throw new ApiError(503, 'Dependency.Unavailable', 'No se pudo confirmar el estado del folleto.');
  } else if (path.endsWith('/generate')) {
    if (artifact.status === 'GENERATING') throw new ApiError(409, 'Resource.Conflict', 'Generación en curso.');
    if ((quota.get(workspace) ?? 0) >= 3) throw new ApiError(429, 'RateLimit.Exceeded', 'Cuota diaria agotada.');
    quota.set(workspace, (quota.get(workspace) ?? 0) + 1);
    artifact = { ...artifact, ...data.design, status: 'GENERATING', generationId: `generation-${++sequence}`, failureCode: null };
    states.set(artifactKey, artifact);
    if (fixture.outcome === 'uncertain') throw new ApiError(503, 'Dependency.Unavailable', 'Respuesta HTTP incierta; consulta el estado.');
  } else if (path.endsWith('/cancel')) {
    if (artifact.generationId !== data.generationId || artifact.status !== 'GENERATING') throw new ApiError(409, 'Resource.Conflict', 'La generación cambió.');
    artifact = { ...artifact, status: 'FAILED', failureCode: 'Catalog.GenerationCancelled' };
    states.set(artifactKey, artifact);
  }
  return { data: structuredClone(artifact), status: 200, statusText: 'OK', config, headers: {} };
} });
