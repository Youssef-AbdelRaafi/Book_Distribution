import { describe, expect, it } from 'vitest';
import { formatAmountBaisa, formatAmountRials } from './format.utils';

describe('currency-format utilities', () => {
  it('carries a rounded fractional amount into the whole-rial component', () => {
    expect(formatAmountRials(1.9999)).toBe('2');
  });

  it('formats the baisa component as three digits', () => {
    expect(formatAmountBaisa(12.005)).toBe('005');
  });

  it('keeps a valid baisa component for negative values', () => {
    expect(formatAmountBaisa(-1.234)).toBe('234');
  });

  it('never produces a four-digit baisa component', () => {
    expect(formatAmountBaisa(1.9999)).toBe('000');
  });
});
