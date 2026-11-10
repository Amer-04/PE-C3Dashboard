import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { Root } from './app/root';
import { RUNTIME_CONFIG, loadRuntimeConfig } from './app/core/runtime-config';

loadRuntimeConfig()
  .then(config => bootstrapApplication(Root, { ...appConfig, providers: [...appConfig.providers, { provide: RUNTIME_CONFIG, useValue: config }] }))
  .catch((err) => console.error(err));
