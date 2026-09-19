import { describe, expect, it } from 'vitest';
import { ALL_CATEGORY_CODES, CATEGORY_FALLBACK_CODE, translateCategory } from './category-labels';

describe('translateCategory', () => {
  it('translates a known category code in each supported language', () => {
    expect(translateCategory('FOOD_AND_DRINK', 'pt')).toBe('Alimentação');
    expect(translateCategory('FOOD_AND_DRINK', 'en')).toBe('Food and drink');
    expect(translateCategory('FOOD_AND_DRINK', 'es')).toBe('Comida y bebida');
  });

  it('falls back to the "Outros" bucket label when given null', () => {
    expect(translateCategory(null, 'pt')).toBe('Outros');
  });

  it('falls back to a humanized version of unknown codes', () => {
    expect(translateCategory('SOME_NEW_CODE', 'pt')).toBe('Some new code');
  });

  it('includes the fallback code in the fixed list of category codes', () => {
    expect(ALL_CATEGORY_CODES).toContain(CATEGORY_FALLBACK_CODE);
  });
});
