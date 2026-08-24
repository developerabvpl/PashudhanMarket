import { TestBed } from '@angular/core/testing';
import { SeoService } from './seo.service';

describe('SeoService', () => {
  let seo: SeoService;

  beforeEach(() => {
    document.head.querySelectorAll('link[rel="canonical"], #upb-jsonld').forEach((el) => el.remove());
    document.head.querySelectorAll('meta[name], meta[property]').forEach((el) => el.remove());

    TestBed.configureTestingModule({});
    seo = TestBed.inject(SeoService);
  });

  function meta(selector: string): string | null {
    return document.head.querySelector(selector)?.getAttribute('content') ?? null;
  }

  it('sets title, description and canonical', () => {
    seo.apply({
      title: 'Banarasi Silk Saree',
      description: 'Handwoven silk saree.',
      canonicalPath: '/products/abc',
    });

    expect(document.title).toBe('Banarasi Silk Saree | UP Bazaar');
    expect(meta('meta[name="description"]')).toBe('Handwoven silk saree.');
    expect(
      document.head.querySelector('link[rel="canonical"]')?.getAttribute('href')
    ).toBe('https://upbazaar.example/products/abc');
  });

  it('reuses the one canonical link across navigations instead of stacking them', () => {
    seo.apply({ title: 'A', description: 'a', canonicalPath: '/products' });
    seo.apply({ title: 'B', description: 'b', canonicalPath: '/products/xyz' });

    expect(document.head.querySelectorAll('link[rel="canonical"]')).toHaveLength(1);
    expect(
      document.head.querySelector('link[rel="canonical"]')?.getAttribute('href')
    ).toBe('https://upbazaar.example/products/xyz');
  });

  it('marks a page noindex when asked', () => {
    seo.apply({ title: 'A', description: 'a', canonicalPath: '/x', noIndex: true });

    expect(meta('meta[name="robots"]')).toBe('noindex, nofollow');
  });

  it('defaults to indexable', () => {
    seo.apply({ title: 'A', description: 'a', canonicalPath: '/x' });

    expect(meta('meta[name="robots"]')).toBe('index, follow');
  });

  it('emits Open Graph tags pointing at the canonical URL', () => {
    seo.apply({ title: 'A', description: 'a', canonicalPath: '/products/abc', type: 'product' });

    expect(meta('meta[property="og:type"]')).toBe('product');
    expect(meta('meta[property="og:url"]')).toBe('https://upbazaar.example/products/abc');
  });

  it('replaces the JSON-LD block rather than appending a second one', () => {
    seo.setJsonLd({ '@type': 'Product', name: 'First' });
    seo.setJsonLd({ '@type': 'Product', name: 'Second' });

    const blocks = document.head.querySelectorAll('#upb-jsonld');

    expect(blocks).toHaveLength(1);
    expect(blocks[0].textContent).toContain('Second');
  });

  it('removes the JSON-LD block when a page has none', () => {
    seo.setJsonLd({ '@type': 'Product' });
    seo.setJsonLd(null);

    expect(document.head.querySelector('#upb-jsonld')).toBeNull();
  });
});
