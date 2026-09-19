import catalog from '@upbazaar/catalog-data';
import {
  CategoryDto,
  PagedListOfProductSummaryDto,
  ProductDto,
  ProductSummaryDto,
} from '@upbazaar/data-access';

/**
 * The catalogue, read straight out of the bundle.
 *
 * Seventy products in a 49 KB file that only changes when someone redeploys. Fetching that over
 * HTTP bought nothing and cost a great deal: it needed a Node process next to the web server to
 * answer /api/catalog, which in turn needed an IIS module the department did not want to install.
 * Importing it instead makes the whole catalogue prerenderable at build time and leaves nothing
 * to run on the server.
 *
 * The shapes below are the ones the API will return when the Catalog module ships. When it does,
 * replace the three functions here with real calls and delete the import — the resolvers and the
 * components above them do not know the difference.
 */

interface RawCategory {
  id: string;
  name: string;
  slug: string;
  parentId: string | null;
}

interface RawProduct {
  id: string;
  sku: string;
  name: string;
  slug: string;
  brand: string | null;
  description: string | null;
  price: number;
  currency: string;
  status: string;
  sellerId: string;
  categoryId: string;
  onHandQuantity: number;
  reservedQuantity: number;
  createdAtUtc: string;
  modifiedAtUtc: string | null;
}

const categories = catalog.categories as RawCategory[];
const products = catalog.products as RawProduct[];

const categoriesById = new Map(categories.map((category) => [category.id, category]));
const productsById = new Map(products.map((product) => [product.id, product]));

/** The contract's CategoryDto; the import bookkeeping in the JSON stays out of it. */
function categoryDto(category: RawCategory): CategoryDto {
  return {
    id: category.id,
    name: category.name,
    slug: category.slug,
    parentId: category.parentId,
  };
}

function productDto(product: RawProduct): ProductDto {
  const category = categoriesById.get(product.categoryId);

  if (category === undefined) {
    throw new Error(`Product ${product.sku} names category ${product.categoryId}, which is absent.`);
  }

  return {
    id: product.id,
    sku: product.sku,
    name: product.name,
    slug: product.slug,
    brand: product.brand,
    description: product.description,
    price: product.price,
    currency: product.currency,
    status: product.status,
    sellerId: product.sellerId,
    category: categoryDto(category),
    onHandQuantity: product.onHandQuantity,
    reservedQuantity: product.reservedQuantity,
    createdAtUtc: product.createdAtUtc,
    modifiedAtUtc: product.modifiedAtUtc,
  };
}

function summaryDto(product: RawProduct): ProductSummaryDto {
  return {
    id: product.id,
    sku: product.sku,
    name: product.name,
    price: product.price,
    currency: product.currency,
    status: product.status,
    availableQuantity: product.onHandQuantity - product.reservedQuantity,
  };
}

/** What the listing page can narrow by. Every field is optional; omitting all of them lists everything. */
export interface CatalogQuery {
  page?: number;
  pageSize?: number;
  search?: string;
  categoryId?: string;
  activeOnly?: boolean;
}

function matches(query: CatalogQuery): RawProduct[] {
  const term = (query.search ?? '').trim().toLowerCase();

  return products.filter((product) => {
    if (query.activeOnly === true && product.status !== 'Active') {
      return false;
    }

    if (query.categoryId !== undefined && product.categoryId !== query.categoryId) {
      return false;
    }

    if (term === '') {
      return true;
    }

    // Brand and category are searchable too: "Goseva" and "diya" are how a shopper thinks about
    // this catalogue, and neither of them appears in the SKU.
    const haystack = [
      product.name,
      product.sku,
      product.brand ?? '',
      categoriesById.get(product.categoryId)?.name ?? '',
    ]
      .join(' ')
      .toLowerCase();

    return haystack.includes(term);
  });
}

/** One page of the catalogue, filtered and sliced exactly as the API would return it. */
export function listProducts(query: CatalogQuery): PagedListOfProductSummaryDto {
  const page = Math.max(1, query.page ?? 1);
  const pageSize = Math.min(100, Math.max(1, query.pageSize ?? 24));

  const found = matches(query);
  const totalPages = Math.ceil(found.length / pageSize);
  const start = (page - 1) * pageSize;

  return {
    items: found.slice(start, start + pageSize).map(summaryDto),
    page,
    pageSize,
    totalCount: found.length,
    totalPages,
    hasNextPage: page < totalPages,
  };
}

/** One product, or null when the id names nothing — the caller redirects rather than showing a shell. */
export function getProduct(productId: string): ProductDto | null {
  const product = productsById.get(productId);

  return product === undefined ? null : productDto(product);
}

/** The category rail. */
export function listCategories(): CategoryDto[] {
  return categories.map(categoryDto);
}

/** Every product id, for the prerenderer to walk. */
export function allProductIds(): string[] {
  return products.map((product) => product.id);
}
