import { createRoot } from 'react-dom/client';
import { MemoryRouter } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { queryClient } from '../../src/core/query/queryClient';
import { queryKeys } from '../../src/core/query/queryKeys';
import { InboxPage } from '../../src/features/conversations/pages/InboxPage';
import { ToastProvider } from '../../src/components/ui/Toast';
import { fixture, calls } from './rc3-inbox-api';
import '../../src/index.css';

queryClient.setDefaultOptions({ queries: { retry: false, refetchOnWindowFocus: false } });
const listKey = queryKeys.conversations.list('workspace-a');
const messagesKey = queryKeys.messages.list('workspace-a', 'chat-rc3');
Object.assign(window, { rc3Inbox: {
  calls,
  fail: async (panel: typeof fixture.fail) => {
    fixture.fail = panel;
    await queryClient.refetchQueries({ queryKey: panel === 'messages' ? messagesKey : listKey, exact: true });
  },
  snapshot: () => ({
    listCount: queryClient.getQueryData<unknown[]>(listKey)?.length ?? 0,
    messageCount: queryClient.getQueryData<unknown[]>(messagesKey)?.length ?? 0,
    listStatus: queryClient.getQueryState(listKey)?.status,
    messageStatus: queryClient.getQueryState(messagesKey)?.status,
  }),
} });
createRoot(document.getElementById('root')!).render(<QueryClientProvider client={queryClient}><ToastProvider>
  <MemoryRouter initialEntries={['/inbox?conversation=chat-rc3']}><InboxPage /></MemoryRouter>
</ToastProvider></QueryClientProvider>);
