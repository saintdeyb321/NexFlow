// Node's existing test runner + installed TypeScript, without an additional JSX test dependency.
import { registerHooks } from 'node:module';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

registerHooks({
  resolve(specifier, context, nextResolve) {
    if (specifier.endsWith('/core/api/axiosClient') || specifier.endsWith('/api/axiosClient'))
      return { url: new URL('./fixtures/reservations-api.ts', import.meta.url).href, shortCircuit: true };
    if (specifier.endsWith('/store/useAuthStore'))
      return { url: new URL('./fixtures/reservations-store.ts', import.meta.url).href, shortCircuit: true };
    if (specifier.startsWith('.') && context.parentURL) {
      const url = new URL(specifier, context.parentURL);
      for (const extension of ['', '.ts', '.tsx']) if (existsSync(fileURLToPath(url) + extension))
        return { url: url.href + extension, shortCircuit: true };
    }
    return nextResolve(specifier, context);
  },
  load(url, context, nextLoad) {
    if (/\.(ts|tsx)$/.test(url) && !url.includes('/node_modules/')) return {
      format: 'module', shortCircuit: true,
      source: ts.transpileModule(readFileSync(new URL(url), 'utf8'), { compilerOptions: { module: ts.ModuleKind.ESNext, target: ts.ScriptTarget.ES2023, jsx: ts.JsxEmit.ReactJSX } }).outputText,
    };
    return nextLoad(url, context);
  },
});
