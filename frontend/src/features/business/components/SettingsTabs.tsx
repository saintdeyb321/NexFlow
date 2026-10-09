import { useLayoutEffect, useRef, useState } from 'react';
import { Building2, Clock, MapPin, MessageCircle } from 'lucide-react';
import { Button } from '../../../components/ui/Button';
import { nextSettingsTab } from '../utils/settingsTabs';
import type { SETTINGS_SECTIONS, SettingsTabId } from '../utils/settingsTabs';

const icons = { profile: Building2, locations: MapPin, hours: Clock, whatsapp: MessageCircle };
export const SettingsTabs = ({ tabs, active, onChange, id }: {
  tabs: readonly typeof SETTINGS_SECTIONS[number][]; active: SettingsTabId; onChange: (tab: SettingsTabId) => void; id: string;
}) => {
  const [focused, setFocused] = useState(active);
  const tablist = useRef<HTMLDivElement>(null);
  const hadFocus = useRef(false);
  const focusedExists = tabs.some(tab => tab.id === focused);
  const focusTarget = focusedExists ? focused : active;
  useLayoutEffect(() => {
    // Restore focus only if a focused tab was removed by a permission change.
    if (!focusedExists && hadFocus.current) tablist.current?.querySelector<HTMLButtonElement>('[tabindex="0"]')?.focus();
  }, [focusedExists, active]);
  return <div>
    <div ref={tablist} role="tablist" aria-label="Configuración del negocio" aria-orientation="horizontal" aria-describedby={`${id}-help`}
      className="nf-settings-tabs" onFocusCapture={() => { hadFocus.current = true; }}
      onBlurCapture={event => { if (!event.currentTarget.contains(event.relatedTarget)) hadFocus.current = false; }}>
      {tabs.map(tab => {
        const Icon = icons[tab.id];
        return <Button key={tab.id} variant="ghost" role="tab" id={`${id}-${tab.id}`} aria-controls={`${id}-panel`}
          aria-selected={active === tab.id} tabIndex={focusTarget === tab.id ? 0 : -1}
          onFocus={() => setFocused(tab.id)} onClick={() => { setFocused(tab.id); onChange(tab.id); }}
          onKeyDown={event => {
            // Manual activation: arrows/Home/End move focus; native Enter/Space activate.
            const next = nextSettingsTab(tabs, tab.id, event.key); if (!next) return;
            event.preventDefault(); setFocused(next);
            document.getElementById(`${id}-${next}`)?.focus();
          }}><Icon aria-hidden="true" className="h-4 w-4 shrink-0" />{tab.label}</Button>;
      })}
    </div>
    <p id={`${id}-help`} className="text-xs text-muted mt-2">Elige una sección. Con teclado: flechas para recorrer; Enter o Espacio para abrir.</p>
  </div>;
};
