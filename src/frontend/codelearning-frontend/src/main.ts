import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';

// Monaco's worker setup lives next to the editor (shared/components/code-editor/monaco-loader.ts),
// so it loads only with the pages that show an editor

bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
