// highlight.js ships no typings for individual grammar modules
declare module 'highlight.js/lib/languages/*' {
  import type { LanguageFn } from 'highlight.js';
  const language: LanguageFn;
  export default language;
}

// The editor core without the bundled language services; its API is the same as the package's
declare module 'monaco-editor/esm/vs/editor/edcore.main.js' {
  export * from 'monaco-editor';
}

declare module 'monaco-editor/esm/vs/editor/editor.worker.js';
