import type * as Monaco from 'monaco-editor';

export type MonacoApi = typeof Monaco;

/** Languages the code editor highlights; each maps to a Monaco language id. */
export type EditorLanguage = 'python' | 'javascript' | 'csharp' | 'java';

// Built by angular.json as a separate, non-injected stylesheet so its ~300 kB stay out of the
// initial bundle; added to the page the first time an editor is shown
const MONACO_STYLESHEET = 'monaco-editor.css';

let monacoPromise: Promise<MonacoApi> | null = null;

/**
 * Loads Monaco on first use. Only the editor core and the tokenizers of the four course
 * languages are pulled in: the full `monaco-editor` entry would also bundle the TypeScript,
 * CSS, HTML and JSON language services and their workers, none of which the app uses.
 */
export function loadMonaco(): Promise<MonacoApi> {
  monacoPromise ??= (async () => {
    installMonacoEnvironment();
    ensureStylesheet();

    const [monaco] = await Promise.all([
      import('monaco-editor/esm/vs/editor/edcore.main.js'),
      import('monaco-editor/esm/vs/basic-languages/python/python.contribution.js'),
      import('monaco-editor/esm/vs/basic-languages/javascript/javascript.contribution.js'),
      import('monaco-editor/esm/vs/basic-languages/csharp/csharp.contribution.js'),
      import('monaco-editor/esm/vs/basic-languages/java/java.contribution.js')
    ]);
    return monaco as unknown as MonacoApi;
  })();

  // A failed load (e.g. a dropped chunk request) may be retried by the next editor
  monacoPromise.catch(() => (monacoPromise = null));
  return monacoPromise;
}

function installMonacoEnvironment(): void {
  const scope = globalThis as typeof globalThis & { MonacoEnvironment?: Monaco.Environment };
  if (scope.MonacoEnvironment) {
    return;
  }

  scope.MonacoEnvironment = {
    // Monaco's workers are ES modules, so they must be started as module workers. With only
    // tokenizer-based languages loaded, the generic editor worker serves every label.
    getWorker: () => new Worker(new URL('./editor.worker', import.meta.url), { type: 'module', name: 'monaco-editor' })
  };
}

function ensureStylesheet(): void {
  if (typeof document === 'undefined' || document.querySelector('link[data-monaco-editor]')) {
    return;
  }

  const link = document.createElement('link');
  link.rel = 'stylesheet';
  link.href = MONACO_STYLESHEET;
  link.dataset['monacoEditor'] = '';
  document.head.appendChild(link);
}
