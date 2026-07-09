export interface Book {
  id: number;
  name: string;
  grade: string;
  subject: string;
  semesterId: number;
  price: number;
  stockQuantity: number;
}

export interface LibraryBook {
  id: number;
  libraryId: number;
  bookId: number;
  bookName?: string;
  bookGrade?: string;
  bookPrice?: number;
  quantity: number;
}

// Backward compat alias
export interface InventoryItem {
  id: number;
  subject: string;
  grade: string;
  term: string;
  price: number;
  quantity: number;
  lowStock?: boolean;
  name?: string;
}
