import { joinParts } from './join-parts';

describe('joinParts', () => {
  it('joins the filled-in parts with a comma and no stray spaces', () => {
    expect(joinParts(['12 Lanka', 'Near Sankat Mochan ', 'Varanasi'])).toBe('12 Lanka, Near Sankat Mochan, Varanasi');
  });

  it('leaves out empty, blank and missing parts', () => {
    expect(joinParts(['12 Lanka', null, '  ', undefined, 'Varanasi'])).toBe('12 Lanka, Varanasi');
    expect(joinParts([null, ''])).toBe('');
  });

  it('takes another separator', () => {
    expect(joinParts(['Uttar Pradesh', '221005'], ' ')).toBe('Uttar Pradesh 221005');
  });
});
