import { DOCUMENT, Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

export interface SeoTags {
  readonly title: string;
  readonly description: string;
  /** Path only, e.g. `/products/abc`. The origin comes from configuration. */
  readonly canonicalPath: string;
  readonly image?: string;
  readonly type?: 'website' | 'product' | 'article';
  /** Set for pages that must not be indexed, such as anything behind auth. */
  readonly noIndex?: boolean;
}

/**
 * Every storefront page goes through here for title, description, canonical and social tags.
 *
 * Canonicals matter more than usual on this app: the product list is filterable, so the same
 * catalogue is reachable through many query strings and search engines need one address per
 * page. Tags are applied during SSR, which is what makes them visible to crawlers at all.
 */
@Injectable({ providedIn: 'root' })
export class SeoService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  /** Public origin, used to build absolute canonical and og:url values. */
  private readonly origin = 'https://upbazaar.example';

  apply(tags: SeoTags): void {
    const fullTitle = `${tags.title} | UP Bazaar`;
    const url = `${this.origin}${tags.canonicalPath}`;

    this.title.setTitle(fullTitle);

    this.setName('description', tags.description);
    this.setName('robots', tags.noIndex ? 'noindex, nofollow' : 'index, follow');

    this.setProperty('og:title', fullTitle);
    this.setProperty('og:description', tags.description);
    this.setProperty('og:type', tags.type ?? 'website');
    this.setProperty('og:url', url);

    this.setName('twitter:card', tags.image ? 'summary_large_image' : 'summary');

    if (tags.image) {
      this.setProperty('og:image', tags.image);
    }

    this.setCanonical(url);
  }

  /**
   * Emits a JSON-LD block. Replaces any block written by a previous route so a product page
   * never inherits the previous product's structured data during client-side navigation.
   */
  setJsonLd(schema: Record<string, unknown> | null): void {
    const existing = this.document.getElementById('upb-jsonld');
    existing?.remove();

    if (schema === null) {
      return;
    }

    const script = this.document.createElement('script');
    script.id = 'upb-jsonld';
    script.type = 'application/ld+json';
    script.textContent = JSON.stringify(schema);

    this.document.head.appendChild(script);
  }

  private setName(name: string, content: string): void {
    this.meta.updateTag({ name, content });
  }

  private setProperty(property: string, content: string): void {
    this.meta.updateTag({ property, content });
  }

  private setCanonical(url: string): void {
    let link = this.document.head.querySelector<HTMLLinkElement>('link[rel="canonical"]');

    // The server-side DOM returns undefined rather than null for a miss, so this has to be a
    // falsy check: `=== null` sails past it and throws on the first server render.
    if (!link) {
      link = this.document.createElement('link');
      link.setAttribute('rel', 'canonical');
      this.document.head.appendChild(link);
    }

    link.setAttribute('href', url);
  }
}
