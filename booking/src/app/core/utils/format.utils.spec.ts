import { describe, expect, it } from 'vitest';
import { formatAmountBaisa, formatAmountRials } from './format.utils';

describe('currency-format utilities', () => {
  it('formats the whole-rial component without rounding up', () => {
    expect(formatAmountRials(12.999)).toBe('12');
  });

  it('formats the baisa component as three digits', () => {
    expect(formatAmountBaisa(12.005)).toBe('005');
  });

  it('keeps a valid baisa component for negative values', () => {
    expect(formatAmountBaisa(-1.234)).toBe('234');
  });
});
