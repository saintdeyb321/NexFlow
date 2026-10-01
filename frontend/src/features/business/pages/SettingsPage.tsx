import { Button } from '../../../components/ui/Button';
import { useState, useEffect } from 'react';
import { Building2, MapPin, Clock, MessageCircle } from 'lucide-react';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { ProfileTab } from '../components/ProfileTab';
import { LocationsTab } from '../components/LocationsTab';
import { HoursTab } from '../components/HoursTab';
import { WhatsAppTab } from '../components/WhatsAppTab';
import { usePermissions } from '../../../core/auth/permissions';

export const SettingsPage = () => {
  const { me } = useAuthStore();
  const workspaceId = me?.workspace?.id;
  const { hasModule, can } = usePermissions();
  const hasLocations = can('LOCATIONS', 'READ');
  const hasHours = can('BUSINESS_HOURS', 'READ');
  const hasProfile = can('BUSINESS_PROFILE', 'READ');
  const hasWhatsApp = hasModule('CONVERSATIONS') && can('CONVERSATIONS', 'READ');

  // Determinamos la pestaña por defecto
  const defaultTab = hasProfile ? 'profile' : hasLocations ? 'locations' : hasHours ? 'hours' : 'whatsapp';

  const [activeTab, setActiveTab] = useState<'profile' | 'locations' | 'hours' | 'whatsapp'>(defaultTab);

  useEffect(() => {
    // Si la pestaña actual deja de ser válida por un cambio de sesión, saltar a la por defecto
    if (activeTab === 'locations' && !hasLocations) setActiveTab(defaultTab);
    if (activeTab === 'hours' && !hasHours) setActiveTab(defaultTab);
    if (activeTab === 'profile' && !hasProfile) setActiveTab(defaultTab);
    if (activeTab === 'whatsapp' && !hasWhatsApp) setActiveTab(defaultTab);
  }, [activeTab, defaultTab, hasLocations, hasHours, hasProfile, hasWhatsApp]);

  if (!workspaceId) return <div className="text-center p-8 text-gray-500">Sin negocio asignado</div>;

  return (
    <div className="max-w-4xl mx-auto animate-in fade-in slide-in-from-bottom-2">
      <div className="mb-6">
        <h1 className="text-2xl font-bold text-gray-900 flex items-center">Configuración del Negocio</h1>
        <p className="mt-1 text-sm text-gray-500">Administra la identidad, ubicaciones, horarios de atención y tu conexión a WhatsApp.</p>
      </div>

      <div className="flex border-b border-gray-200 mb-6 overflow-x-auto custom-scrollbar">
        {hasProfile && (
          <Button variant="ghost" onClick={() => setActiveTab('profile')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'profile' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
            <div className="flex items-center"><Building2 className="w-4 h-4 mr-2"/> Perfil</div>
          </Button>
        )}

        {hasLocations && (
          <Button variant="ghost" onClick={() => setActiveTab('locations')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'locations' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
            <div className="flex items-center"><MapPin className="w-4 h-4 mr-2"/> Sedes</div>
          </Button>
        )}

        {hasHours && (
          <Button variant="ghost" onClick={() => setActiveTab('hours')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'hours' ? 'border-blue-600 text-blue-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
            <div className="flex items-center"><Clock className="w-4 h-4 mr-2"/> Horarios</div>
          </Button>
        )}

        {hasWhatsApp && <Button variant="ghost" onClick={() => setActiveTab('whatsapp')} className={`whitespace-nowrap px-4 py-2 border-b-2 font-medium text-sm transition-colors ${activeTab === 'whatsapp' ? 'border-green-600 text-green-600' : 'border-transparent text-gray-500 hover:text-gray-700'}`}>
          <div className="flex items-center"><MessageCircle className="w-4 h-4 mr-2"/> WhatsApp</div>
        </Button>}
      </div>

      <div className="mt-2 pb-12">
        {activeTab === 'profile' && hasProfile && <ProfileTab />}
        {activeTab === 'locations' && hasLocations && <LocationsTab />}
        {activeTab === 'hours' && hasHours && <HoursTab />}
        {activeTab === 'whatsapp' && hasWhatsApp && <WhatsAppTab />}
      </div>
    </div>
  );
};
