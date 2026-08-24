import { HttpErrorResponse } from '@angular/common/http';

/**
 * An API failure reduced to what the UI needs: one line to show, a stable code to branch on,
 * and per-field messages to attach to form controls.
 */
export interface ApiProblem {
  readonly status: number;
  /** Stable machine-readable code, e.g. `catalog.stock.insufficient`. */
  readonly code: string;
  /** Human-readable summary from the API, already suitable for a toast. */
  readonly title: string;
  /** Field name (camelCased to match the form model) to messages. */
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>;
  /** True when the API rejected the input rather than failing to do the work. */
  readonly isValidation: boolean;
}

interface ProblemDetailsBody {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/**
 * Normalizes any HTTP failure into an ApiProblem, including the ones that never reached the
 * API: a network drop has status 0 and no body, and the UI still needs something to show.
 */
export function toApiProblem(error: unknown): ApiProblem {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      status: 0,
      code: 'client.unexpected',
      title: 'errors.unexpected',
      fieldErrors: {},
      isValidation: false,
    };
  }

  if (error.status === 0) {
    return {
      status: 0,
      code: 'client.offline',
      title: 'errors.offline',
      fieldErrors: {},
      isValidation: false,
    };
  }

  const body = (error.error ?? {}) as ProblemDetailsBody;

  // The API sends the code both as an extension and inside the type URI; prefer the extension.
  const code = body.code ?? codeFromTypeUri(body.type) ?? `http.${error.status}`;
  const fieldErrors = normalizeFieldErrors(body.errors);

  return {
    status: error.status,
    code,
    title: body.title ?? body.detail ?? defaultTitleFor(error.status),
    fieldErrors,
    isValidation: error.status === 400 && Object.keys(fieldErrors).length > 0,
  };
}

/** Messages for one control, ready to render under the field. */
export function fieldErrorsFor(problem: ApiProblem | null, field: string): readonly string[] {
  return problem?.fieldErrors[field] ?? [];
}

function codeFromTypeUri(type: string | undefined): string | undefined {
  if (!type) {
    return undefined;
  }

  const marker = '/errors/';
  const index = type.indexOf(marker);

  return index === -1 ? undefined : type.slice(index + marker.length);
}

/**
 * ASP.NET reports validation errors keyed by the property name it bound ("Price", or
 * "$.lines[0].quantity" for a JSON body). Forms address controls in camelCase, so the key is
 * reduced to its last segment and lower-cased on the first letter.
 */
function normalizeFieldErrors(
  errors: Record<string, string[]> | undefined
): Record<string, readonly string[]> {
  if (!errors) {
    return {};
  }

  const normalized: Record<string, string[]> = {};

  for (const [key, messages] of Object.entries(errors)) {
    const field = camelCase(key.replace(/^\$\./, '').split('.').pop() ?? key);
    normalized[field] = [...(normalized[field] ?? []), ...messages];
  }

  return normalized;
}

function camelCase(value: string): string {
  return value.length === 0 ? value : value[0].toLowerCase() + value.slice(1);
}

function defaultTitleFor(status: number): string {
  switch (status) {
    case 401:
      return 'errors.unauthorized';
    case 403:
      return 'errors.forbidden';
    case 404:
      return 'errors.notFound';
    case 409:
      return 'errors.conflict';
    default:
      return 'errors.unexpected';
  }
}
