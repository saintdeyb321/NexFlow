import { dehydrate, hashKey, hydrate } from '@tanstack/react-query';
import type { DehydratedQuery, QueryKey } from '@tanstack/react-query';
import type { MeResponse } from '../types/auth.types';
import { queryClient } from './queryClient';
import { STABLE_TTL, stableQueryModule } from './queryPolicies';

const SCHEMA_VERSION = 1;
const PREFIX = `nexflow:qcache:v${SCHEMA_VERSION}:`;
type Identity = { userId: string; workspaceId: string; permissions: string; readable: Set<string> };
let identity: Identity | null = null;
let generation = 0;
let pendingWrite = false;
export const getQuerySession = () => generation;
export const isCurrentQuerySession = (session: number | undefined) => session === generation;
const namespace = (who: Identity) => `${PREFIX}${who.userId}:${who.workspaceId}`;
const canPersist = (key: QueryKey, who: Identity): boolean => {
  if (key[1] !== who.workspaceId) return false;
  const module = stableQueryModule(key);
  const readable = (code: string) => who.readable.has(code);
  return module === 'SHARED' ? readable('CATALOG') || readable('SERVICES') : module !== null && readable(module);
};
const fresh = (updatedAt: number) => Number.isFinite(updatedAt) && updatedAt > 0 && updatedAt <= Date.now() && Date.now() - updatedAt < STABLE_TTL;

export const persistQueryCache = () => {
  if (!identity?.workspaceId) return;
  try {
    const state = dehydrate(queryClient, {
      shouldDehydrateMutation: () => false,
      shouldDehydrateQuery: query => canPersist(query.queryKey, identity!) && query.state.status === 'success'
        && !query.state.isInvalidated && fresh(query.state.dataUpdatedAt),
    });
    sessionStorage.setItem(namespace(identity), JSON.stringify({ schema: SCHEMA_VERSION, userId: identity.userId, workspaceId: identity.workspaceId, state }));
  } catch { /* Storage may be unavailable; the in-memory QueryClient remains authoritative. */ }
};

const restoreQueryCache = (who: Identity) => {
  try {
    const raw = sessionStorage.getItem(namespace(who));
    if (!raw) return;
    const payload: unknown = JSON.parse(raw);
    if (!payload || typeof payload !== 'object' || !('schema' in payload) || payload.schema !== SCHEMA_VERSION
      || !('userId' in payload) || payload.userId !== who.userId || !('workspaceId' in payload) || payload.workspaceId !== who.workspaceId
      || !('state' in payload) || !payload.state || typeof payload.state !== 'object'
      || !('queries' in payload.state) || !Array.isArray(payload.state.queries)) return;
    const queries = payload.state.queries.filter((value: unknown): value is DehydratedQuery => {
      if (!value || typeof value !== 'object' || !('queryKey' in value) || !Array.isArray(value.queryKey)
        || !canPersist(value.queryKey, who) || !('queryHash' in value) || value.queryHash !== hashKey(value.queryKey)
        || !('state' in value) || !value.state || typeof value.state !== 'object') return false;
      const state = value.state;
      return 'status' in state && state.status === 'success' && 'data' in state && state.data !== undefined
        && 'isInvalidated' in state && state.isInvalidated === false
        && 'dataUpdatedAt' in state && typeof state.dataUpdatedAt === 'number' && fresh(state.dataUpdatedAt);
    });
    hydrate(queryClient, { mutations: [], queries });
  } catch { /* Discard malformed or inaccessible storage. */ }
};

export const disconnectQueryIdentity = () => {
  identity = null;
  generation++;
  void queryClient.cancelQueries();
  queryClient.clear();
};

export const connectQueryIdentity = (me: MeResponse) => {
  const userId = me.user.id;
  const workspaceId = me.workspace?.id ?? '';
  const permissions = JSON.stringify([me.workspace?.status, [...me.entitlements].sort(),
    Object.entries(me.capabilities).sort(([a], [b]) => a.localeCompare(b)).map(([module, caps]) => [module, [...caps].sort()])]);
  if (identity?.userId === userId && identity.workspaceId === workspaceId && identity.permissions === permissions) return;
  persistQueryCache();
  disconnectQueryIdentity();
  const readable = new Set(me.entitlements.filter(module => me.capabilities[module]?.includes('READ')));
  const next = { userId, workspaceId, permissions, readable };
  if (workspaceId) restoreQueryCache(next);
  identity = next;
  persistQueryCache();
};

export const clearNexFlowStorage = () => {
  for (const name of ['localStorage', 'sessionStorage'] as const) {
    try {
      const storage = window[name];
      const keys = Array.from({ length: storage.length }, (_, index) => storage.key(index));
      for (const key of keys) if (key?.startsWith('nexflow:')) storage.removeItem(key);
    } catch { /* Restricted storage must not prevent logout. */ }
  }
};

queryClient.getQueryCache().subscribe(event => {
  if (!identity || !stableQueryModule(event.query.queryKey) || pendingWrite) return;
  pendingWrite = true;
  const session = generation;
  queueMicrotask(() => {
    pendingWrite = false;
    if (isCurrentQuerySession(session)) persistQueryCache();
  });
});
window.addEventListener('pagehide', persistQueryCache);
