import type { MeResponse } from '../types/auth.types';
import { useAuthStore } from '../store/useAuthStore';

export const hasModule = (me: MeResponse | null, module: string): boolean =>
  me?.entitlements.includes(module) === true;

export const can = (me: MeResponse | null, module: string, capability: string): boolean =>
  Boolean(me?.workspace && hasModule(me, module)
    && (capability === 'READ' || me.workspace.status !== 'Deleting')
    && me.capabilities[module]?.includes(capability));

export const usePermissions = () => {
  const me = useAuthStore(state => state.me);
  return {
    hasModule: (module: string) => hasModule(me, module),
    can: (module: string, capability: string) => can(me, module, capability),
  };
};
