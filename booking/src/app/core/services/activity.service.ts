import { Injectable, signal, inject } from '@angular/core';
import { Activity, ActivityPayload } from '../models/activity.model';
import { InventoryService } from './inventory.service';
import { LibraryService } from './library.service';
import { InvoiceService } from './invoice.service';
import { ReceiptVoucherService } from './receipt-voucher.service';
import { ToastService } from './toast.service';
import { ConfirmService } from './confirm.service';
import { filter, Observable, throwError } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class ActivityService {
  private activitiesSignal = signal<Activity[]>([]);
  public readonly activities$ = this.activitiesSignal.asReadonly();
  private readonly storageKey = 'activity_log';
  private readonly maxActivities = 50;
  private inventoryService = inject(InventoryService);
  private libraryService = inject(LibraryService);
  private invoiceService = inject(InvoiceService);
  private receiptVoucherService = inject(ReceiptVoucherService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);

  constructor() {
    this.loadActivities(); 
  }

  private loadActivities() {
    const saved = localStorage.getItem(this.storageKey);
    if (saved) {
      try {
        this.activitiesSignal.set(JSON.parse(saved));
      } catch (e) {
        this.activitiesSignal.set([]);
      }
    }
  }

  private generateId(): string {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
    // Fallback for older browsers
    return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, c => {
      const r = Math.random() * 16 | 0;
      return (c === 'x' ? r : (r & 0x3 | 0x8)).toString(16);
    });
  }

  logActivity(action: string, details: string, type?: 'ADD' | 'UPDATE' | 'DELETE' | 'GENERAL', payload?: ActivityPayload) {
    const newActivity: Activity = {
      id: this.generateId(),
      action,
      details,
      timestamp: new Date().toISOString(),
      type,
      payload,
      status: 'active'
    };
    
    const updated = [newActivity, ...this.activitiesSignal()].slice(0, this.maxActivities);
    this.activitiesSignal.set(updated);
    localStorage.setItem(this.storageKey, JSON.stringify(updated));
  }

  private executeCompensation(activity: Activity): Observable<any> {
    if (!activity.type || !activity.payload) return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
    if (activity.payload.entity === 'library') return this.libraryService.executeCompensation(activity);
    if (activity.payload.entity === 'inventory') return this.inventoryService.executeCompensation(activity);
    if (activity.payload.entity === 'invoice') return this.invoiceService.executeCompensation(activity);
    if (activity.payload.entity === 'receipt_voucher') return this.receiptVoucherService.executeCompensation(activity);
    return throwError(() => new Error('لا يمكن التراجع عن هذا النشاط'));
  }

  private executeRedo(activity: Activity): Observable<any> {
    if (!activity.type || !activity.payload) return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
    if (activity.payload.entity === 'library') return this.libraryService.executeRedo(activity);
    if (activity.payload.entity === 'inventory') return this.inventoryService.executeRedo(activity);
    if (activity.payload.entity === 'invoice') return this.invoiceService.executeRedo(activity);
    if (activity.payload.entity === 'receipt_voucher') return this.receiptVoucherService.executeRedo(activity);
    return throwError(() => new Error('لا يمكن إعادة هذا النشاط'));
  }

  undoActivity(activityId: string) {
    const activities = this.activitiesSignal();
    const index = activities.findIndex(a => a.id === activityId);
    if (index === -1 || activities[index].status === 'undone') return;

    const activity = activities[index];
    const isFinancial = activity.type === 'ADD' || activity.type === 'DELETE';
    const doUndo = () => {
      this.executeCompensation(activity).subscribe({
        next: () => {
          const current = this.activitiesSignal();
          const idx = current.findIndex(a => a.id === activityId);
          if (idx === -1 || current[idx].status === 'undone') return;
          const updated = [...current];
          updated[idx] = { ...current[idx], status: 'undone' };
          this.activitiesSignal.set(updated);
          localStorage.setItem(this.storageKey, JSON.stringify(updated));
          this.toast.show(`تم التراجع عن: ${activity.action}`, 'success');
        },
        error: (err) => {
          this.toast.show(err?.message || 'حدث خطأ في التراجع', 'error');
        }
      });
    };

    if (isFinancial) {
      this.confirmService.confirm(
        `⚠️ هل أنت متأكد من التراجع عن العملية التالية؟\n\n"${activity.action}"\n${activity.details}\n\nقد يؤثر ذلك على الأرصدة المالية.`
      ).pipe(filter(result => !!result)).subscribe({ next: () => doUndo() });
    } else {
      doUndo();
    }
  }

  redoActivity(activityId: string) {
    const activities = this.activitiesSignal();
    const index = activities.findIndex(a => a.id === activityId);
    if (index === -1 || activities[index].status !== 'undone') return;

    const activity = activities[index];
    const isFinancial = activity.type === 'ADD' || activity.type === 'DELETE';
    const doRedo = () => {
      this.executeRedo(activity).subscribe({
        next: () => {
          const current = this.activitiesSignal();
          const idx = current.findIndex(a => a.id === activityId);
          if (idx === -1 || current[idx].status !== 'undone') return;
          const updated = [...current];
          updated[idx] = { ...current[idx], status: 'active' };
          this.activitiesSignal.set(updated);
          localStorage.setItem(this.storageKey, JSON.stringify(updated));
          this.toast.show(`تمت إعادة: ${activity.action}`, 'success');
        },
        error: (err) => {
          this.toast.show(err?.message || 'حدث خطأ في الإعادة', 'error');
        }
      });
    };

    if (isFinancial) {
      this.confirmService.confirm(
        `⚠️ هل أنت متأكد من إعادة العملية التالية؟\n\n"${activity.action}"\n${activity.details}\n\nقد يؤثر ذلك على الأرصدة المالية.`
      ).pipe(filter(result => !!result)).subscribe({ next: () => doRedo() });
    } else {
      doRedo();
    }
  }

  clearHistory() {
    this.activitiesSignal.set([]);
    localStorage.removeItem(this.storageKey);
  }
}
