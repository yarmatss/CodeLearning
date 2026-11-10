/// <reference lib="webworker" />

// Monaco's generic editor worker (diffing, word ranges, link detection). Bundled by the Angular
// builder as its own module worker; the languages the editor offers need no other worker.
import 'monaco-editor/esm/vs/editor/editor.worker.js';
