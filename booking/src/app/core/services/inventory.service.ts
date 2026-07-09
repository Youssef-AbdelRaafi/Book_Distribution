import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, of, tap } from 'rxjs';
import { Book, InventoryItem } from '../models/inventory.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class InventoryService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/books`;

  private inventorySubject = new BehaviorSubject<Book[]>([]);
  public inventory$ = this.inventorySubject.asObservable();

  fetchBooks(semesterId?: number): void {
    let params: any = {};
    if (semesterId) params.semesterId = semesterId.toString();
    this.http.get<any>(this.apiUrl, { params }).pipe(
      tap(res => {
        const data = res.data || res;
        this.inventorySubject.next(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        console.error('API Error fetching books', error);
        return of([]);
      })
    ).subscribe();
  }

  getItemById(id: number): Book | undefined {
    return this.inventorySubject.value.find(item => item.id === id);
  }

  addBook(book: Partial<Book>): Observable<any> {
    return this.http.post<any>(this.apiUrl, book).pipe(
      tap(() => this.fetchBooks())
    );
  }

  addBooksBulk(books: Partial<Book>[]): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/bulk`, { books }).pipe(
      tap(() => this.fetchBooks())
    );
  }

  updateBook(id: number, book: Partial<Book>): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, book).pipe(
      tap(() => this.fetchBooks())
    );
  }

  deleteBook(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.fetchBooks())
    );
  }

  // Backward compat methods
  addInventoryItem(item: any) { this.addBook(item).subscribe(); }
  updateInventoryItem(item: any) { this.updateBook(item.id, item).subscribe(); }
  deleteInventoryItem(id: number) { this.deleteBook(id).subscribe(); }
  executeCompensation(activity: any) {}
  executeRedo(activity: any) {}
}
