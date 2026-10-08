import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';

const frontend = new URL('../', import.meta.url);
const json = file => JSON.parse(readFileSync(new URL(file, frontend), 'utf8').replace(/^\uFEFF/, ''));

test('both Hosting configurations publish dist only to the NexFlow target', () => {
  for (const file of ['firebase.json', 'firebase.nexflow.json']) {
    const { hosting } = json(file);
    assert.equal(hosting.public, 'dist');
    assert.equal(hosting.target, 'nexflow');
    assert.deepEqual(hosting.rewrites, [{ source: '**', destination: '/index.html' }]);
  }
  const rc = json('.firebaserc');
  assert.deepEqual(rc.targets[rc.projects.default].hosting.nexflow, ['nex-flow']);
});

test('Vite output contains the NexFlow application and no default Firebase page', () => {
  assert.equal(existsSync(new URL('public/index.html', frontend)), false);
  const html = readFileSync(new URL('dist/index.html', frontend), 'utf8');
  assert.match(html, /<title>NexFlow<\/title>/);
  assert.match(html, /id="root"/);
  assert.match(html, /src="\/assets\/[^"\s]+\.js"/);
  assert.doesNotMatch(html, /Welcome to Firebase|Hosting Setup Complete|firebase-app-compat|\/src\/main\.tsx/);
});
