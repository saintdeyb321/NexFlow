export const SETTINGS_SECTIONS = [
  { id: 'profile', label: 'Perfil', module: 'BUSINESS_PROFILE' },
  { id: 'locations', label: 'Sedes', module: 'LOCATIONS' },
  { id: 'hours', label: 'Horarios', module: 'BUSINESS_HOURS' },
  { id: 'whatsapp', label: 'WhatsApp', module: 'CONVERSATIONS' },
] as const;
export type SettingsTabId = typeof SETTINGS_SECTIONS[number]['id'];
export const permittedSettingsTabs = (read: (module: string) => boolean) => SETTINGS_SECTIONS.filter(section => read(section.module));
export const nextSettingsTab = (tabs: readonly { id: SettingsTabId }[], current: SettingsTabId, key: string): SettingsTabId | undefined => {
  if (!tabs.length) return;
  const index = Math.max(0, tabs.findIndex(tab => tab.id === current));
  if (key === 'Home') return tabs[0].id;
  if (key === 'End') return tabs[tabs.length - 1].id;
  if (key === 'ArrowRight') return tabs[(index + 1) % tabs.length].id;
  if (key === 'ArrowLeft') return tabs[(index + tabs.length - 1) % tabs.length].id;
};
