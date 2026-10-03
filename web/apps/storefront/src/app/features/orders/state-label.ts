import { TranslocoService } from '@jsverse/transloco';
import { translateOr } from '@upbazaar/ui';

/**
 * A delivery state's name in the language showing.
 *
 * The API lists the states and union territories by their English names (GET
 * /orders/delivery-states), and those names are what an address stores and the courier reads, so
 * the option's value stays exactly as sent. What the buyer reads is looked up under
 * `checkout.states.`, keyed by the name with everything but its letters dropped ("Tamil Nadu" ->
 * TamilNadu) since the API sends no code. A state the API adds before its label lands here shows
 * the API's name.
 */
export function stateLabel(transloco: TranslocoService, name: string): string {
  return translateOr(transloco, 'checkout.states.' + name.replace(/[^A-Za-z]/g, ''), name);
}
