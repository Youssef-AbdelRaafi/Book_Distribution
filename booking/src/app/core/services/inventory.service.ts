import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, of, tap, map, throwError } from 'rxjs';
import { Book } from '../models/inventory.model';
import { ApiResponse } from '../models/api-response.model';
import { ActivityPayload } from '../models/activity.model';
import { ToastService } from './toast.service';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private http = inject(HttpClient);
  private toast = inject(ToastService);
  private apiUrl = `${environment.apiUrl}/books`;

  private inventorySubject = new BehaviorSubject<Book[]>([]);
  public inventory$ = this.inventorySubject.asObservable();

  fetchBooks(semesterId?: number): void {
    let params = new HttpParams();
    if (semesterId) params = params.set('semesterId', semesterId.toString());
    this.http.get<ApiResponse<Book[]>>(this.apiUrl, { params }).pipe(
      tap(res => {
        const data = res.data;
        this.inventorySubject.next(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل المخزون', 'error');
        return of([]);
      })
    ).subscribe();
  }

  getItemById(id: number): Book | undefined {
    return this.inventorySubject.value.find(item => item.id === id);
  }

  private sortBooks(books: Book[]): Book[] {
    const gradeOrder: Record<string, number> = {
      'إصدارات الصف التاسع': 1,
      'إصدارات الصف العاشر': 2,
      'إصدارات الصف الحادي عشر': 3,
      'إصدارات الصف الثاني عشر': 4
    };

    const subjectOrder: Record<string, number> = {
      'فيزياء': 1,
      'كيمياء': 2,
      'علوم بيئية': 3
    };

    return [...books].sort((a, b) => {
      const gA = gradeOrder[a.grade] ?? 99;
      const gB = gradeOrder[b.grade] ?? 99;
      if (gA !== gB) return gA - gB;

      const sA = subjectOrder[a.subject] ?? 99;
      const sB = subjectOrder[b.subject] ?? 99;
      if (sA !== sB) return sA - sB;

      return (a.name || '').localeCompare(b.name || '');
    });
  }

  private prependBook(book: Book): void {
    this.inventorySubject.next(this.sortBooks([book, ...this.inventorySubject.value]));
  }

  private replaceBook(id: number, book: Book): void {
    this.inventorySubject.next(
      this.inventorySubject.value.map(b => b.id === id ? book : b)
    );
  }

  private removeBook(id: number): void {
    this.inventorySubject.next(
      this.inventorySubject.value.filter(b => b.id !== id)
    );
  }

  addBook(book: Partial<Book>): Observable<ApiResponse<Book>> {
    return this.http.post<ApiResponse<Book>>(this.apiUrl, book).pipe(
      tap(res => {
        const created = res.data;
        if (created?.id) this.prependBook(created);
      })
    );
  }

  addBooksBulk(books: Partial<Book>[]): Observable<ApiResponse<Book[]>> {
    return this.http.post<ApiResponse<Book[]>>(`${this.apiUrl}/bulk`, { books }).pipe(
      tap(res => {
        const created = res.data;
        if (Array.isArray(created)) this.inventorySubject.next(this.sortBooks([...created, ...this.inventorySubject.value]));
      })
    );
  }

  updateBook(id: number, book: Partial<Book>, isCompensation = false): Observable<ApiResponse<Book>> {
    const url = isCompensation ? `${this.apiUrl}/${id}?isCompensation=true` : `${this.apiUrl}/${id}`;
    return this.http.put<ApiResponse<Book>>(url, book).pipe(
      tap(res => {
        const updated = res.data;
        if (updated?.id) this.replaceBook(id, updated);
      })
    );
  }

  deleteBook(id: number): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.removeBook(id))
    );
  }

  restoreBook(id: number): Observable<ApiResponse<Book>> {
    return this.http.post<ApiResponse<Book>>(`${this.apiUrl}/${id}/restore`, {});
  }

  // Backward compat methods
  addInventoryItem(item: Partial<Book>) {
    this.addBook(item).subscribe({ error: () => this.toast.show('تعذر إضافة العنصر', 'error') });
  }
  updateInventoryItem(item: Book) {
    this.updateBook(item.id, item).subscribe({ error: () => this.toast.show('تعذر تحديث العنصر', 'error') });
  }
  deleteInventoryItem(id: number) {
    this.deleteBook(id).subscribe({ error: () => this.toast.show('تعذر حذف العنصر', 'error') });
  }
  executeCompensation(activity: { type?: string; payload?: ActivityPayload }): Observable<void> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    if (activity.type === 'ADD' && payload.id) {
      return this.deleteBook(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload) {
      if (payload.id) {
        return this.restoreBook(payload.id).pipe(
          tap(() => this.fetchBooks()),
          map(() => undefined)
        );
      }
      if (payload['name']) {
        const book: Partial<Book> = {
          name: payload['name'] as string,
          grade: payload['grade'] as string,
          subject: payload['subject'] as string,
          semesterId: payload['semesterId'] as number,
          price: payload['price'] as number,
          stockQuantity: payload['stockQuantity'] as number
        };
        return this.addBook(book).pipe(map(() => undefined));
      }
      return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.previous) {
      return this.updateBook(payload.id, payload.previous as unknown as Partial<Book>, true).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }
  executeRedo(activity: { type?: string; payload?: ActivityPayload }): Observable<void> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    if (activity.type === 'ADD' && payload) {
      const data = (payload['data'] as any) || payload;
      const book: Partial<Book> = {
        name: data['name'] as string,
        grade: data['grade'] as string,
        subject: data['subject'] as string,
        semesterId: data['semesterId'] as number,
        price: data['price'] as number,
        stockQuantity: data['stockQuantity'] as number
      };
      return this.addBook(book).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload?.id) {
      return this.deleteBook(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.current) {
      return this.updateBook(payload.id, payload.current as unknown as Partial<Book>, true).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }
}
