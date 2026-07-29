import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  effect,
  inject,
  Input,
  signal,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { HttpErrorResponse } from '@angular/common/http';
import { InvoiceService } from '../../core/services/invoice.service';
import { SettingsService } from '../../core/services/settings.service';
import { ToastService } from '../../core/services/toast.service';
import { DashboardData } from '../../core/models/invoice.model';
import {
  LS_DASH_ANALYSIS_COLLAPSED,
  LS_DASH_CLASSIC_MODE,
} from '../../core/constants/local-storage-keys';

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.html',
})
export class DashboardComponent {
  @Input() isCompact = false;

  trackById = (index: number, item: any) => item.id ?? index;
  trackByIndex = (index: number) => index;

  private readonly invoicesService = inject(InvoiceService);
  public readonly settingsService = inject(SettingsService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  isAnalysisCollapsed = signal(localStorage.getItem(LS_DASH_ANALYSIS_COLLAPSED) === 'true');
  isClassicMode = signal(localStorage.getItem(LS_DASH_CLASSIC_MODE) !== 'false');
  classicDisplayMode = signal<'revenue' | 'quantity'>('revenue');
  filterTermCode = signal<string>('');
  dashboardData = signal<DashboardData | null>(null);

  classicUnitLabel = computed(() =>
    this.classicDisplayMode() === 'revenue'
      ? this.settingsService.printSettings().mainCurrency
      : 'كتاب',
  );

  constructor() {
    effect(() => {
      this.filterTermCode.set(this.settingsService.getActiveTermCode());
    });

    effect(() => {
      const activeSemester = this.settingsService.activeSemester();
      const termCode = this.filterTermCode();
      const targetSemester = this.settingsService
        .allSemesters()
        .find(
          (semester) =>
            semester.academicYearId === activeSemester?.academicYearId &&
            (!termCode || semester.code === termCode),
        );

      const filters =
        termCode && targetSemester
          ? { semesterId: targetSemester.id }
          : activeSemester?.academicYearId
            ? { academicYearId: activeSemester.academicYearId }
            : undefined;

      if (!filters) {
        this.dashboardData.set(null);
        return;
      }

      this.invoicesService
        .getDashboardAnalytics(filters)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (response) => this.dashboardData.set(response.data ?? null),
          error: () => this.dashboardData.set(null),
        });
    });
  }

  onTermCodeChange(code: string): void {
    this.settingsService
      .activateSemesterByCode(code)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.toast.show('تم تنشيط الفصل الدراسي', 'success'),
        error: (error: HttpErrorResponse) =>
          this.toast.show(error.error?.message || 'تعذر تغيير الفصل الدراسي', 'error'),
      });
  }

  toggleClassicMode(event: Event): void {
    event.stopPropagation();
    this.isClassicMode.set(!this.isClassicMode());
    localStorage.setItem(LS_DASH_CLASSIC_MODE, String(this.isClassicMode()));
  }

  toggleAnalysis(): void {
    this.isAnalysisCollapsed.set(!this.isAnalysisCollapsed());
    localStorage.setItem(LS_DASH_ANALYSIS_COLLAPSED, String(this.isAnalysisCollapsed()));
  }

  stats = computed(() => {
    const data = this.dashboardData();
    return {
      totalLibraries: data?.totalLibraries ?? 0,
      totalItems: data?.totalItems ?? 0,
      lowStockCount: data?.lowStockCount ?? 0,
      totalInvoices: (data?.orderCount ?? 0) + (data?.refundCount ?? 0),
      totalRevenue: data?.totalRevenue ?? 0,
      totalCollected: data?.totalCollected ?? 0,
      totalItemsSold: data?.totalItemsSold ?? 0,
    };
  });

  pendingBalances = computed(() =>
    (this.dashboardData()?.libraryBalances ?? [])
      .filter((library) => library.balance > 0)
      .slice(0, 5)
      .map((library) => ({ name: library.libraryName, balance: library.balance })),
  );

  criticalStock = computed(() =>
    (this.dashboardData()?.criticalStock ?? [])
      .slice(0, 5)
      .map((book) => ({ name: book.name, remaining: book.stockQuantity, demand: book.demand })),
  );

  mostRefunded = computed(() => this.dashboardData()?.mostRefunded ?? []);

  chartData = computed(() => {
    const data = this.dashboardData()?.salesByTerm ?? [];
    const maxRevenue =
      data.length > 0 ? Math.max(...data.map((item) => Math.max(item.revenue, 0)), 1) : 1;
    const colors = [
      'bg-primary hover:bg-primary-container',
      'bg-info hover:bg-info/80',
      'bg-warning/80 hover:bg-warning',
      'bg-success/80 hover:bg-success',
    ];
    const formatter = new Intl.NumberFormat('ar-SA', { notation: 'compact' });
    const bars = data.map((item, index) => {
      const revenue = Math.max(item.revenue, 0);
      return {
        term: item.termName,
        revenue,
        heightPercent: Math.max((revenue / maxRevenue) * 95, 5),
        colorClass: colors[index % colors.length],
      };
    });

    return {
      bars,
      label100: formatter.format(Math.round(maxRevenue)),
      label50: formatter.format(Math.round(maxRevenue * 0.5)),
      label0: '0',
      hasData: bars.length > 0,
    };
  });

  classicChartData = computed(() => {
    const mode = this.classicDisplayMode();
    const data = (this.dashboardData()?.classicSalesByYear ?? []).map((item) => ({
      year: item.academicYear,
      value: Math.max(mode === 'revenue' ? item.revenue : item.quantity, 0),
    }));
    const maxValue = data.length > 0 ? Math.max(...data.map((item) => item.value), 1) : 1;
    const colors = ['bg-[#C6D2FD]', 'bg-[#3A7CF6]', 'bg-[#002060]'];

    return {
      bars: data.map((item, index) => ({
        ...item,
        heightPercent: Math.max((item.value / maxValue) * 90, 5),
        colorClass: colors[index % colors.length],
      })),
      hasData: data.length > 0,
    };
  });

  classicTableData = computed(() => {
    const mode = this.classicDisplayMode();
    return (this.dashboardData()?.classicRows ?? []).map((row) => ({
      year: row.academicYear,
      term: row.termName,
      bestLibrary: mode === 'revenue' ? row.bestLibraryByRevenue : row.bestLibraryByQuantity,
      libraryCount: row.libraryCount,
      ordered: row.ordered,
      refunded: row.refunded,
      netSales: mode === 'revenue' ? row.netRevenue : row.netQuantity,
    }));
  });
}
