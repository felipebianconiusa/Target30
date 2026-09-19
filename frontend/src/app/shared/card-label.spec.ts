import { describe, expect, it } from 'vitest';
import { cardLabel } from './card-label';

describe('cardLabel', () => {
  it('returns just the original name when there is no nickname', () => {
    expect(cardLabel(null, 'Savor')).toBe('Savor');
    expect(cardLabel(undefined, 'Savor')).toBe('Savor');
    expect(cardLabel('', 'Savor')).toBe('Savor');
  });

  it('shows the nickname followed by the original name', () => {
    expect(cardLabel('Mercado', 'Savor')).toBe('Mercado (Savor)');
  });
});
