import test from 'node:test';
import assert from 'node:assert/strict';
import { canPairWhatsApp, whatsappPollInterval, whatsappStatus } from '../src/features/business/whatsAppState.ts';
import type { WhatsAppStatusResponse } from '../src/features/business/types/business.types.ts';

const now = Date.parse('2026-10-08T00:00:00Z');
const unlinked: WhatsAppStatusResponse = { status: 'DISCONNECTED', isLinked: false, canConnect: true, requiresLogout: false, qrBase64: null, qrExpiresAt: null, message: null };
const qr: WhatsAppStatusResponse = { ...unlinked, status: 'QR_AVAILABLE', qrBase64: 'test-base64', qrExpiresAt: new Date(now + 30_000).toISOString() };

test('expired QR is clearly expired', () => assert.equal(whatsappStatus(qr, false, now + 30_000), 'QR_EXPIRED'));
test('successful link takes precedence over an expired QR deadline', () => assert.equal(whatsappStatus({ ...qr, status: 'CONNECTED', isLinked: true }, false, now + 30_000), 'CONNECTED'));
test('provider errors are visible even when cached data was connected', () => assert.equal(whatsappStatus({ ...unlinked, status: 'CONNECTED', isLinked: true }, true, now), 'UNAVAILABLE'));
test('connected sessions never poll', () => assert.equal(whatsappPollInterval({ ...unlinked, status: 'CONNECTED' }, true, now, now + 60_000), false));
test('QR polling is limited to its original deadline', () => {
  assert.equal(whatsappPollInterval(qr, true, now, 0), 5000);
  assert.equal(whatsappPollInterval(qr, true, now + 30_000, 0), false);
});
test('hidden pages do not poll', () => assert.equal(whatsappPollInterval(qr, false, now, now + 60_000), false));
test('reconnection polling requires a finite user action window', () => {
  const reconnecting = { ...unlinked, status: 'RECONNECTING' as const, isLinked: true };
  assert.equal(whatsappPollInterval(reconnecting, true, now, 0), false);
  assert.equal(whatsappPollInterval(reconnecting, true, now, now + 60_000), 5000);
  assert.equal(whatsappPollInterval(reconnecting, true, now + 60_000, now + 60_000), false);
});
test('a linked session cannot pair even if canConnect was stale', () => assert.equal(canPairWhatsApp({ ...unlinked, isLinked: true }, true, false, 'RECONNECTING'), false));
test('pending logout cannot pair', () => assert.equal(canPairWhatsApp({ ...unlinked, requiresLogout: true }, true, false, 'DISCONNECT_PENDING'), false));
test('double click and missing configuration permissions prevent pairing', () => {
  assert.equal(canPairWhatsApp(unlinked, true, true, 'DISCONNECTED'), false);
  assert.equal(canPairWhatsApp(unlinked, false, false, 'DISCONNECTED'), false);
  assert.equal(canPairWhatsApp(unlinked, true, false, 'DISCONNECTED'), true);
});
test('provider unavailability cannot offer a new QR', () => assert.equal(canPairWhatsApp(unlinked, true, false, 'UNAVAILABLE'), false));
