# Product photographs

One photograph per catalogue category, served at `/media/products/<code>.jpg`. The code is the middle
segment of the SKU — `UPB-AGB-001` looks for `agb.jpg` — so every product in a category shares
its category's picture until per-product photography exists.

| File | Category | What it should show |
| --- | --- | --- |
| `agb.jpg` | Gobar Agarbatti / Dhoop Batti | Cow dung dhoop / incense sticks |
| `ark.jpg` | Gomutra Ark (Distilled Cow Urine) | A bottle of gomutra ark |
| `diy.jpg` | Gobar Diya / Deepak | Cow dung diyas |
| `kan.jpg` | Gobar Kande / Upale | Stacked cow dung cakes |
| `khd.jpg` | Gobar Khad / Organic Manure | Manure in hand, or a seedling being fed |
| `phn.jpg` | Gomutra Phenyl / Floor Cleaner | The cleaner bottle, or a floor being mopped |
| `sab.jpg` | Panchgavya / Gomutra Sabun | The soap bar |
| `sam.jpg` | Gobar Sambrani / Havan Cups | A lit sambrani cup |
| `ghn.jpg` | Gomutra Ghanvati / Ayurvedic | **No photograph yet** |

**A category is only asked for if it is listed in `CATEGORY_PHOTOS`** in
`apps/storefront/src/app/features/products/product-thumb.ts`. That list is what stops the browser
requesting a file nobody has supplied. Adding a photograph therefore takes two steps: drop the
file here, and add its code to that set. Removing one takes the same two.

Anything missing — a category with no entry, or an entry whose file fails to load — falls back to
a drawn tile, so the grid never shows a broken-image icon.

Square or 4:3 works best; the card crops to 4:3 and the product page to a square. Keep them under
roughly 200 KB: they are served as-is, with no build-time resizing.
