import { CategoryDto } from '@upbazaar/data-access';

/** A category placed in the tree, with how deep it sits. */
export interface CategoryRow {
  readonly category: CategoryDto;
  readonly depth: number;
}

/**
 * Orders a flat category list as a tree: each parent followed by its children, alphabetical at
 * every level. A category whose parent is missing is treated as top level, so it still shows.
 */
export function toTree(categories: readonly CategoryDto[]): CategoryRow[] {
  const ids = new Set(categories.map((c) => c.id));
  const byName = [...categories].sort((a, b) => a.name.localeCompare(b.name));
  const rows: CategoryRow[] = [];

  const visit = (parentId: string | null, depth: number): void => {
    for (const category of byName) {
      const parent = category.parentId && ids.has(category.parentId) ? category.parentId : null;

      if (parent === parentId && !rows.some((r) => r.category.id === category.id)) {
        rows.push({ category, depth });
        visit(category.id, depth + 1);
      }
    }
  };

  visit(null, 0);

  return rows;
}

/** Ids of every category beneath the given one, at any depth. */
export function descendantsOf(categoryId: string, categories: readonly CategoryDto[]): string[] {
  const found: string[] = [];
  let frontier = [categoryId];

  while (frontier.length > 0) {
    const children = categories.filter((c) => c.parentId && frontier.includes(c.parentId) && !found.includes(c.id));

    found.push(...children.map((c) => c.id));
    frontier = children.map((c) => c.id);
  }

  return found;
}
