import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { methodologyVersionInterceptor } from './core/methodology-store';
import { authInterceptor } from './core/auth.interceptor';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([methodologyVersionInterceptor, authInterceptor])),
    provideRouter(routes, withComponentInputBinding()),
  ],
};
