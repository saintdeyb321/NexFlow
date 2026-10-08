import { useQuery } from '@tanstack/react-query';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { queryKeys } from '../../../core/query/queryKeys';
import { queryPolicies } from '../../../core/query/queryPolicies';
import { getBusinessProfile } from '../services/business.service';

export const useBusinessProfile = (enabled = true) => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { can } = usePermissions();
  return useQuery({
    ...queryPolicies.stable, queryKey: queryKeys.business.profile(workspaceId),
    queryFn: ({ signal }) => getBusinessProfile(signal),
    enabled: enabled && !!workspaceId && can('BUSINESS_PROFILE', 'READ'),
  });
};
