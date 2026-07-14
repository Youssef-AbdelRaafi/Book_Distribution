import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { BehaviorSubject, Observable, catchError, of, tap, map, throwError } from 'rxjs';
import { Library, Governorate } from '../models/library.model';
import { ApiResponse } from '../models/api-response.model';
import { ToastService } from './toast.service';
import { environment } from '../../../environments/environment';

import { ActivityPayload } from '../models/activity.model';

@Injectable({ providedIn: 'root' })
export class LibraryService {
  private http = inject(HttpClient);
  private toast = inject(ToastService);
  private apiUrl = `${environment.apiUrl}/libraries`;
  private govUrl = `${environment.apiUrl}/governorates`;

  private librariesSubject = new BehaviorSubject<Library[]>([]);
  public libraries$ = this.librariesSubject.asObservable();

  governorates = signal<Governorate[]>([]);

  fetchLibraries(): void {
    this.http.get<ApiResponse<Library[]>>(this.apiUrl).pipe(
      tap(res => {
        this.librariesSubject.next(Array.isArray(res.data) ? res.data : []);
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل المكتبات', 'error');
        return of({ data: [], success: false } as ApiResponse<Library[]>);
      })
    ).subscribe();
  }

  fetchGovernorates(): void {
    this.http.get<ApiResponse<Governorate[]>>(this.govUrl).pipe(
      tap(res => {
        this.governorates.set(Array.isArray(res.data) ? res.data : []);
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل المحافظات', 'error');
        return of({ data: [], success: false } as ApiResponse<Governorate[]>);
      })
    ).subscribe();
  }

  private prependLibrary(lib: Library): void {
    this.librariesSubject.next([lib, ...this.librariesSubject.value]);
  }

  private replaceLibrary(id: number, lib: Library): void {
    this.librariesSubject.next(
      this.librariesSubject.value.map(l => l.id === id ? lib : l)
    );
  }

  private removeLibrary(id: number): void {
    this.librariesSubject.next(
      this.librariesSubject.value.filter(l => l.id !== id)
    );
  }

  addLibrary(lib: Partial<Library>): Observable<ApiResponse<Library>> {
    return this.http.post<ApiResponse<Library>>(this.apiUrl, lib).pipe(
      tap(res => {
        const created = res.data;
        if (created?.id) this.prependLibrary(created);
      })
    );
  }

  updateLibrary(id: number, lib: Partial<Library>): Observable<ApiResponse<Library>> {
    return this.http.put<ApiResponse<Library>>(`${this.apiUrl}/${id}`, lib).pipe(
      tap(res => {
        const updated = res.data;
        if (updated?.id) this.replaceLibrary(id, updated);
      })
    );
  }

  updateRating(id: number, rating: { responseRating?: string; paymentRating?: string; notes?: string }): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(`${this.apiUrl}/${id}/rating`, rating).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  deleteLibrary(id: number): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.removeLibrary(id))
    );
  }

  getLibraryBooks(libraryId: number, semesterId?: number): Observable<ApiResponse<unknown>> {
    let params = new HttpParams();
    if (semesterId) params = params.set('semesterId', semesterId.toString());
    return this.http.get<ApiResponse<unknown>>(`${this.apiUrl}/${libraryId}/books`, { params });
  }

  updateLibraryBooks(libraryId: number, books: { bookId: number; quantity: number }[]): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(`${this.apiUrl}/${libraryId}/books`, { items: books });
  }

  uploadLogo(id: number, file: File): Observable<ApiResponse<unknown>> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ApiResponse<unknown>>(`${this.apiUrl}/${id}/logo`, formData).pipe(
      tap(() => this.fetchLibraries())
    );
  }

  executeCompensation(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    if (activity.type === 'ADD' && payload.id) {
      return this.deleteLibrary(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload) {
      return this.addLibrary(payload as unknown as Library).pipe(map(() => undefined));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.previous) {
      return this.updateLibrary(payload.id, payload.previous as unknown as Partial<Library>).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }
  executeRedo(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload) return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    if (activity.type === 'ADD' && payload) {
      return this.addLibrary(payload as unknown as Library).pipe(map(() => undefined));
    } else if (activity.type === 'DELETE' && payload?.id) {
      return this.deleteLibrary(payload.id).pipe(map(() => undefined));
    } else if (activity.type === 'UPDATE' && payload?.id && payload?.current) {
      return this.updateLibrary(payload.id, payload.current as unknown as Partial<Library>).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }
}
