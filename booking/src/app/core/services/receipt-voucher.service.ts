import { Injectable, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, tap, catchError, of, map, throwError } from 'rxjs';
import { ReceiptVoucher, CreateReceiptVoucher } from '../models/receipt-voucher.model';
import { ActivityPayload } from '../models/activity.model';
import { ApiResponse } from '../models/api-response.model';
import { ToastService } from './toast.service';
import { environment } from '../../../environments/environment';

@Injectable({ providedIn: 'root' })
export class ReceiptVoucherService {
  private http = inject(HttpClient);
  private toast = inject(ToastService);
  private apiUrl = `${environment.apiUrl}/receipt-vouchers`;

  private vouchersSignal = signal<ReceiptVoucher[]>([]);
  public readonly vouchers$ = this.vouchersSignal.asReadonly();

  fetchVouchers(libraryId?: number, semesterId?: number): void {
    let params = new HttpParams();
    if (libraryId) params = params.set('libraryId', libraryId.toString());
    if (semesterId) params = params.set('semesterId', semesterId.toString());
    this.http.get<ApiResponse<ReceiptVoucher[]>>(this.apiUrl, { params }).pipe(
      tap(res => {
        const data = res.data;
        this.vouchersSignal.set(Array.isArray(data) ? data : []);
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل سندات القبض', 'error');
        return of([]);
      })
    ).subscribe();
  }

  private prependVoucher(v: ReceiptVoucher): void {
    this.vouchersSignal.set([v, ...this.vouchersSignal()]);
  }

  private removeVoucher(id: number): void {
    this.vouchersSignal.set(
      this.vouchersSignal().filter(v => v.id !== id)
    );
  }

  create(voucher: CreateReceiptVoucher): Observable<ApiResponse<ReceiptVoucher>> {
    return this.http.post<ApiResponse<ReceiptVoucher>>(this.apiUrl, voucher).pipe(
      tap(res => {
        const created = res.data;
        if (created?.id) this.prependVoucher(created);
      })
    );
  }

  getByLibraryId(libraryId: number): Observable<ApiResponse<ReceiptVoucher[]>> {
    return this.http.get<ApiResponse<ReceiptVoucher[]>>(this.apiUrl, { params: { libraryId: libraryId.toString() } });
  }

  delete(id: number): Observable<ApiResponse<unknown>> {
    return this.http.delete<ApiResponse<unknown>>(`${this.apiUrl}/${id}`).pipe(
      tap(() => this.removeVoucher(id))
    );
  }

  restoreVoucher(id: number): Observable<any> {
    return this.http.post(`${this.apiUrl}/${id}/restore`, {});
  }

  executeCompensation(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload || payload.entity !== 'receipt_voucher') {
      return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    }

    if (activity.type === 'ADD' && payload.id) {
      return this.delete(payload.id).pipe(map(() => undefined));
    }
    if (activity.type === 'DELETE') {
      if (payload.id) {
        return this.restoreVoucher(payload.id).pipe(map(() => undefined));
      }
      return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    }
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }

  executeRedo(activity: { type?: string; payload?: ActivityPayload }): Observable<any> {
    const payload = activity?.payload;
    if (!payload || !payload.id || payload.entity !== 'receipt_voucher') {
      return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    }

    if (activity.type === 'ADD') {
      return this.restoreVoucher(payload.id).pipe(map(() => undefined));
    }
    if (activity.type === 'DELETE') {
      return this.delete(payload.id).pipe(map(() => undefined));
    }
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }
}
