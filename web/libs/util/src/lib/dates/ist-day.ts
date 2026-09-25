/**
 * Turning a date picked in a form into a moment. People here mean a day in India: a sale that
 * "ends on the 31st" should run to the last second of the 31st in IST, not stop at 5:30 am when
 * UTC's 31st ends.
 */

/** The first moment of an IST day, from a date input's yyyy-mm-dd value, as ISO UTC. */
export function startOfIstDay(date: string): string {
  return new Date(`${date}T00:00:00+05:30`).toISOString();
}

/** The last second of an IST day, from a date input's yyyy-mm-dd value, as ISO UTC. */
export function endOfIstDay(date: string): string {
  return new Date(`${date}T23:59:59+05:30`).toISOString();
}
