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

  private prependBook(book: Book): void {
    this.inventorySubject.next([book, ...this.inventorySubject.value]);
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
        if (Array.isArray(created)) this.inventorySubject.next([...created, ...this.inventorySubject.value]);
      })
    );
  }

  updateBook(id: number, book: Partial<Book>): Observable<ApiResponse<Book>> {
    return this.http.put<ApiResponse<Book>>(`${this.apiUrl}/${id}`, book).pipe(
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

  // Backward compat methods
  addInventoryItem(item: any) {
    this.addBook(item).subscribe({ error: () => this.toast.show('تعذر إضافة العنصر', 'error') });
  }
  updateInventoryItem(item: any) {
    this.updateBook(item.id, item).subscribe({ error: () => this.toast.show('تعذر تحديث العنصر', 'error') });
  }
  deleteInventoryItem(id: number) {
    this.deleteBook(id).subscribe({ error: () => this.toast.show('تعذر حذف العنصر', 'error') });
  }
  executeCompensation(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    if (activity.type === 'ADD' && payload.id) {
      return this.deleteBook(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload) {
      return this.addBook(payload as unknown as Book).pipe(map(() => undefined));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.previous) {
      return this.updateBook(payload.id, payload.previous as unknown as Partial<Book>).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }
  executeRedo(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    if (activity.type === 'ADD' && payload) {
      return this.addBook(payload as unknown as Book).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload?.id) {
      return this.deleteBook(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.current) {
      return this.updateBook(payload.id, payload.current as unknown as Partial<Book>).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }
}
