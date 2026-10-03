import { TranslocoService } from '@jsverse/transloco';
import { translateOr } from '@upbazaar/ui';

/**
 * A role's name as people read it, in the language showing.
 *
 * The API names roles in code - "SellerDispatch", "SuperAdmin" - and those stayed as they were in
 * Hindi, in a list that was otherwise translated. The labels sit under `staff.roleNames.`, keyed by
 * the API's name; a role created on the server that has no label yet shows that name, not a
 * translation key. The name itself is still what is sent back when roles are assigned.
 */
export function roleLabel(transloco: TranslocoService, name: string): string {
  return translateOr(transloco, 'staff.roleNames.' + name, name);
}

/** Several roles on one line, as the staff list and the confirmation show them. */
export function roleLabels(transloco: TranslocoService, names: readonly string[]): string {
  return names.map((name) => roleLabel(transloco, name)).join(', ');
}
