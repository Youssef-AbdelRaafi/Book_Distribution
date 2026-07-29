export interface InvoiceItem {
  id?: number;
  bookId: number;
  bookName: string;
  bookGrade: string;
  quantity: number;
  unitPrice: number;
  total: number;
  // تفصيل الجرد — يُستخدم في المخالصة
  orderedQty?: number;
  refundedQty?: number;
  soldQuantity?: number;
  amountDue?: number;
  // backward compat
  name?: string;
  grade?: string;
  term?: string;
  subject?: string;
  price?: number;
}

export interface Invoice {
  id?: number;
  invoiceNumber: number;
  termCode: string; // "A" أو "B"
  displayNumber: string; // "1A", "2B"
  type: 'order' | 'refund' | 'clearance';
  libraryId: number;
  libraryName?: string;
  governorateName?: string;
  cityName?: string;
  semesterId: number;
  semesterName?: string;
  date: string;
  totalAmount: number;
  printStatus: string;
  responsibleName?: string;
  responsiblePhone?: string;
  items: InvoiceItem[];
  // backward compat
  region?: string;
  city?: string;
}

export interface ClearancePreviewItem {
  bookId: number;
  bookName: string;
  bookGrade: string;
  quantity: number;
  unitPrice: number;
  total: number;
  orderedQty?: number;
  refundedQty?: number;
  soldQuantity?: number;
  amountDue?: number;
}

export interface ClearancePreview {
  libraryId?: number;
  libraryName: string;
  governorateName: string;
  cityName: string;
  semesterId: number;
  semesterName: string;
  termCode: string;
  totalAmount: number;
  paidAmount: number;
  responsibleName?: string;
  responsiblePhone?: string;
  items: ClearancePreviewItem[];
}

export interface BatchClearanceResult {
  count: number;
  invoices: Invoice[];
}

export interface ClearanceLibraryPreview {
  libraryId: number;
  libraryName: string;
  governorateName: string;
  cityName: string;
  totalAmount: number;
  paidAmount: number;
  netAmount: number;
  responsibleName?: string;
  responsiblePhone?: string;
}

export interface DashboardData {
  totalLibraries: number;
  totalItems: number;
  lowStockCount: number;
  totalRevenue: number;
  totalCollected: number;
  totalOutstanding: number;
  totalItemsSold: number;
  orderCount: number;
  refundCount: number;
  criticalStock: {
    id: number;
    name: string;
    grade: string;
    stockQuantity: number;
    demand: number;
  }[];
  libraryBalances: {
    libraryId: number;
    libraryName: string;
    totalAmount: number;
    paidAmount: number;
    balance: number;
  }[];
  mostRefunded: { name: string; count: number }[];
  salesByTerm: { termCode: string; termName: string; revenue: number }[];
  classicSalesByYear: { academicYear: string; revenue: number; quantity: number }[];
  classicRows: {
    academicYear: string;
    termCode: string;
    termName: string;
    bestLibraryByRevenue: string;
    bestLibraryByQuantity: string;
    libraryCount: number;
    ordered: number;
    refunded: number;
    netRevenue: number;
    netQuantity: number;
  }[];
}
