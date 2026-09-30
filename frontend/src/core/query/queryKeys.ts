type WorkspaceId = string | null | undefined;
type CategoryScope = 'PRODUCT' | 'SERVICE' | 'SHARED';
const workspace = (id: WorkspaceId) => ['workspace', id ?? null] as const;
const location = (id?: string | null) => !id || id === 'all' ? null : id;

export const queryKeys = {
  workspace,
  business: {
    profile: (id: WorkspaceId) => [...workspace(id), 'business', 'profile'] as const,
    whatsapp: (id: WorkspaceId) => [...workspace(id), 'business', 'whatsapp'] as const,
  },
  locations: { all: (id: WorkspaceId) => [...workspace(id), 'locations'] as const },
  hours: {
    all: (id: WorkspaceId) => [...workspace(id), 'hours'] as const,
    byLocation: (id: WorkspaceId, locationId: string) => [...workspace(id), 'hours', locationId] as const,
  },
  faqs: { all: (id: WorkspaceId) => [...workspace(id), 'faqs'] as const },
  catalog: {
    allProducts: (id: WorkspaceId) => [...workspace(id), 'catalog', 'products'] as const,
    products: (id: WorkspaceId, locationId?: string | null) => [...workspace(id), 'catalog', 'products', location(locationId)] as const,
    allCategories: (id: WorkspaceId) => [...workspace(id), 'catalog', 'categories'] as const,
    categories: (id: WorkspaceId, scope: CategoryScope) => [...workspace(id), 'catalog', 'categories', scope] as const,
  },
  services: {
    all: (id: WorkspaceId) => [...workspace(id), 'services'] as const,
    list: (id: WorkspaceId, locationId?: string | null) => [...workspace(id), 'services', location(locationId)] as const,
  },
  reservations: {
    lists: (id: WorkspaceId) => [...workspace(id), 'reservations', 'list'] as const,
    list: (id: WorkspaceId, locationId: string, date: string) => [...workspace(id), 'reservations', 'list', locationId, date] as const,
    availability: (id: WorkspaceId) => [...workspace(id), 'reservations', 'availability'] as const,
    availabilityByLocation: (id: WorkspaceId, locationId: string) => [...workspace(id), 'reservations', 'availability', locationId] as const,
    slots: (id: WorkspaceId, locationId: string, serviceId: string, date: string) => [...workspace(id), 'reservations', 'availability', locationId, serviceId, date] as const,
  },
  requests: {
    lists: (id: WorkspaceId) => [...workspace(id), 'requests', 'list'] as const,
    list: (id: WorkspaceId, limit: number, status?: string) => [...workspace(id), 'requests', 'list', limit, status === 'ALL' ? null : status ?? null] as const,
    detail: (id: WorkspaceId, requestId: string) => [...workspace(id), 'requests', 'detail', requestId] as const,
    assignees: (id: WorkspaceId) => [...workspace(id), 'requests', 'assignees'] as const,
  },
  orders: {
    lists: (id: WorkspaceId) => [...workspace(id), 'orders', 'list'] as const,
    list: (id: WorkspaceId, status?: string) => [...workspace(id), 'orders', 'list', status === 'ALL' ? null : status ?? null] as const,
    detail: (id: WorkspaceId, orderId: string) => [...workspace(id), 'orders', 'detail', orderId] as const,
  },
  conversations: {
    lists: (id: WorkspaceId) => [...workspace(id), 'conversations'] as const,
    list: (id: WorkspaceId, limit = 50) => [...workspace(id), 'conversations', limit] as const,
  },
  messages: {
    conversation: (id: WorkspaceId, conversationId: string | null) => [...workspace(id), 'messages', conversationId] as const,
    list: (id: WorkspaceId, conversationId: string | null, limit = 50) => [...workspace(id), 'messages', conversationId, limit] as const,
  },
  notifications: { list: (id: WorkspaceId) => [...workspace(id), 'notifications'] as const },
  dashboard: { summary: (id: WorkspaceId) => [...workspace(id), 'dashboard'] as const },
  artifacts: {
    all: (id: WorkspaceId) => [...workspace(id), 'artifacts'] as const,
    byScope: (id: WorkspaceId, scope: 'PRODUCT' | 'SERVICE') => [...workspace(id), 'artifacts', scope] as const,
  },
  system: {
    workspaces: (userId?: string) => ['system', userId ?? null, 'workspaces'] as const,
    modules: (userId?: string) => ['system', userId ?? null, 'modules'] as const,
    templates: (userId?: string) => ['system', userId ?? null, 'templates'] as const,
  },
};
