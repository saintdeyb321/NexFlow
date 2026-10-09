import { useId, useState } from 'react';
import { Building2 } from 'lucide-react';
import { PageHeader } from '../../../components/ui/Layout';
import { EmptyState } from '../../../components/ui/Feedback';
import { useAuthStore } from '../../../core/store/useAuthStore';
import { usePermissions } from '../../../core/auth/permissions';
import { getQuerySession } from '../../../core/query/queryPersistence';
import { ProfileTab } from '../components/ProfileTab';
import { LocationsTab } from '../components/LocationsTab';
import { HoursTab } from '../components/HoursTab';
import { WhatsAppTab } from '../components/WhatsAppTab';
import { SettingsTabs } from '../components/SettingsTabs';
import { permittedSettingsTabs } from '../utils/settingsTabs';
import type { SettingsTabId } from '../utils/settingsTabs';

export const SettingsPage = () => {
  const me = useAuthStore(state => state.me);
  return <SettingsContent key={JSON.stringify([me?.user.id, me?.workspace?.id, getQuerySession()])} />;
};

const SettingsContent = () => {
  const workspaceId = useAuthStore(state => state.me?.workspace?.id);
  const { hasModule, can } = usePermissions();
  const tabs = permittedSettingsTabs(module => can(module, 'READ') && (module !== 'CONVERSATIONS' || hasModule(module)));
  const [selected, setSelected] = useState<SettingsTabId>('profile');
  const active = tabs.some(tab => tab.id === selected) ? selected : tabs[0]?.id;
  const id = useId();
  if (!workspaceId) return <EmptyState title="Sin negocio asignado" description="Tu cuenta aún no tiene un workspace disponible." />;
  if (!active) return <EmptyState title="Sin acceso a configuración" description="Tu cuenta no tiene permisos de lectura para las secciones de este workspace." />;

  return <div className="nf-page nf-settings max-w-5xl space-y-6">
    <PageHeader title="Configuración del negocio" description="Administra la identidad, las sedes, los horarios y la conexión a WhatsApp de tu negocio." icon={<Building2 aria-hidden="true" className="w-5 h-5" />} />
    <SettingsTabs tabs={tabs} active={active} onChange={setSelected} id={id} />
    <div id={`${id}-panel`} role="tabpanel" aria-labelledby={`${id}-${active}`} tabIndex={0} className="min-w-0 pb-6">
      {active === 'profile' && <ProfileTab />}
      {active === 'locations' && <LocationsTab />}
      {active === 'hours' && <HoursTab />}
      {active === 'whatsapp' && <WhatsAppTab />}
    </div>
  </div>;
};
