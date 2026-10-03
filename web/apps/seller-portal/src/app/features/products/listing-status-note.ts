/**
 * The sentence over a listing's details saying where it stands and what the seller can do about
 * it. Each status has its own: only a live listing is "live", one in review is waiting for a
 * moderator, and a draft is not on sale at all until it is submitted. A new listing, not saved
 * yet, has no status and needs no sentence.
 */
export function listingStatusNoteKey(status: string | null | undefined): string | null {
  switch (status) {
    case 'Draft':
      return 'sellerPortal.statusNote.Draft';
    case 'InReview':
      return 'sellerPortal.statusNote.InReview';
    case 'Active':
      return 'sellerPortal.statusNote.Active';
    case 'Archived':
      return 'sellerPortal.statusNote.Archived';
    default:
      return null;
  }
}
