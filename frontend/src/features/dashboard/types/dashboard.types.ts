export interface ItemQueryMetricDto {
  id: string;
  name: string;
  queries: number;
}

export interface GlobalMetricsDto {
  conversationsToday: number;
  aiMessagesHandled: number;
  humanMessagesHandled: number;
  totalHandoffsToday: number;
}

export interface CatalogMetricsDto {
  totalProducts: number;
  totalQueriesThisWeek: number;
  topQueriedProducts: ItemQueryMetricDto[];
}

export interface ServicesMetricsDto {
  totalServices: number;
  totalQueriesThisWeek: number;
  topQueriedServices: ItemQueryMetricDto[];
}

export interface ReservationsMetricsDto {
  reservationsToday: number;
  pending: number;
  confirmed: number;
  cancelled: number;
}

export interface RequestsMetricsDto {
  pending: number;
  inReview: number;
  completed: number;
}

export interface DashboardResponseDto {
  global: GlobalMetricsDto;
  catalog: CatalogMetricsDto | null;
  services: ServicesMetricsDto | null;
  reservations: ReservationsMetricsDto | null;
  requests: RequestsMetricsDto | null;
}