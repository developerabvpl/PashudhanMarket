import { CartLineDto } from '@upbazaar/data-access';

/**
 * Why a line cannot be bought as it stands, as the Cart API reports it.
 *
 * Only an account cart ever carries one: a guest basket has no server to check it against,
 * so its lines are taken at face value until sign-in merges them.
 */
export type CartLineProblem = 'Unavailable' | 'InsufficientStock' | 'PriceChanged';

/**
 * One line of the basket, whichever side holds it.
 *
 * `price` is today's unit price and `priceWhenAdded` what the shopper saw when they added it;
 * the two differ only when the server has flagged PriceChanged.
 */
export interface CartLine {
  readonly productId: string;
  readonly sku: string;
  readonly name: string;
  readonly price: number;
  readonly priceWhenAdded: number;
  readonly currency: string;
  readonly quantity: number;
  readonly problem: CartLineProblem | null;
}

const PROBLEMS: readonly string[] = ['Unavailable', 'InsufficientStock', 'PriceChanged'];

/**
 * Maps a server line into the shape the page renders.
 *
 * Name and SKU come back null once a product has left the catalogue; the line still has to be
 * shown so the shopper can remove it, so it falls back to something printable.
 */
export function fromCartLineDto(dto: CartLineDto, currency: string): CartLine {
  return {
    productId: dto.productId,
    sku: dto.sku ?? '',
    name: dto.name ?? dto.sku ?? dto.productId,
    price: dto.unitPrice,
    priceWhenAdded: dto.priceWhenAdded,
    currency,
    quantity: dto.quantity,
    // An unknown value is treated as a problem rather than ignored, so a new server-side
    // reason still keeps the line out of the subtotal the way the server does.
    problem: dto.problem === null ? null : PROBLEMS.includes(dto.problem) ? (dto.problem as CartLineProblem) : 'Unavailable',
  };
}
