import test from 'node:test';
import assert from 'node:assert/strict';
import { nextSettingsTab, permittedSettingsTabs, SETTINGS_SECTIONS } from '../src/features/business/utils/settingsTabs.ts';

test('secciones mantienen Perfil/Sedes/Horarios/WhatsApp y sus permisos READ reales', () => {
  assert.deepEqual(SETTINGS_SECTIONS.map(tab => [tab.id, tab.label, tab.module]), [
    ['profile', 'Perfil', 'BUSINESS_PROFILE'], ['locations', 'Sedes', 'LOCATIONS'], ['hours', 'Horarios', 'BUSINESS_HOURS'], ['whatsapp', 'WhatsApp', 'CONVERSATIONS'],
  ]);
});
test('cada combinación de permisos conserva solo pestañas autorizadas y el orden inicial', () => {
  for (let mask = 0; mask < 16; mask++) {
    const expected = SETTINGS_SECTIONS.filter((_, index) => mask & (1 << index));
    const permitted = permittedSettingsTabs(module => expected.some(section => section.module === module));
    assert.deepEqual(permitted, expected);
  }
});
test('flechas recorren y envuelven solo pestañas presentes, sin activación ni llamadas API', () => {
  assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'profile', 'ArrowRight'), 'locations');
  assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'profile', 'ArrowLeft'), 'whatsapp');
  assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'whatsapp', 'ArrowRight'), 'profile');
  const limited = permittedSettingsTabs(module => module === 'LOCATIONS' || module === 'CONVERSATIONS');
  assert.equal(nextSettingsTab(limited, 'locations', 'ArrowRight'), 'whatsapp');
  assert.equal(nextSettingsTab(limited, 'whatsapp', 'ArrowLeft'), 'locations');
});
test('Home/End van a los extremos autorizados; Enter/Espacio se delegan al botón nativo', () => {
  assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'hours', 'Home'), 'profile');
  assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'locations', 'End'), 'whatsapp');
  for (const key of ['Enter', ' ', 'Tab', 'ArrowUp', 'Escape']) assert.equal(nextSettingsTab(SETTINGS_SECTIONS, 'profile', key), undefined);
});
test('pestaña única y ausencia de acceso no crean focos o controles inexistentes', () => {
  const single = permittedSettingsTabs(module => module === 'BUSINESS_HOURS');
  for (const key of ['ArrowLeft', 'ArrowRight', 'Home', 'End']) {
    assert.equal(nextSettingsTab(single, 'hours', key), 'hours');
    assert.equal(nextSettingsTab([], 'hours', key), undefined);
  }
});
