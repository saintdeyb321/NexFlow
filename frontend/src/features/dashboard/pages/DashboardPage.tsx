import { PageHeader, Card } from '../../../components/ui/Layout';
import { Skeleton, ErrorState } from '../../../components/ui/Feedback';
import { queryPolicies, usePageVisible } from '../../../core/query/queryPolicies';
import { queryKeys } from '../../../core/query/queryKeys';
import { useQuery } from '@tanstack/react-query';
import { MessageSquare, Bot, UserCog, CalendarCheck, ClipboardList, ShoppingBag, Zap, Activity } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { getDashboardSummary } from '../services/dashboard.service';

export const DashboardPage = () => {
  const isPageVisible = usePageVisible();
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;
  const { can } = usePermissions();
  const adminName = me?.user?.firstName || me?.user?.email?.split('@')[0] || 'Administrador';

  const { data, isLoading, isError, refetch } = useQuery({
    ...queryPolicies.dynamic,
    queryKey: queryKeys.dashboard.summary(workspaceId),
    queryFn: ({ signal }) => getDashboardSummary(signal),
    enabled: !!workspaceId && can('CONVERSATIONS', 'READ'),
    refetchInterval: isPageVisible ? 60000 : false,
  });

  if (isLoading) return <div className="nf-page space-y-6"><Skeleton className="h-32" /><div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">{[1,2,3,4].map(i => <Skeleton key={i} className="h-32" />)}</div><div className="grid lg:grid-cols-2 gap-5">{[1,2].map(i => <Skeleton key={i} className="h-56" />)}</div></div>;
  if (isError || !data) return <ErrorState title="No pudimos cargar tu resumen" description="Revisa tu conexión e intenta nuevamente." onRetry={() => void refetch()} />;

  return (
    <div className="nf-page space-y-7 pb-4">
      <div className="nf-dashboard-hero rounded-2xl bg-sidebar p-6 sm:p-8 border border-sidebar-hover relative overflow-hidden">
        <span aria-hidden="true" className="absolute right-0 top-0 w-40 h-40 rounded-full bg-primary/20 blur-2xl" />
        <p className="text-xs text-cyan-300 uppercase tracking-[.15em] font-semibold mb-3">Tu centro de atención</p>
        <PageHeader title={`Hola, ${adminName}`} description="Aquí tienes el resumen de tu atención automatizada." />
      </div>

      <section>
        <h2 className="text-sm font-semibold text-foreground mb-4 flex items-center">
          <Activity aria-hidden="true" className="w-5 h-5 mr-2 text-primary" /> Tráfico de Hoy
        </h2>
        <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-4 gap-4">
          <StatCard icon={<MessageSquare aria-hidden="true" />} title="Conversaciones" value={data.global.conversationsToday} color="blue" />
          <StatCard icon={<Bot aria-hidden="true" />} title="Atendidas por IA" value={data.global.aiMessagesHandled} color="emerald" />
          <StatCard icon={<UserCog aria-hidden="true" />} title="Asesor Humano" value={data.global.humanMessagesHandled} color="purple" />
          <StatCard icon={<UserCog aria-hidden="true" />} title="Derivaciones (Handoffs)" value={data.global.totalHandoffsToday} color="orange" />
        </div>
      </section>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {data.reservations && (
          <ModuleCard title="Citas y Reservas" icon={<CalendarCheck aria-hidden="true" className="w-5 h-5 text-indigo-600" />} colorClass="border-indigo-100">
            <div className="grid gap-4 grid-cols-1 sm:grid-cols-2">
              <MiniStat label="Nuevas Hoy" value={data.reservations.reservationsToday} />
              <MiniStat label="Pendientes" value={data.reservations.pending} />
              <MiniStat label="Confirmadas" value={data.reservations.confirmed} />
              <MiniStat label="Canceladas" value={data.reservations.cancelled} />
            </div>
          </ModuleCard>
        )}

        {data.requests && (
          <ModuleCard title="Solicitudes (Requests)" icon={<ClipboardList aria-hidden="true" className="w-5 h-5 text-amber-600" />} colorClass="border-amber-100">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              <MiniStat label="Pendientes" value={data.requests.pending} />
              <MiniStat label="En Revisión" value={data.requests.inReview} />
              <MiniStat label="Completadas" value={data.requests.completed} />
            </div>
          </ModuleCard>
        )}

        {data.catalog && (
          <ModuleCard title="Catálogo de Productos" icon={<ShoppingBag aria-hidden="true" className="w-5 h-5 text-emerald-600" />} colorClass="border-emerald-100">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <MiniStat label="Productos Totales" value={data.catalog.totalProducts} />
              <MiniStat label="Consultas IA (Semana)" value={data.catalog.totalQueriesThisWeek} />
            </div>
          </ModuleCard>
        )}

        {data.services && (
          <ModuleCard title="Portafolio de Servicios" icon={<Zap aria-hidden="true" className="w-5 h-5 text-purple-600" />} colorClass="border-purple-100">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <MiniStat label="Servicios Activos" value={data.services.totalServices} />
              <MiniStat label="Consultas IA (Semana)" value={data.services.totalQueriesThisWeek} />
            </div>
          </ModuleCard>
        )}
      </div>
    </div>
  );
};

const StatCard = ({ icon, title, value, color }: { icon: React.ReactNode, title: string, value: number | null, color: 'blue' | 'emerald' | 'purple' | 'orange' }) => {
  const colorStyles = {
    blue: 'bg-indigo-50 text-primary',
    emerald: 'bg-emerald-50 text-emerald-600',
    purple: 'bg-violet-50 text-violet-700',
    orange: 'bg-amber-50 text-amber-700',
  };

  return (
    <div className="nf-panel p-5 flex items-start gap-4">
      <div className={`w-12 h-12 rounded-xl flex items-center justify-center shrink-0 ${colorStyles[color]}`}>
        {icon}
      </div>
      <div>
        <p className="text-xs text-muted font-medium mb-2">{title}</p>
        <h3 className="text-3xl font-semibold tracking-tight text-foreground">{value ?? '—'}</h3>
      </div>
    </div>
  );
};

const ModuleCard = ({ title, icon, colorClass, children }: { title: string, icon: React.ReactNode, colorClass: string, children: React.ReactNode }) => (
  <Card className={`border-t-4 ${colorClass}`}>
    <div className="flex items-center mb-4 pb-3 border-b border-gray-50">
      {icon}
      <h3 className="ml-2 text-lg font-semibold text-foreground">{title}</h3>
    </div>
    {children}
  </Card>
);

const MiniStat = ({ label, value }: { label: string, value: number | null }) => (
  <div className="bg-surface-soft rounded-xl p-4 min-w-0">
    <p className="text-[11px] text-muted uppercase tracking-wider font-semibold mb-1">{label}</p>
    <p className="text-3xl font-semibold tracking-tight text-foreground">{value ?? '—'}</p>
  </div>
);
