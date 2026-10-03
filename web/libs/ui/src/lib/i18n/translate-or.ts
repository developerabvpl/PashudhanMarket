import { TranslocoService } from '@jsverse/transloco';

/**
 * The words for something the API names - a role, a state - when we have words for that name, and
 * the API's own name when we do not.
 *
 * Such lists are the server's: a role added there, or a state renamed, reaches the screen before
 * its label lands in the translation files. Translating the key blindly would then print the key
 * itself ("staff.roleNames.Auditor"); this prints "Auditor" instead, which is at least what the
 * server calls it. A key the page's language lacks but English has reads in English, as any other
 * string does.
 *
 * Reads the language showing at the moment it is called, so call it from a template (which is
 * drawn again when the language changes), not once into a field.
 */
export function translateOr(transloco: TranslocoService, key: string, fallback: string): string {
  const known = [transloco.getActiveLang(), transloco.getDefaultLang()].some(
    (lang) => key in transloco.getTranslation(lang)
  );

  return known ? transloco.translate(key) : fallback;
}
