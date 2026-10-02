import { HttpContext, HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { ToastService } from '@upbazaar/ui';
import { catchError, throwError } from 'rxjs';
import { toApiProblem } from './api-problem';

/**
 * Marks a request whose caller shows its own failure, beside the control that caused it. The
 * error reporter then stays quiet, so the buyer is not told the same thing twice - once in words
 * they can read and once in a toast holding the API's English title and code.
 */
export const CALLER_SHOWS_ERRORS = new HttpContextToken<boolean>(() => false);

/** The context to pass as `api.invoke(fn, params, callerShowsErrors())` when a page reports the failure itself. */
export function callerShowsErrors(): HttpContext {
  return new HttpContext().set(CALLER_SHOWS_ERRORS, true);
}

/**
 * Turns any failed API call into a single toast, then rethrows so the caller can still react.
 *
 * Three cases are deliberately silent:
 * - **Validation (400 with field errors)** belongs under the offending input, not in a toast.
 * - **401** is the interceptor in libs/auth's business; it redirects, and a toast on the way
 *   out is just noise.
 * - **A request sent with {@link callerShowsErrors}**: the page shows the failure in place.
 */
export const httpErrorInterceptor: HttpInterceptorFn = (request, next) => {
  const toast = inject(ToastService);

  return next(request).pipe(
    catchError((error: unknown) => {
      const problem = toApiProblem(error);

      if (!problem.isValidation && problem.status !== 401 && !request.context.get(CALLER_SHOWS_ERRORS)) {
        toast.error(problem.title, problem.code);
      }

      return throwError(() => error);
    })
  );
};
