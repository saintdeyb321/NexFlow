import { create } from 'zustand';
import type { MeResponse } from '../../src/core/types/auth.types';

const me: MeResponse = {
  user: { id: 'local-user', email: 'local@example.invalid', firstName: 'Prueba', lastName: 'Local', isSuperAdmin: false },
  workspace: { id: 'workspace-a', name: 'Prueba local', status: 'Active' }, license: null, membershipRole: 'Owner',
  entitlements: ['CONVERSATIONS'], capabilities: { CONVERSATIONS: ['READ', 'TAKEOVER', 'RELEASE', 'SEND_MESSAGE', 'DELETE'] },
};
export const useAuthStore = create(() => ({ me, isAuthenticated: true, isLoading: false, selectedLocationId: 'all' }));
export const changeIdentity = (workspace: string, user = 'local-user', read = true) => useAuthStore.setState({
  me: { ...me, user: { ...me.user, id: user }, workspace: { ...me.workspace!, id: workspace },
    capabilities: { CONVERSATIONS: read ? me.capabilities.CONVERSATIONS : [] } },
});
