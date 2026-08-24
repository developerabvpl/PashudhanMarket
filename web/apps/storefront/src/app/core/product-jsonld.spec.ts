import { ProductDto } from '@upbazaar/data-access';
import { breadcrumbJsonLd, productJsonLd } from './product-jsonld';

const baseProduct: ProductDto = {
  id: 'a1',
  sku: 'UPB-SAREE-001',
  name: 'Banarasi Silk Saree',
  slug: 'banarasi-silk-saree',
  description: 'Handwoven in Varanasi.',
  price: 4599,
  currency: 'INR',
  status: 'Active',
  sellerId: 's1',
  category: { id: 'c1', name: 'Sarees', slug: 'sarees', parentId: null },
  onHandQuantity: 10,
  reservedQuantity: 2,
  createdAtUtc: '2026-03-14T10:00:00Z',
  modifiedAtUtc: null,
};

const url = 'https://upbazaar.example/products/a1';

describe('productJsonLd', () => {
  it('publishes the offer with price and currency', () => {
    const schema = productJsonLd(baseProduct, url) as Record<string, any>;

    expect(schema['@type']).toBe('Product');
    expect(schema['offers'].price).toBe(4599);
    expect(schema['offers'].priceCurrency).toBe('INR');
  });

  it('reports InStock while unreserved units remain', () => {
    const schema = productJsonLd(baseProduct, url) as Record<string, any>;

    expect(schema['offers'].availability).toBe('https://schema.org/InStock');
  });

  it('reports OutOfStock once every unit is reserved', () => {
    const soldOut = { ...baseProduct, onHandQuantity: 2, reservedQuantity: 2 };

    const schema = productJsonLd(soldOut, url) as Record<string, any>;

    expect(schema['offers'].availability).toBe('https://schema.org/OutOfStock');
  });

  it('falls back to the name when a product has no description', () => {
    const schema = productJsonLd({ ...baseProduct, description: null }, url) as Record<string, any>;

    expect(schema['description']).toBe('Banarasi Silk Saree');
  });
});

describe('breadcrumbJsonLd', () => {
  it('lists products then the product itself', () => {
    const schema = breadcrumbJsonLd(baseProduct, 'https://upbazaar.example', url) as Record<string, any>;
    const items = schema['itemListElement'] as Array<Record<string, unknown>>;

    expect(items).toHaveLength(2);
    expect(items[1]['name']).toBe('Banarasi Silk Saree');
    expect(items[1]['item']).toBe(url);
  });
});
