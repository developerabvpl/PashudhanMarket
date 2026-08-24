import { HttpErrorResponse } from '@angular/common/http';
import { fieldErrorsFor, toApiProblem } from './api-problem';

function httpError(status: number, body: unknown): HttpErrorResponse {
  return new HttpErrorResponse({ status, error: body, url: '/api/catalog/products' });
}

describe('toApiProblem', () => {
  it('reads the code, title and status the API sent', () => {
    const problem = toApiProblem(
      httpError(409, {
        type: 'https://upbazaar.dev/errors/catalog.product.duplicate_sku',
        title: 'A product with this SKU already exists.',
        status: 409,
        code: 'catalog.product.duplicate_sku',
      })
    );

    expect(problem.status).toBe(409);
    expect(problem.code).toBe('catalog.product.duplicate_sku');
    expect(problem.title).toBe('A product with this SKU already exists.');
    expect(problem.isValidation).toBe(false);
  });

  it('falls back to the code embedded in the type URI', () => {
    const problem = toApiProblem(
      httpError(404, { type: 'https://upbazaar.dev/errors/catalog.product.not_found' })
    );

    expect(problem.code).toBe('catalog.product.not_found');
  });

  it('maps validation errors onto camelCased field names', () => {
    const problem = toApiProblem(
      httpError(400, {
        title: 'One or more validation errors occurred.',
        errors: {
          Price: ['Price must be greater than zero.'],
          Sku: ['SKU is required.'],
        },
      })
    );

    expect(problem.isValidation).toBe(true);
    expect(fieldErrorsFor(problem, 'price')).toEqual(['Price must be greater than zero.']);
    expect(fieldErrorsFor(problem, 'sku')).toEqual(['SKU is required.']);
  });

  it('reduces a JSON pointer key to the control name', () => {
    const problem = toApiProblem(
      httpError(400, { errors: { '$.lines[0].quantity': ['Must be greater than 0.'] } })
    );

    expect(fieldErrorsFor(problem, 'quantity')).toEqual(['Must be greater than 0.']);
  });

  it('merges messages that arrive under different keys for one field', () => {
    const problem = toApiProblem(
      httpError(400, { errors: { Price: ['Too low.'], '$.price': ['Also wrong.'] } })
    );

    expect(fieldErrorsFor(problem, 'price')).toEqual(['Too low.', 'Also wrong.']);
  });

  it('treats a 400 without field errors as a plain failure, not a validation problem', () => {
    const problem = toApiProblem(httpError(400, { title: 'Bad request.' }));

    expect(problem.isValidation).toBe(false);
  });

  it('reports a network failure as offline rather than an unexplained error', () => {
    const problem = toApiProblem(httpError(0, null));

    expect(problem.code).toBe('client.offline');
    expect(problem.title).toBe('errors.offline');
  });

  it('falls back to an i18n key when the API sends no title', () => {
    expect(toApiProblem(httpError(403, {})).title).toBe('errors.forbidden');
    expect(toApiProblem(httpError(404, {})).title).toBe('errors.notFound');
  });

  it('handles a thrown value that is not an HTTP error at all', () => {
    const problem = toApiProblem(new Error('boom'));

    expect(problem.code).toBe('client.unexpected');
    expect(problem.status).toBe(0);
  });

  it('returns no messages for a field nothing was said about', () => {
    expect(fieldErrorsFor(null, 'price')).toEqual([]);
  });
});
