import { Injectable, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap, catchError, of } from 'rxjs';
import { Invoice } from '../models/invoice.model';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private http = inject(HttpClient);
  private apiUrl = `${environment.apiUrl}/invoices`;

  private invoicesSignal = signal<Invoice[]>([]);
  public readonly invoices$ = this.invoicesSignal.asReadonly();

  fetchInvoices(filters?: any): void {
    let params: any = {};
    if (filters) {
      if (filters.type) params.type = filters.type;
      if (filters.semesterId) params.semesterId = filters.semesterId;
      if (filters.libraryId) params.libraryId = filters.libraryId;
    }
    this.http.get<any>(this.apiUrl, { params }).pipe(
      tap(res => {
        const data = res.data || res;
        this.invoicesSignal.set(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        console.error('API Error fetching invoices', error);
        return of([]);
      })
    ).subscribe();
  }

  createOrder(order: { libraryId: number; semesterId: number; items: { bookId: number; quantity: number }[] }): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/order`, order).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  createRefund(refund: { libraryId: number; semesterId: number; items: { bookId: number; quantity: number }[] }): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/refund`, refund).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  createClearance(clearance: { libraryId: number; semesterId: number }): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/clearance`, clearance).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  createBatchClearance(semesterId: number): Observable<any> {
    return this.http.post<any>(`${this.apiUrl}/clearance/batch`, { semesterId }).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  getClearancePreview(semesterId: number, libraryId?: number): Observable<any> {
    let params: any = { semesterId: semesterId.toString() };
    if (libraryId) params.libraryId = libraryId.toString();
    return this.http.get<any>(`${this.apiUrl}/clearance/preview`, { params });
  }

  getInvoicesByLibrary(libraryName: string): Invoice[] {
    return this.invoicesSignal().filter(inv => inv.libraryName === libraryName);
  }

  getInvoicesByLibraryId(libraryId: number): Observable<any> {
    return this.http.get<any>(this.apiUrl, { params: { libraryId: libraryId.toString() } });
  }

  updatePrintStatus(id: number, status: string): Observable<any> {
    return this.http.put<any>(`${this.apiUrl}/${id}/print-status`, { printStatus: status }).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  deleteInvoice(id: number): Observable<any> {
    return this.http.delete<any>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  getNextNumber(semesterId: number): Observable<any> {
    return this.http.get<any>(`${this.apiUrl}/next-number`, { params: { semesterId: semesterId.toString() } });
  }

  // backward compat
  saveInvoice(invoice: Invoice) {}
  updateInvoice(updatedInvoice: Invoice) {}
}
