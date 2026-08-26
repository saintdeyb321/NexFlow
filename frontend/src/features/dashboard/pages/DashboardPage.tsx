import { useEffect, useState } from 'react';
import { Users, Calendar, Activity, Zap } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { getServices, getLocations } from '../../business/services/business.service';
import { getReservations } from '../../reservations/services/reservation.service';

export const DashboardPage = () => {
  const { me } = useAuthStore();
  const [stats, setStats] = useState({ services: 0, todayReservations: 0 });
  const [isLoading, setIsLoading] = useState(true);

  // Extraemos los permisos (módulos) de la sesión
  const entitlements = me?.entitlements || [];

  useEffect(() => {
    const loadDashboardData = async () => {
      try {
        const today = new Date().toISOString().split('T')[0];
        
        let servicesCount = 0;
        let reservationsCount = 0;

        // 🔥 CORRECCIÓN (Fase 2): Solo llamamos a los endpoints si la licencia lo permite
        if (entitlements.includes('SERVICES')) {
          const services = await getServices().catch(() => []);
          servicesCount = services.length;
        }

        if (entitlements.includes('RESERVATIONS')) {
          const locs = await getLocations().catch(() => []);
          const mainLocId = locs.find(l => l.isMain)?.id || locs[0]?.id;
          
          if (mainLocId) {
            const reservations = await getReservations(mainLocId, today).catch(() => []);
            reservationsCount = reservations.length;
          }
        }

        setStats({
          services: servicesCount,
          todayReservations: reservationsCount
        });
      } finally {
        setIsLoading(false);
      }
    };

    loadDashboardData();
  }, [entitlements]);

  if (isLoading) return <div className="animate-pulse flex h-64 items-center justify-center text-gray-500">Cargando métricas...</div>;

  return (
    <div className="max-w-6xl">
      <div className="mb-8">
        <h1 className="text-2xl font-bold text-gray-900">Hola, {me?.user.firstName} 👋</h1>
        <p className="mt-1 text-sm text-gray-500">Aquí tienes un resumen de la actividad de tu negocio hoy.</p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-8">
        
        {entitlements.includes('RESERVATIONS') && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex items-center">
            <div className="w-12 h-12 rounded-full bg-blue-50 text-blue-600 flex items-center justify-center mr-4">
              <Calendar className="w-6 h-6" />
            </div>
            <div>
              <p className="text-sm text-gray-500 font-medium">Citas Hoy</p>
              <h3 className="text-2xl font-bold text-gray-900">{stats.todayReservations}</h3>
            </div>
          </div>
        )}

        {entitlements.includes('SERVICES') && (
          <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex items-center">
            <div className="w-12 h-12 rounded-full bg-purple-50 text-purple-600 flex items-center justify-center mr-4">
              <Zap className="w-6 h-6" />
            </div>
            <div>
              <p className="text-sm text-gray-500 font-medium">Servicios Activos</p>
              <h3 className="text-2xl font-bold text-gray-900">{stats.services}</h3>
            </div>
          </div>
        )}

        <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex items-center">
          <div className="w-12 h-12 rounded-full bg-green-50 text-green-600 flex items-center justify-center mr-4">
            <Users className="w-6 h-6" />
          </div>
          <div>
            <p className="text-sm text-gray-500 font-medium">Estado de IA</p>
            <h3 className="text-lg font-bold text-green-600">En Línea</h3>
          </div>
        </div>

        <div className="bg-white rounded-xl shadow-sm border border-gray-100 p-6 flex items-center">
          <div className="w-12 h-12 rounded-full bg-orange-50 text-orange-600 flex items-center justify-center mr-4">
            <Activity className="w-6 h-6" />
          </div>
          <div>
            <p className="text-sm text-gray-500 font-medium">Licencia</p>
            <h3 className="text-sm font-bold text-gray-900">Activa</h3>
          </div>
        </div>

      </div>

      <div className="bg-gradient-to-r from-purple-600 to-blue-600 rounded-2xl p-8 text-white shadow-lg">
        <h2 className="text-xl font-bold mb-2">NexFlow Automations</h2>
        <p className="opacity-90 max-w-2xl">
          Tu asistente de inteligencia artificial está conectado y listo para atender a tus clientes por WhatsApp 24/7. 
          Recuerda mantener actualizados tus horarios, sedes y servicios para que las respuestas sean siempre precisas.
        </p>
      </div>
    </div>
  );
};