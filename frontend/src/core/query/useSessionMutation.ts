import { useMutation } from '@tanstack/react-query';
import type { UseMutationOptions } from '@tanstack/react-query';
import { getQuerySession, isCurrentQuerySession } from './queryPersistence';

// Preserve TanStack's mutation state while ignoring callbacks from a disconnected identity.
export const useSessionMutation = <TData, TVariables = void>(
  options: Omit<UseMutationOptions<TData, Error, TVariables, number>, 'onMutate'>,
) => {
  const sessionAtRender = getQuerySession();
  return useMutation({
  ...options,
  mutationFn: (variables, context) => {
    if (!isCurrentQuerySession(sessionAtRender)) return Promise.reject(new Error('La sesión ha cambiado.'));
    if (!options.mutationFn) return Promise.reject(new Error('Operación no configurada.'));
    return options.mutationFn(variables, context);
  },
  onMutate: () => sessionAtRender,
  onSuccess: (data, variables, session, context) => {
    if (isCurrentQuerySession(session)) return options.onSuccess?.(data, variables, session, context);
  },
  onError: (error, variables, session, context) => {
    if (isCurrentQuerySession(session)) return options.onError?.(error, variables, session, context);
  },
  onSettled: (data, error, variables, session, context) => {
    if (isCurrentQuerySession(session)) return options.onSettled?.(data, error, variables, session, context);
  },
  });
};
