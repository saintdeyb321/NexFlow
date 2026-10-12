import axios, { AxiosError } from 'axios';
import { axiosClient, ApiError, getApiErrorPresentation } from '../../src/core/api/axiosClient';
import { useAuthStore } from './reservations-store';
import type { ArtifactStatusDto, ArtifactScope } from '../../src/features/artifacts/types/artifact.types';

export { axiosClient, ApiError, getApiErrorPresentation };
export const states = new Map<string, ArtifactStatusDto>();
export const quota = new Map<string, number>();
export const calls: { method: string; path: string; scope: ArtifactScope; workspace: string; generationId?: string; timeout: number }[] = [];
export const fixture = { outcome: 'accepted', cancelOutcome: 'accepted', getError: false, getTimeout: false, delay: 0, postDelay: 0 };
let sequence = 0;
export const key = (workspace: string, scope: ArtifactScope) => `${workspace}:${scope}`;
export const initial = (): ArtifactStatusDto => ({ status: 'NOT_GENERATED', pdfUrl: null, lastGeneratedAt: null, origin: 'GENERATED', visualStyle: null, palette: null, creativity: null, generationId: null, failureCode: null });
axiosClient.defaults.adapter = async config => {
  const workspace = String(config.headers['X-Workspace-Id'] ?? useAuthStore.getState().me?.workspace?.id ?? '');
  const path = config.url!.split('?')[0];
  const data = typeof config.data === 'string' ? JSON.parse(config.data) : config.data;
  const scope = (data?.scope ?? new URLSearchParams(config.url!.split('?')[1]).get('scope')) as ArtifactScope;
  const artifactKey = key(workspace, scope);
  let artifact = structuredClone(states.get(artifactKey) ?? initial());
  calls.push({ method: config.method!, path, workspace, scope, generationId: data?.generationId, timeout: config.timeout! });
  const behavior = { ...fixture };
  const timeout = (code = 'ECONNABORTED') => new AxiosError('Simulated HTTP deadline', code, config, {});
  const delay = config.method === 'get' ? behavior.delay : behavior.postDelay;
  if (delay) await new Promise(resolve => setTimeout(resolve, delay));
  if (config.method === 'get') {
    if (behavior.outcome === 'network') throw new AxiosError('Simulated network disconnect', 'ERR_NETWORK', config, {});
    if (behavior.getTimeout) throw timeout('ETIMEDOUT');
    if (behavior.getError) throw new ApiError(503, 'Dependency.Unavailable', 'No se pudo confirmar el estado del folleto.');
  } else if (path.endsWith('/generate')) {
    if (behavior.outcome === 'timeout-rejected') throw timeout('ETIMEDOUT');
    if (artifact.status === 'GENERATING') throw new ApiError(409, 'Resource.Conflict', 'Generación en curso.');
    if ((quota.get(workspace) ?? 0) >= 3) throw new ApiError(429, 'RateLimit.Exceeded', 'Cuota diaria agotada.');
    quota.set(workspace, (quota.get(workspace) ?? 0) + 1);
    artifact = { ...artifact, ...data.design, status: 'GENERATING', generationId: `generation-${++sequence}`, failureCode: null };
    states.set(artifactKey, artifact);
    if (behavior.outcome === 'uncertain') throw new ApiError(503, 'Dependency.Unavailable', 'Respuesta HTTP incierta; consulta el estado.');
    if (behavior.outcome === 'hang-accepted') return axios.getAdapter('xhr')({ ...config, baseURL: location.origin, url: '/tests/artifact-http-hang' });
    if (behavior.outcome === 'timeout-failed') states.set(artifactKey, { ...artifact, status: 'FAILED', failureCode: 'Catalog.GenerationTimeout' });
    if (behavior.outcome === 'timeout-current') states.set(artifactKey, { ...artifact, status: 'CURRENT', pdfUrl: 'https://storage.example.test/ready.pdf' });
    if (behavior.outcome.startsWith('timeout-')) throw timeout();
  } else if (path.endsWith('/cancel')) {
    if (artifact.generationId !== data.generationId || artifact.status !== 'GENERATING') throw new ApiError(409, 'Resource.Conflict', 'La generación cambió.');
    artifact = { ...artifact, status: 'FAILED', failureCode: 'Catalog.GenerationCancelled' };
    states.set(artifactKey, artifact);
    if (behavior.cancelOutcome === 'hang-accepted') return axios.getAdapter('xhr')({ ...config, baseURL: location.origin, url: '/tests/artifact-http-hang' });
    if (behavior.cancelOutcome === 'timeout-accepted') throw timeout();
  }
  return { data: structuredClone(artifact), status: 200, statusText: 'OK', config, headers: {} };
};
