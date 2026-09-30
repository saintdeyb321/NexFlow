export interface ItemQueryMetricDto {
  id: string;
  name: string;
  queries: number;
}

export interface GlobalMetricsDto {
  conversationsToday: number;
  aiMessagesHandled: number | null;
  humanMessagesHandled: number | null;
  totalHandoffsToday: number | null;
}

export interface CatalogMetricsDto {
  totalProducts: number;
  totalQueriesThisWeek: number | null;
  topQueriedProducts: ItemQueryMetricDto[] | null;
}

export interface ServicesMetricsDto {
  totalServices: number;
  totalQueriesThisWeek: number | null;
  topQueriedServices: ItemQueryMetricDto[] | null;
}

export interface ReservationsMetricsDto {
  reservationsToday: number;
  pending: number | null;
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
