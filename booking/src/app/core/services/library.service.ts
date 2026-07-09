import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, of, tap } from 'rxjs';
import { Library, Governorate } from '../models/library.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/libraries`;
  private govUrl = `${environment.apiUrl}/governorates`;

  private librariesSubject = new BehaviorSubject<Library[]>([]);
  public libraries$ = this.librariesSubject.asObservable();

  governorates = signal<Governorate[]>([]);

  fetchLibraries(): void {
    this.http.get<any>(this.apiUrl).pipe(
      tap(res => {
        const data = res.data || res;
        this.librariesSubject.next(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        console.error('API Error fetching libraries', error);
        return of([]);
      })
    ).subscribe();
  }

  fetchGovernorates(): void {
    this.http.get<any>(this.govUrl).pipe(
      tap(res => {
        const data = res.data || res;
        this.governorates.set(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        console.error('API Error fetching governorates', error);
        return of([]);
      })
    ).subscribe();
  }

  addLibrary(lib: Partial<Library>): Observable<any> {
    return this.http.post<any>(this.apiUrl, lib).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  updateLibrary(id: number, lib: Partial<Library>): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}`, lib).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  updateRating(id: number, rating: { responseRating?: string; paymentRating?: string; notes?: string }): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/rating`, rating).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  deleteLibrary(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  getLibraryBooks(libraryId: number, semesterId?: number): Observable<any> {
    let params: any = {};
    if (semesterId) params.semesterId = semesterId.toString();
    return this.http.get<any>(`${this.apiUrl}/${libraryId}/books`, { params });
  }

  updateLibraryBooks(libraryId: number, books: { bookId: number; quantity: number }[]): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${libraryId}/books`, { items: books });
  }

  // Backward compat
  executeCompensation(activity: any) {}
  executeRedo(activity: any) {}
}
