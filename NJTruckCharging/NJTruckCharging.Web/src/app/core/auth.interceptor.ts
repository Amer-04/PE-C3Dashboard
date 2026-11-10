import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap, tap } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Sends API calls to the API host with the signed-in user's access token. A 401 (missing/expired/invalid token)
 * returns the user to the sign-in screen; a 403 "not_approved" shows the not-approved screen.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  if (!req.url.startsWith('/api')) return next(req);
  return from(auth.accessToken()).pipe(
    switchMap(token => next(req.clone({
      url: auth.apiUrl(req.url),
      setHeaders: token ? { Authorization: `Bearer ${token}` } : {},
    }))),
    tap({
      error: (e: unknown) => {
        if (!(e instanceof HttpErrorResponse) || !auth.enabled) return;
        if (e.status === 401) auth.sessionRejected();
        else if (e.status === 403 && e.error?.code === 'not_approved') auth.notApproved();
      },
    }),
  );
};
