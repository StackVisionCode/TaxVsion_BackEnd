import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, expect, it } from 'vitest';
import { Client } from 'minio';

// Los overrides de package.json no pueden sacar a minio del rango de sus dependencias directas:
// stream-json 3 rompe minio en Linux ('jsonl/Parser.js' pasó a 'parser.js').
function installedVersion(name: string): string {
  const nested = join('node_modules', 'minio', 'node_modules', name, 'package.json');
  const hoisted = join('node_modules', name, 'package.json');
  const path = existsSync(nested) ? nested : hoisted;
  return (JSON.parse(readFileSync(path, 'utf8')) as { version: string }).version;
}

function satisfiesCaret(version: string, range: string): boolean {
  const [maj = 0, min = 0, pat = 0] = version.split('.').map(Number);
  const [rMaj = 0, rMin = 0, rPat = 0] = range.slice(1).split('.').map(Number);
  if (maj !== rMaj) return false;
  return min > rMin || (min === rMin && pat >= rPat);
}

describe('minio dependencies', () => {
  it('keeps every direct dependency of minio inside its declared range', () => {
    const pkg = JSON.parse(readFileSync(join('node_modules', 'minio', 'package.json'), 'utf8')) as {
      dependencies: Record<string, string>;
    };
    const outOfRange = Object.entries(pkg.dependencies)
      .filter(([, range]) => range.startsWith('^'))
      .filter(([name, range]) => !satisfiesCaret(installedVersion(name), range))
      .map(([name, range]) => `${name}@${installedVersion(name)} (minio pide ${range})`);

    expect(outOfRange).toEqual([]);
  });

  it('loads and signs a URL with query parameters without touching the network', async () => {
    const client = new Client({
      endPoint: 'localhost',
      port: 9000,
      useSSL: false,
      accessKey: 'test-access-key',
      secretKey: 'test-secret-key',
      region: 'us-east-1',
    });

    const url = await client.presignedGetObject('bucket', 'a b.pdf', 60, {
      'response-content-type': 'application/pdf',
    });

    expect(url).toContain('response-content-type=application%2Fpdf');
    expect(url).toContain('X-Amz-Signature=');
  });
});
