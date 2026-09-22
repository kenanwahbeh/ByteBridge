import { describe, expect, test } from 'bun:test';
import {
  getSingleHeader,
  isCloudflareTunnelRequest,
  isDirectLoopbackRequest,
  isLoopbackAddress,
} from '../../src/server/requestSecurity';

describe('control panel request boundary', () => {
  test.each([
    '127.0.0.1',
    '127.23.45.67',
    '::1',
    '::ffff:127.0.0.1',
  ])('accepts loopback address %s', (address) => {
    expect(isLoopbackAddress(address)).toBe(true);
  });

  test.each([
    '0.0.0.0',
    '10.0.0.5',
    '192.168.1.20',
    '203.0.113.9',
    '::ffff:192.168.1.20',
    '',
  ])('rejects non-loopback address %s', (address) => {
    expect(isLoopbackAddress(address)).toBe(false);
  });

  test('allows a direct local control panel request', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      undefined,
      '127.0.0.1:3000',
    )).toBe(true);
  });

  test('allows the local control panel same-origin browser request', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      'http://127.0.0.1:3000',
      '127.0.0.1:3000',
    )).toBe(true);
  });

  test('allows the localhost control panel same-origin browser request', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      'http://localhost:3000',
      'localhost:3000',
    )).toBe(true);
  });

  test('still rejects a localhost origin when its port does not match', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      'http://localhost:4000',
      'localhost:3000',
    )).toBe(false);
  });

  test('rejects a Cloudflare tunnel hop even though its socket is local', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      '203.0.113.9',
      undefined,
      undefined,
      '127.0.0.1:3000',
    )).toBe(false);
  });

  test('rejects another reverse proxy hop even though its socket is local', () => {
    expect(isDirectLoopbackRequest(
      '::1',
      undefined,
      '203.0.113.9, 127.0.0.1',
      undefined,
      '[::1]:3000',
    )).toBe(false);
  });

  test('rejects a malicious website targeting the local control API', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      'https://attacker.example',
      '127.0.0.1:3000',
    )).toBe(false);
  });

  test('rejects opaque and malformed browser origins', () => {
    expect(isDirectLoopbackRequest(
      '127.0.0.1',
      undefined,
      undefined,
      'null',
      '127.0.0.1:3000',
    )).toBe(false);
  });

  test('recognizes a same-origin HTTPS Cloudflare tunnel hop', () => {
    expect(isCloudflareTunnelRequest(
      '127.0.0.1',
      '203.0.113.9',
      'https://panel.example.com',
      'panel.example.com',
    )).toBe(true);
  });

  test('rejects tunnel hops with a cross-site or insecure origin', () => {
    expect(isCloudflareTunnelRequest(
      '127.0.0.1',
      '203.0.113.9',
      'https://attacker.example',
      'panel.example.com',
    )).toBe(false);
    expect(isCloudflareTunnelRequest(
      '127.0.0.1',
      '203.0.113.9',
      'http://panel.example.com',
      'panel.example.com',
    )).toBe(false);
  });

  test('rejects ambiguous assertion headers', () => {
    expect(getSingleHeader(['token-one', 'token-two'])).toBeNull();
    expect(getSingleHeader('   ')).toBeNull();
    expect(getSingleHeader('signed-token')).toBe('signed-token');
  });
});
