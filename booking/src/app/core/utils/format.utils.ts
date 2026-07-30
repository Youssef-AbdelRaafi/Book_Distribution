export function formatAmountRials(amount: number): string {
  const thousandths = Math.round((amount || 0) * 1000);
  const sign = thousandths < 0 ? '-' : '';
  return `${sign}${Math.floor(Math.abs(thousandths) / 1000)}`;
}

export function formatAmountBaisa(amount: number): string {
  const thousandths = Math.round((amount || 0) * 1000);
  return (Math.abs(thousandths) % 1000).toString().padStart(3, '0');
}
