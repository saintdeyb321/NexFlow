import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

export const hoursIdentity: MeResponse = {
  user: { id: 'test-user', email: 'test@example.invalid', firstName: 'Prueba', lastName: 'Local', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Negocio de prueba', status: 'Active' },
  license: null, membershipRole: 'Owner', entitlements: ['BUSINESS_HOURS'], capabilities: { BUSINESS_HOURS: ['READ', 'UPDATE'] },
};
export const useAuthStore = create<{ me: MeResponse | null; selectedLocationId: string; setSelectedLocationId: (id: string) => void }>(set => ({
  me: structuredClone(hoursIdentity), selectedLocationId: 'all', setSelectedLocationId: id => set({ selectedLocationId: id }),
}));
