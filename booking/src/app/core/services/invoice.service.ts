import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap, map, catchError, of, throwError } from 'rxjs';
import { Invoice, ClearancePreview } from '../models/invoice.model';
import { ApiResponse } from '../models/api-response.model';
import { ActivityPayload } from '../models/activity.model';
import { ToastService } from './toast.service';
import { InventoryService } from './inventory.service';
import { environment } from '../../../environments/environment';

interface CreateOrderRequest {
  libraryId: number;
  semesterId: number;
  items: { bookId: number; quantity: number }[];
}

interface CreateRefundRequest {
  libraryId: number;
  semesterId: number;
  items: { bookId: number; quantity: number }[];
}

interface CreateClearanceRequest {
  libraryId: number;
  semesterId: number;
}

interface CreateBatchClearanceRequest {
  semesterId: number;
}

interface NextNumberResponse {
  nextNumber: number;
  termCode: string;
  displayNumber: string;
}

@Injectable({ providedIn: 'root' })
export class InvoiceService {
  private http = inject(HttpClient);
  private toast = inject(ToastService);
  private inventoryService = inject(InventoryService);
  private apiUrl = `${environment.apiUrl}/invoices`;

  private invoicesSignal = signal<Invoice[]>([]);
  public readonly invoices$ = this.invoicesSignal.asReadonly();

  fetchInvoices(filters?: { type?: string; semesterId?: number; libraryId?: number }): void {
    let params = new HttpParams();
    if (filters) {
      if (filters.type) params = params.set('type', filters.type);
      if (filters.semesterId) params = params.set('semesterId', filters.semesterId);
      if (filters.libraryId) params = params.set('libraryId', filters.libraryId);
    }
    this.http.get<ApiResponse<Invoice[]>>(this.apiUrl, { params }).pipe(
      tap(res => {
        const data = res.data;
        this.invoicesSignal.set(Array.isArray(data) ? data : []);
      }),
      catchError(() => {
        this.toast.show('تعذر تحميل الفواتير', 'error');
        return of([]);
      })
    ).subscribe();
  }

  private prependInvoice(inv: Invoice): void {
    this.invoicesSignal.set([inv, ...this.invoicesSignal()]);
  }

  private replaceInvoice(id: number, inv: Invoice): void {
    this.invoicesSignal.set(
      this.invoicesSignal().map(i => i.id === id ? inv : i)
    );
  }

  private removeInvoice(id: number): void {
    this.invoicesSignal.set(
      this.invoicesSignal().filter(i => i.id !== id)
    );
  }

  createOrder(order: CreateOrderRequest): Observable<ApiResponse<Invoice>> {
    return this.http.post<ApiResponse<Invoice>>(`${this.apiUrl}/order`, order).pipe(
      tap(res => {
        const created = res.data;
        if (created?.id) this.prependInvoice(created);
      })
    );
  }

  createRefund(refund: CreateRefundRequest): Observable<ApiResponse<Invoice>> {
    return this.http.post<ApiResponse<Invoice>>(`${this.apiUrl}/refund`, refund).pipe(
      tap(res => {
        const created = res.data;
        if (created?.id) this.prependInvoice(created);
      })
    );
  }

  createClearance(clearance: CreateClearanceRequest): Observable<ApiResponse<Invoice>> {
    return this.http.post<ApiResponse<Invoice>>(`${this.apiUrl}/clearance`, clearance).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  createBatchClearance(semesterId: number): Observable<ApiResponse<{ count: number; totalAmount: number; invoices: Invoice[] }>> {
    return this.http.post<ApiResponse<{ count: number; totalAmount: number; invoices: Invoice[] }>>(`${this.apiUrl}/clearance/batch`, { semesterId } as CreateBatchClearanceRequest).pipe(
      tap(() => this.fetchInvoices())
    );
  }

  getClearancePreview(semesterId: number, libraryId?: number): Observable<ApiResponse<ClearancePreview>> {
    let params = new HttpParams().set('semesterId', semesterId.toString());
    if (libraryId) params = params.set('libraryId', libraryId.toString());
    return this.http.get<ApiResponse<ClearancePreview>>(`${this.apiUrl}/clearance/preview`, { params });
  }

  getInvoicesByLibrary(libraryName: string): Invoice[] {
    return this.invoicesSignal().filter(inv => inv.libraryName === libraryName);
  }

  getInvoicesByLibraryId(libraryId: number): Observable<ApiResponse<Invoice[]>> {
    return this.http.get<ApiResponse<Invoice[]>>(this.apiUrl, { params: new HttpParams().set('libraryId', libraryId.toString()) });
  }

  updatePrintStatus(id: number, status: string): Observable<ApiResponse<Invoice>> {
    return this.http.put<ApiResponse<Invoice>>(`${this.apiUrl}/${id}/print-status`, { printStatus: status }).pipe(
      tap(res => {
        const updated = res.data;
        if (updated?.id) this.replaceInvoice(id, updated);
      })
    );
  }

  deleteInvoice(id: number): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.removeInvoice(id))
    );
  }

  getNextNumber(semesterId: number): Observable<ApiResponse<NextNumberResponse>> {
    return this.http.get<ApiResponse<NextNumberResponse>>(`${this.apiUrl}/next-number`, { params: new HttpParams().set('semesterId', semesterId.toString()) });
  }

  executeCompensation(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload || !payload.id || payload.entity !== 'invoice') {
      return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    }

    if (activity.type === 'ADD') {
      return this.deleteInvoice(payload.id).pipe(
        tap(() => this.inventoryService.fetchBooks()),
        map(() => undefined)
      );
    }
    if (activity.type === 'DELETE') {
      this.toast.show('لا يمكن استعادة الفاتورة المحذوفة عبر التراجع', 'error');
      return throwError(() => new Error('لا يمكن استعادة الفاتورة المحذوفة'));
    }
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }

  executeRedo(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload || !payload.id || payload.entity !== 'invoice') {
      return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    }

    if (activity.type === 'DELETE') {
      return this.deleteInvoice(payload.id).pipe(
        tap(() => this.inventoryService.fetchBooks()),
        map(() => undefined)
      );
    }
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }
}
