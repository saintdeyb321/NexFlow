import { PageHeader } from '../../../components/ui/Layout';
import { EmptyState } from '../../../components/ui/Feedback';
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

  if (!workspaceId) return <EmptyState title="Sin negocio asignado" description="Tu cuenta aún no tiene un workspace disponible." />;
  if (!hasProfile && !hasLocations && !hasHours && !hasWhatsApp) return <EmptyState title="Sin acceso a configuración" description="Tu cuenta no tiene permisos de lectura para las secciones de este workspace." />;

  return (
    <div className="nf-page max-w-5xl">
<PageHeader title="Configuración del negocio" description="Identidad, sedes, horarios y conexión a WhatsApp." icon={<Building2 aria-hidden="true" className="w-5 h-5" />} />

      <div role="tablist" aria-label="Configuración del negocio" className="nf-tabs mb-6">
        {hasProfile && (
          <Button variant="ghost" role="tab" id="settings-profile" aria-controls="settings-panel" aria-selected={activeTab === 'profile'} onClick={() => setActiveTab('profile')} className="nf-tab">
            <div className="flex items-center"><Building2 aria-hidden="true" className="w-4 h-4 mr-2"/> Perfil</div>
          </Button>
        )}

        {hasLocations && (
          <Button variant="ghost" role="tab" id="settings-locations" aria-controls="settings-panel" aria-selected={activeTab === 'locations'} onClick={() => setActiveTab('locations')} className="nf-tab">
            <div className="flex items-center"><MapPin aria-hidden="true" className="w-4 h-4 mr-2"/> Sedes</div>
          </Button>
        )}

        {hasHours && (
          <Button variant="ghost" role="tab" id="settings-hours" aria-controls="settings-panel" aria-selected={activeTab === 'hours'} onClick={() => setActiveTab('hours')} className="nf-tab">
            <div className="flex items-center"><Clock aria-hidden="true" className="w-4 h-4 mr-2"/> Horarios</div>
          </Button>
        )}

        {hasWhatsApp && <Button variant="ghost" role="tab" id="settings-whatsapp" aria-controls="settings-panel" aria-selected={activeTab === 'whatsapp'} onClick={() => setActiveTab('whatsapp')} className="nf-tab">
          <div className="flex items-center"><MessageCircle aria-hidden="true" className="w-4 h-4 mr-2"/> WhatsApp</div>
        </Button>}
      </div>

      <div id="settings-panel" role="tabpanel" aria-labelledby={`settings-${activeTab}`} className="mt-2 pb-6">
        {activeTab === 'profile' && hasProfile && <ProfileTab />}
        {activeTab === 'locations' && hasLocations && <LocationsTab />}
        {activeTab === 'hours' && hasHours && <HoursTab />}
        {activeTab === 'whatsapp' && hasWhatsApp && <WhatsAppTab />}
      </div>
    </div>
  );
};
