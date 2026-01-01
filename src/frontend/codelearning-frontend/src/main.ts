import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

(window as any).MonacoEnvironment = {
  getWorkerUrl: (moduleId: string, label: string) => {
    if (label === 'json') {
      return './assets/monaco/esm/vs/language/json/json.worker.js';
    }
    if (label === 'css' || label === 'scss' || label === 'less') {
      return './assets/monaco/esm/vs/language/css/css.worker.js';
    }
    if (label === 'html' || label === 'handlebars' || label === 'razor') {
      return './assets/monaco/esm/vs/language/html/html.worker.js';
    }
    if (label === 'typescript' || label === 'javascript') {
      return './assets/monaco/esm/vs/language/typescript/ts.worker.js';
    }

    if (label === 'python') {
      return './assets/monaco/esm/vs/basic-languages/python/python.js';
    }
    if (label === 'csharp') {
      return './assets/monaco/esm/vs/basic-languages/csharp/csharp.js';
    }
    if (label === 'java') {
      return './assets/monaco/esm/vs/basic-languages/java/java.js';
    }
    return './assets/monaco/esm/vs/editor/editor.worker.js';
  },
};

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
