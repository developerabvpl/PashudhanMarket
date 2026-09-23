import { CategoryDto } from '@upbazaar/data-access';
import { descendantsOf, toTree } from './category-tree';

const category = (id: string, name: string, parentId: string | null = null): CategoryDto => ({
  id,
  name,
  slug: name.toLowerCase(),
  parentId,
});

const all = [
  category('soap', 'Soap'),
  category('neem', 'Neem soap', 'soap'),
  category('diyas', 'Diyas'),
  category('big', 'Big diyas', 'diyas'),
  category('huge', 'Huge diyas', 'big'),
  category('orphan', 'Orphan', 'gone'),
];

describe('category tree', () => {
  it('puts each parent before its children, alphabetically at every level', () => {
    expect(toTree(all).map((r) => `${r.depth}:${r.category.id}`)).toEqual([
      '0:diyas',
      '1:big',
      '2:huge',
      '0:orphan',
      '0:soap',
      '1:neem',
    ]);
  });

  it('finds every descendant, however deep', () => {
    expect(descendantsOf('diyas', all).sort()).toEqual(['big', 'huge']);
    expect(descendantsOf('neem', all)).toEqual([]);
  });
});
