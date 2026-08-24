# Product photographs

Served at `/media/products/<file>`. Which file a product gets is decided in
`apps/storefront/src/app/features/products/product-thumb.ts` — `CATEGORY_PHOTOS` maps the middle
segment of the SKU to a filename, and `TYPE_PHOTOS` overrides that for product types within a
category that have their own picture.

**Not under `public/products`.** That folder shadows the `/products` route: the static middleware
claims the path and answers with a redirect before the router sees it, and the catalogue stops
resolving.

| File | Shown for |
| --- | --- |
| `Dhoop.webp` | `AGB` — Gobar Agarbatti / Dhoop Batti, default for the category |
| `agarbatti.jpg` | `AGB` listings that say *agarbatti* or *incense* without saying *dhoop* |
| `Cow dung cake.jpg` | `KAN` — Gobar Kande / Upale |
| `gomutra.jpg` | `ARK` — Gomutra Ark |
| `Soap.jpg` | `SAB` — Panchgavya / Gomutra Sabun |
| `Cow Dung Diya.jpg` | `DIY` — Gobar Diya / Deepak |
| `Floor cleaner.jpg` | `PHN` — Gomutra Phenyl / Floor Cleaner |
| `Khad.jpg` | `KHD` — Gobar Khad / Organic Manure |
| `haven cup.jpg` | `SAM` — Gobar Sambrani / Havan Cups |
| — | `GHN` — Gomutra Ghanvati has no photograph and falls back to a drawn tile |

## Adding or changing one

Drop the file here and point an entry at it. Filenames are listed in code rather than derived, so
they can be anything — spaces and mixed extensions are fine, and the URL is percent-encoded. A
category with no entry, or an entry whose file fails to load, falls back to a drawn tile rather
than a broken-image icon, and its JSON-LD carries no `image`.

## Shape and size

Cards and the product page both render a square box with `object-contain`, so nothing is cropped
and any aspect ratio is safe — but a portrait shot letterboxes with white bands either side. The
supplied `Floor cleaner.jpg`, `Soap.jpg` and `gomutra.jpg` are portrait; squarer replacements
would fill the tile better.

Keep files under roughly 200 KB. They are served as-is, with no build-time resizing.
