import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';
import { catchError, tap } from 'rxjs/operators';
import { ToastService } from './toast.service';
import { environment } from '../../../environments/environment';
import { ApiResponse } from '../models/api-response.model';

export interface PrintSettings {
  brandName: string;
  phones: string;
  mainCurrency: string;
  subCurrency: string;
  ownerSignatureName: string;
  whatsappNumber: string;
}

export interface SemesterInfo {
  id: number;
  name: string;
  code: string;
  academicYearName: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private http = inject(HttpClient);
  private toast = inject(ToastService);
  private apiUrl = `${environment.apiUrl}/settings`;
  private semesterUrl = `${environment.apiUrl}/semesters`;

  private readonly STORAGE_KEY = 'printSettings';

  private defaultSettings: PrintSettings = {
    brandName: 'سلسلة تدريبات كامبريدج في الفيزياء',
    phones: 'إدارة المبيعات: هاتف: 91913020 - 98877925',
    mainCurrency: 'R.O.',
    subCurrency: 'Bz',
    ownerSignatureName: 'مدحت محمد عبد الستار',
    whatsappNumber: '91913020'
  };

  printSettings = signal<PrintSettings>(this.loadSettings());
  activeSemester = signal<SemesterInfo | null>(null);
  allSemesters = signal<SemesterInfo[]>([]);

  private readonly ORDER_KEY = 'sectionOrder';
  private defaultOrder = ['inv-form', 'inv-list', 'lib-form', 'dashboard', 'lib-list', 'inventory'];
  sectionOrder = signal<string[]>(this.loadOrder());

  constructor() {
    this.fetchSettings();
  }

  reloadAfterAuth(): void {
    this.fetchSettingsFull();
    this.fetchSemesters();
  }

  fetchSettingsFull(): void {
    this.http.get<ApiResponse<Record<string, unknown>>>(`${this.apiUrl}/full`).pipe(
      tap(res => {
        const data = res.data as unknown as Record<string, string>;
        if (data) {
          const settings: PrintSettings = {
            brandName: data['brandName'] || this.defaultSettings.brandName,
            phones: data['phones'] || this.defaultSettings.phones,
            mainCurrency: data['mainCurrency'] || this.defaultSettings.mainCurrency,
            subCurrency: data['subCurrency'] || this.defaultSettings.subCurrency,
            ownerSignatureName: data['ownerSignatureName'] || this.defaultSettings.ownerSignatureName,
            whatsappNumber: data['whatsappNumber'] || this.defaultSettings.whatsappNumber
          };
          this.printSettings.set(settings);
          localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
        }
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل الإعدادات', 'error');
        return of(null);
      })
    ).subscribe();
  }

  private loadSettings(): PrintSettings {
    const saved = localStorage.getItem(this.STORAGE_KEY);
    if (saved) {
      try { return JSON.parse(saved); } catch (e) {}
    }
    return this.defaultSettings;
  }

  private loadOrder(): string[] {
    const saved = localStorage.getItem(this.ORDER_KEY);
    if (saved) {
      try {
        let arr = JSON.parse(saved);
        if (arr.includes('invoices') || arr.includes('libraries')) arr = this.defaultOrder;
        return arr;
      } catch (e) {}
    }
    return this.defaultOrder;
  }

  fetchSettings(): void {
    // Public endpoint returns only branding (safe for login page)
    this.http.get<ApiResponse<Record<string, unknown>>>(this.apiUrl).pipe(
      tap(res => {
        const data = res.data as unknown as Record<string, string>;
        if (data) {
          const settings: PrintSettings = {
            brandName: data['brandName'] || this.defaultSettings.brandName,
            phones: data['phones'] || this.defaultSettings.phones,
            mainCurrency: data['mainCurrency'] || this.defaultSettings.mainCurrency,
            subCurrency: data['subCurrency'] || this.defaultSettings.subCurrency,
            ownerSignatureName: data['ownerSignatureName'] || this.defaultSettings.ownerSignatureName,
            whatsappNumber: data['whatsappNumber'] || this.defaultSettings.whatsappNumber
          };
          this.printSettings.set(settings);
          localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
        }
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل الإعدادات', 'error');
        return of(null);
      })
    ).subscribe();
  }

  fetchSemesters(): void {
    this.http.get<ApiResponse<SemesterInfo[]>>(this.semesterUrl).pipe(
      tap(res => {
        const data = res.data;
        if (Array.isArray(data)) {
          this.allSemesters.set(data);
          const active = data.find((s: SemesterInfo) => s.isActive) ?? data[0];
          if (active) this.activeSemester.set(active);
        }
      }),
      catchError(error => {
        this.toast.show('تعذر تحميل الفصول الدراسية', 'error');
        return of(null);
      })
    ).subscribe();
  }

  getCurrentTerm(): string {
    const active = this.activeSemester();
    return active?.name || 'الفصل الأول';
  }

  getActiveTermCode(): string {
    const active = this.activeSemester();
    return active?.code || 'A';
  }

  getActiveSemesterId(): number | null {
    const active = this.activeSemester();
    return active?.id ?? this.allSemesters().find(s => s.isActive)?.id ?? null;
  }

  activateSemester(semesterId: number): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(`${this.semesterUrl}/${semesterId}/activate`, {}).pipe(
    tap(() => {
      this.allSemesters.update(list =>
        list.map(s => ({ ...s, isActive: s.id === semesterId }))
      );
      const activated = this.allSemesters().find(s => s.id === semesterId);
      if (activated) this.activeSemester.set(activated);
    })
    );
  }

  activateSemesterByCode(code: string): Observable<ApiResponse<unknown>> {
    const semester = this.resolveSemesterByCode(code);
    if (!semester) {
      return throwError(() => ({ error: { message: 'الفصل الدراسي غير موجود لهذا العام' } }));
    }
    return this.activateSemester(semester.id);
  }

  startNewYear(startYear: number): Observable<ApiResponse<unknown>> {
    return this.http.post<ApiResponse<unknown>>(`${this.semesterUrl}/start-new-year`, { startYear }).pipe(
      tap(() => {
        this.fetchSemesters();
      })
    );
  }

  private resolveSemesterByCode(code: string): SemesterInfo | undefined {
    const matches = this.allSemesters().filter(s => s.code === code);
    if (matches.length === 0) return undefined;

    const active = this.activeSemester();
    const yearName = active?.academicYearName;
    if (yearName) {
      const sameYear = matches.find(s => s.academicYearName === yearName);
      if (sameYear) return sameYear;
    }

    return matches.sort((a, b) => b.academicYearName.localeCompare(a.academicYearName))[0];
  }

  updatePrintSettings(settings: PrintSettings): Observable<ApiResponse<unknown>> {
    return this.http.put<ApiResponse<unknown>>(this.apiUrl, settings).pipe(
      tap(() => {
        this.printSettings.set(settings);
        localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
      }),
      catchError(error => {
        this.toast.show('تعذر حفظ إعدادات الطباعة', 'error');
        return throwError(() => error);
      })
    );
  }

  updateSectionOrder(order: string[]) {
    this.sectionOrder.set(order);
    localStorage.setItem(this.ORDER_KEY, JSON.stringify(order));
  }
}
