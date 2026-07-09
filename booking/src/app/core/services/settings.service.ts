import { Injectable, signal, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, tap, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PrintSettings {
  brandName: string;
  phones: string;
  mainCurrency: string;
  subCurrency: string;
  term1Start?: string;
  term2Start?: string;
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
  private apiUrl = `${environment.apiUrl}/settings`;
  private semesterUrl = `${environment.apiUrl}/semesters`;

  private readonly STORAGE_KEY = 'printSettings';

  private defaultSettings: PrintSettings = {
    brandName: 'سلسلة تدريبات كامبريدج في الفيزياء',
    phones: 'إدارة المبيعات: هاتف: 91913020 - 98877925',
    mainCurrency: 'R.O.',
    subCurrency: 'Bz'
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
    this.fetchSettings();
    this.fetchSemesters();
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
    this.http.get<any>(this.apiUrl).pipe(
      tap(res => {
        const data = res.data || res;
        if (data) {
          const settings: PrintSettings = {
            brandName: data.brandName || this.defaultSettings.brandName,
            phones: data.phones || this.defaultSettings.phones,
            mainCurrency: data.mainCurrency || this.defaultSettings.mainCurrency,
            subCurrency: data.subCurrency || this.defaultSettings.subCurrency
          };
          this.printSettings.set(settings);
          localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
        }
      }),
      catchError(() => of(null))
    ).subscribe();
  }

  fetchSemesters(): void {
    this.http.get<any>(this.semesterUrl).pipe(
      tap(res => {
        const data = res.data || res;
        if (Array.isArray(data)) {
          this.allSemesters.set(data);
          const active = data.find((s: any) => s.isActive) ?? data[0];
          if (active) this.activeSemester.set(active);
        }
      }),
      catchError(() => of(null))
    ).subscribe();
  }

  getCurrentTerm(): string {
    const active = this.activeSemester();
    return active?.name || 'الأول';
  }

  getActiveTermCode(): string {
    const active = this.activeSemester();
    return active?.code || 'A';
  }

  getActiveSemesterId(): number {
    const active = this.activeSemester();
    return active?.id ?? this.allSemesters().find(s => s.isActive)?.id ?? 0;
  }

  activateSemester(semesterId: number): Observable<any> {
    return this.http.put<any>(`${this.semesterUrl}/${semesterId}/activate`, {}).pipe(
      tap(() => {
        this.allSemesters.update(list =>
          list.map(s => ({ ...s, isActive: s.id === semesterId }))
        );
        const activated = this.allSemesters().find(s => s.id === semesterId);
        if (activated) this.activeSemester.set(activated);
        this.fetchSemesters();
      })
    );
  }

  activateSemesterByCode(code: string): Observable<any> {
    const semester = this.resolveSemesterByCode(code);
    if (!semester) {
      return throwError(() => ({ error: { message: 'الفصل الدراسي غير موجود لهذا العام' } }));
    }
    return this.activateSemester(semester.id);
  }

  startNewYear(startYear: number): Observable<any> {
    return this.http.post<any>(`${this.semesterUrl}/start-new-year`, { startYear }).pipe(
      tap(() => {
        this.fetchSemesters();
      })
    );
  }

  private resolveSemesterByCode(code: string): SemesterInfo | undefined {
    const active = this.activeSemester();
    const yearName = active?.academicYearName;
    const matches = this.allSemesters().filter(s => s.code === code);

    if (yearName) {
      const sameYear = matches.find(s => s.academicYearName === yearName);
      if (sameYear) return sameYear;
    }

    return matches[0];
  }

  /** @deprecated Use activateSemester or activateSemesterByCode */
  setActiveSemester(semesterId: number): void {
    this.activateSemester(semesterId).subscribe();
  }

  updatePrintSettings(settings: PrintSettings) {
    this.printSettings.set(settings);
    localStorage.setItem(this.STORAGE_KEY, JSON.stringify(settings));
    this.http.put(this.apiUrl, settings).subscribe();
  }

  updateSectionOrder(order: string[]) {
    this.sectionOrder.set(order);
    localStorage.setItem(this.ORDER_KEY, JSON.stringify(order));
  }
}
