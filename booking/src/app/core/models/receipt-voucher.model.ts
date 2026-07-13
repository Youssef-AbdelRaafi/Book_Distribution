export interface ReceiptVoucher {
  id?: number;
  voucherNumber: number;
  voucherYear: number;
  displayNumber: string;
  libraryId: number;
  libraryName?: string;
  governorateName?: string;
  cityName?: string;
  semesterId?: number;
  semesterName?: string;
  amount: number;
  paymentMethod: 'cash' | 'cheque';
  chequeNumber?: string;
  bankName?: string;
  purpose: string;
  date: string;
  createdAt?: string;
}

export interface CreateReceiptVoucher {
  libraryId: number;
  semesterId?: number;
  amount: number;
  paymentMethod: 'cash' | 'cheque';
  chequeNumber?: string;
  bankName?: string;
  purpose: string;
  date: string;
}
