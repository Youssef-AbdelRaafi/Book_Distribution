export function formatAmountRials(amount: number): string {
  return Math.floor(amount).toString();
}

export function formatAmountBaisa(amount: number): string {
  return (Math.abs(Math.round(((amount || 0) * 1000) % 1000))).toString().padStart(3, '0');
}
