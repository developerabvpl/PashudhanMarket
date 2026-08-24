import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { ToastService } from '@upbazaar/ui';
import { catchError, throwError } from 'rxjs';
import { toApiProblem } from './api-problem';

/**
 * Turns any failed API call into a single toast, then rethrows so the caller can still react.
 *
 * Two cases are deliberately silent:
 * - **Validation (400 with field errors)** belongs under the offending input, not in a toast.
 * - **401** is the interceptor in libs/auth's business; it redirects, and a toast on the way
 *   out is just noise.
 */
export const httpErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      const problem = toApiProblem(error);

      if (!problem.isValidation && problem.status !== 401) {
        toast.error(problem.title, problem.code);
      }

      return throwError(() => error);
    })
  );
};
