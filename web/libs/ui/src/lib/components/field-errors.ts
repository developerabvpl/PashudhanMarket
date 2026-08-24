import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';

/**
 * Renders the messages for one form control, wherever they came from: a client-side validator
 * or the API's problem-details response. Both arrive here as i18n keys.
 *
 * ```html
 * <upb-field-errors [errors]="priceErrors()" fieldId="price" />
 * ```
 *
 * `fieldId` ties the messages to the input with `aria-describedby`, which is what lets a
 * screen reader read the error when focus lands on the field.
 */
@Component({
  selector: 'upb-field-errors',
  imports: [TranslocoPipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (messages().length > 0) {
    <ul [id]="fieldId() + '-errors'" class="mt-1 space-y-0.5 text-sm text-danger" role="alert">
      @for (message of messages(); track message) {
      <li>{{ message | transloco }}</li>
      }
    </ul>
    }
  `,
})
export class FieldErrors {
  readonly errors = input<readonly string[] | null>([]);

  readonly fieldId = input.required<string>();

  readonly messages = computed<readonly string[]>(() => this.errors() ?? []);
}
