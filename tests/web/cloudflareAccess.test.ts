import { describe, expect, test } from 'bun:test';
import {
  createLocalJWKSet,
  exportJWK,
  generateKeyPair,
  SignJWT,
} from 'jose';
import {
  CloudflareAccessVerifier,
  normalizeTeamDomain,
} from '../../src/server/cloudflareAccess';

const teamDomain = 'test-team.cloudflareaccess.com';
const audience = 'expected-audience';

describe('Cloudflare Access verification', () => {
  test('accepts only a correctly signed token for the configured application', async () => {
    const { privateKey, publicKey } = await generateKeyPair('RS256');
    const publicJwk = await exportJWK(publicKey);
    Object.assign(publicJwk, { kid: 'test-key', alg: 'RS256', use: 'sig' });
    const resolver = createLocalJWKSet({ keys: [publicJwk] });
    const verifier = new CloudflareAccessVerifier(async () => resolver);
    const valid = await createToken(privateKey, audience);
    const wrongAudience = await createToken(privateKey, 'another-app');

    expect(await verifier.verify(valid, config())).toBe(true);
    expect(await verifier.verify(wrongAudience, config())).toBe(false);
    expect(await verifier.verify('not-a-jwt', config())).toBe(false);
  });

  test('rejects disabled or malformed tenant configuration before resolving keys', async () => {
    let resolverCalls = 0;
    const verifier = new CloudflareAccessVerifier(async () => {
      resolverCalls++;
      throw new Error('must not fetch');
    });

    expect(await verifier.verify('token', {
      ...config(),
      enabled: false,
    })).toBe(false);
    expect(await verifier.verify('token', {
      ...config(),
      teamDomain: 'https://attacker.example/path',
    })).toBe(false);
    expect(resolverCalls).toBe(0);
  });

  test('retries resolver creation after a transient initialization failure', async () => {
    const { privateKey, publicKey } = await generateKeyPair('RS256');
    const publicJwk = await exportJWK(publicKey);
    Object.assign(publicJwk, { kid: 'test-key', alg: 'RS256', use: 'sig' });
    const resolver = createLocalJWKSet({ keys: [publicJwk] });
    let resolverCalls = 0;
    const verifier = new CloudflareAccessVerifier(async () => {
      resolverCalls++;
      if (resolverCalls === 1) throw new Error('temporary failure');
      return resolver;
    });
    const token = await createToken(privateKey, audience);

    expect(await verifier.verify(token, config())).toBe(false);
    expect(await verifier.verify(token, config())).toBe(true);
    expect(resolverCalls).toBe(2);
  });

  test('normalizes a valid team domain and rejects SSRF-shaped values', () => {
    expect(normalizeTeamDomain(' Test-Team.CloudflareAccess.com '))
      .toBe(teamDomain);
    expect(normalizeTeamDomain('localhost')).toBeNull();
    expect(normalizeTeamDomain('team.cloudflareaccess.com.attacker.example'))
      .toBeNull();
    expect(normalizeTeamDomain('team.cloudflareaccess.com@127.0.0.1'))
      .toBeNull();
  });
});

function config() {
  return {
    enabled: true,
    teamDomain,
    audience,
  };
}

async function createToken(
  privateKey: CryptoKey,
  tokenAudience: string,
): Promise<string> {
  return new SignJWT({ email: 'person@example.com' })
    .setProtectedHeader({ alg: 'RS256', kid: 'test-key' })
    .setIssuer(`https://${teamDomain}`)
    .setAudience(tokenAudience)
    .setIssuedAt()
    .setExpirationTime('5m')
    .sign(privateKey);
}
