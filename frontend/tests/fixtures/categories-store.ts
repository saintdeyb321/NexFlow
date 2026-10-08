import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

export const categoryIdentity: MeResponse = {
  user: { id: 'test-user', email: 'test@example.invalid', firstName: 'Prueba', lastName: 'Local', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Negocio de prueba', status: 'Active' },
  license: null, membershipRole: 'Owner', entitlements: ['CATALOG', 'SERVICES'],
  capabilities: { CATALOG: ['READ', 'CREATE', 'UPDATE', 'DELETE'], SERVICES: ['READ', 'CREATE', 'UPDATE', 'DELETE'] },
};
export const useAuthStore = create<{ me: MeResponse; selectedLocationId: string; setSelectedLocationId: (id: string) => void }>(set => ({
  me: structuredClone(categoryIdentity), selectedLocationId: 'all', setSelectedLocationId: id => set({ selectedLocationId: id }),
}));
