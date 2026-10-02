/**
 * Joins the filled-in parts of an address or name with a separator, leaving out the empty ones.
 *
 * Templates that glued optional parts on inline - `{{ line1 }}@if (line2) {, {{ line2 }} }, {{ city }}`
 * - picked up the block's whitespace and printed "Near Sankat Mochan , Varanasi". Joining in code
 * trims every part, skips blank ones, and so never leaves a stray space or a doubled comma.
 */
export function joinParts(parts: readonly (string | null | undefined)[], separator = ', '): string {
  return parts
    .map((part) => part?.trim() ?? '')
    .filter((part) => part !== '')
    .join(separator);
}
