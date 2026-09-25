import { useQuery } from '@tanstack/react-query';
import { MessageSquare, Bot, UserCog, CalendarCheck, ClipboardList, ShoppingBag, Zap, Activity } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getDashboardSummary } from '../services/dashboard.service';

export const DashboardPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;

  const { data, isLoading, isError } = useQuery({
    queryKey: ['dashboard', workspaceId],
    queryFn: getDashboardSummary,
    enabled: !!workspaceId,
    refetchInterval: 60000, // Refresca cada minuto automáticamente
  });

  if (isLoading) {
    return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando métricas de atención...</div>;
  }

  if (isError || !data) {
    return <div className="p-8 text-center text-red-500">Error al cargar el panel de control. Intenta nuevamente.</div>;
  }

  return (
    <div className="max-w-7xl mx-auto animate-in fade-in space-y-8">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-bold text-gray-900">Hola, {me?.user.firstName} 👋</h1>
        <p className="mt-1 text-sm text-gray-500">Aquí tienes el resumen de tu centro de atención automatizada.</p>
      </div>

      {/* Módulo Global (Siempre presente) */}
      <section>
        <h2 className="text-lg font-semibold text-gray-800 mb-4 flex items-center">
          <Activity className="w-5 h-5 mr-2 text-blue-600" /> Tráfico de Hoy
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <StatCard icon={<MessageSquare />} title="Conversaciones" value={data.global.conversationsToday} color="blue" />
          <StatCard icon={<Bot />} title="Atendidas por IA" value={data.global.aiMessagesHandled} color="emerald" />
          <StatCard icon={<UserCog />} title="Asesor Humano" value={data.global.humanMessagesHandled} color="purple" />
          <StatCard icon={<UserCog />} title="Derivaciones (Handoffs)" value={data.global.totalHandoffsToday} color="orange" />
        </div>
      </section>

      {/* Mini-Dashboards Condicionales por Licencia */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        
        {data.reservations && (
          <ModuleCard title="Citas y Reservas" icon={<CalendarCheck className="w-5 h-5 text-indigo-600" />} colorClass="border-indigo-100">
            <div className="grid grid-cols-2 gap-4">
              <MiniStat label="Nuevas Hoy" value={data.reservations.reservationsToday} />
              <MiniStat label="Pendientes" value={data.reservations.pending} />
              <MiniStat label="Confirmadas" value={data.reservations.confirmed} />
              <MiniStat label="Canceladas" value={data.reservations.cancelled} />
            </div>
          </ModuleCard>
        )}

        {data.requests && (
          <ModuleCard title="Solicitudes (Requests)" icon={<ClipboardList className="w-5 h-5 text-amber-600" />} colorClass="border-amber-100">
            <div className="grid grid-cols-3 gap-4">
              <MiniStat label="Pendientes" value={data.requests.pending} />
              <MiniStat label="En Revisión" value={data.requests.inReview} />
              <MiniStat label="Completadas" value={data.requests.completed} />
            </div>
          </ModuleCard>
        )}

        {data.catalog && (
          <ModuleCard title="Catálogo de Productos" icon={<ShoppingBag className="w-5 h-5 text-emerald-600" />} colorClass="border-emerald-100">
            <div className="flex justify-between items-end mb-4">
              <MiniStat label="Productos Totales" value={data.catalog.totalProducts} />
              <MiniStat label="Consultas IA (Semana)" value={data.catalog.totalQueriesThisWeek} />
            </div>
            <div className="text-sm text-gray-500 border-t pt-3 mt-3">
              <p className="font-medium mb-2">Más consultados:</p>
              <ul className="space-y-1">
                {data.catalog.topQueriedProducts.map(p => (
                  <li key={p.id} className="flex justify-between">
                    <span className="truncate pr-2">{p.name}</span>
                    <span className="font-medium text-gray-900">{p.queries}</span>
                  </li>
                ))}
              </ul>
            </div>
          </ModuleCard>
        )}

        {data.services && (
          <ModuleCard title="Portafolio de Servicios" icon={<Zap className="w-5 h-5 text-purple-600" />} colorClass="border-purple-100">
            <div className="flex justify-between items-end mb-4">
              <MiniStat label="Servicios Activos" value={data.services.totalServices} />
              <MiniStat label="Consultas IA (Semana)" value={data.services.totalQueriesThisWeek} />
            </div>
            <div className="text-sm text-gray-500 border-t pt-3 mt-3">
              <p className="font-medium mb-2">Más consultados:</p>
              <ul className="space-y-1">
                {data.services.topQueriedServices.map(s => (
                  <li key={s.id} className="flex justify-between">
                    <span className="truncate pr-2">{s.name}</span>
                    <span className="font-medium text-gray-900">{s.queries}</span>
                  </li>
                ))}
              </ul>
            </div>
          </ModuleCard>
        )}

      </div>
    </div>
  );
};

// --- Subcomponentes de UI Privados para limpiar el código ---

const StatCard = ({ icon, title, value, color }: { icon: React.ReactNode, title: string, value: number, color: 'blue' | 'emerald' | 'purple' | 'orange' }) => {
  const colorStyles = {
    blue: 'bg-blue-50 text-blue-600',
    emerald: 'bg-emerald-50 text-emerald-600',
    purple: 'bg-purple-50 text-purple-600',
    orange: 'bg-orange-50 text-orange-600',
  };

  return (
    <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-5 flex items-center">
      <div className={`w-12 h-12 rounded-full flex items-center justify-center mr-4 ${colorStyles[color]}`}>
        {icon}
      </div>
      <div>
        <p className="text-sm text-gray-500 font-medium">{title}</p>
        <h3 className="text-2xl font-bold text-gray-900">{value}</h3>
      </div>
    </div>
  );
};

const ModuleCard = ({ title, icon, colorClass, children }: { title: string, icon: React.ReactNode, colorClass: string, children: React.ReactNode }) => (
  <div className={`bg-white rounded-xl shadow-sm border-t-4 ${colorClass} border-x border-b border-gray-100 p-6`}>
    <div className="flex items-center mb-4">
      {icon}
      <h3 className="ml-2 text-lg font-semibold text-gray-800">{title}</h3>
    </div>
    {children}
  </div>
);

const MiniStat = ({ label, value }: { label: string, value: number }) => (
  <div>
    <p className="text-xs text-gray-500 uppercase tracking-wide">{label}</p>
    <p className="text-xl font-bold text-gray-900">{value}</p>
  </div>
);