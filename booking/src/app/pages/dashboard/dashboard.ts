import { Component, inject, Input, computed, signal, effect, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { toSignal, takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { InventoryService } from '../../core/services/inventory.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { ReceiptVoucherService } from '../../core/services/receipt-voucher.service';
import { LibraryService } from '../../core/services/library.service';
import { SettingsService } from '../../core/services/settings.service';
import { ToastService } from '../../core/services/toast.service';
import { Invoice } from '../../core/models/invoice.model';
import { Book } from '../../core/models/inventory.model';
import { LS_DASH_ANALYSIS_COLLAPSED, LS_DASH_CLASSIC_MODE } from '../../core/constants/local-storage-keys';

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './dashboard.html'
})
export class DashboardComponent {
  @Input() isCompact = false;
  trackById = (i: number, item: any) => item?.id ?? i;
  trackByIndex = (i: number) => i;
  private inventoryService = inject(InventoryService);
  private invoicesService = inject(InvoiceService);
  private receiptVoucherService = inject(ReceiptVoucherService);
  private libraryService = inject(LibraryService);
  public settingsService = inject(SettingsService);
  private toast = inject(ToastService);
  private destroyRef = inject(DestroyRef);

  classicUnitLabel = computed(() => {
    const mode = this.classicDisplayMode();
    if (mode === 'revenue') return this.settingsService.printSettings().mainCurrency;
    if (mode === 'quantity') return 'كتاب';
    return 'مكتبة';
  });

  onTermCodeChange(code: string) {
    this.settingsService.activateSemesterByCode(code).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => this.toast.show('تم تنشيط الفصل الدراسي', 'success'),
      error: (err) => this.toast.show(err.error?.message || 'تعذر تغيير الفصل الدراسي', 'error')
    });
  }

  isAnalysisCollapsed = signal(localStorage.getItem(LS_DASH_ANALYSIS_COLLAPSED) === 'true');
  isClassicMode = signal(localStorage.getItem(LS_DASH_CLASSIC_MODE) !== 'false');
  classicDisplayMode = signal<'libraries' | 'revenue' | 'quantity'>('libraries');
  private readonly LOW_STOCK_THRESHOLD = 150;
  private readonly CRITICAL_STOCK_THRESHOLD = 200;

  filterTermCode = signal<string>('');

  filteredInvoices = computed(() => {
    const term = this.filterTermCode();
    if (!term) return this.invoices();
    return this.invoices().filter(inv => inv.termCode === term);
  });

  constructor() {
    effect(() => {
      this.filterTermCode.set(this.settingsService.getActiveTermCode());
    });
  }

  toggleClassicMode(event: Event) {
    event.stopPropagation();
    this.isClassicMode.set(!this.isClassicMode());
    localStorage.setItem(LS_DASH_CLASSIC_MODE, String(this.isClassicMode()));
  }

  // Convert Observables to Signals
  private inventory = toSignal(this.inventoryService.inventory$, { initialValue: [] });
  private invoices = this.invoicesService.invoices$;
  private libraries = toSignal(this.libraryService.libraries$, { initialValue: [] });

  toggleAnalysis() {
    this.isAnalysisCollapsed.set(!this.isAnalysisCollapsed());
    localStorage.setItem(LS_DASH_ANALYSIS_COLLAPSED, String(this.isAnalysisCollapsed()));
  }

  // 1. Top Stats
  stats = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const vouchers = this.receiptVoucherService.vouchers$();
    const invt: Book[] = this.inventory();
    const libs = this.libraries();

    let totalRevenue = 0;
    let totalCollected = 0;
    let totalItemsSold = 0;

    invs.forEach(inv => {
      let invTotal = 0;
      let invQty = 0;
      inv.items?.forEach(item => {
        invTotal += (item.total || 0);
        invQty += (item.quantity || 0);
      });

      if (inv.type === 'order') {
        totalRevenue += invTotal;
        totalItemsSold += invQty;
      } else if (inv.type === 'refund') {
        totalRevenue -= invTotal;
        totalItemsSold -= invQty;
      } else if (inv.type === 'clearance') {
        totalCollected += inv.totalAmount || 0;
      }
    });

    // Add receipt voucher amounts to collected
    vouchers.forEach(v => {
      totalCollected += v.amount || 0;
    });

    return {
      totalLibraries: libs.length,
      totalItems: invt.reduce((sum, item) => sum + (item.stockQuantity || 0), 0),
      lowStockCount: invt.filter(item => (item.stockQuantity || 0) < this.LOW_STOCK_THRESHOLD).length,
      totalInvoices: invs.length,
      totalRevenue,
      totalCollected,
      totalItemsSold
    };
  });

  // 2. Pending Balances (أين أموالي؟)
  pendingBalances = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const vouchers = this.receiptVoucherService.vouchers$();
    const libMap = new Map<string, { ordered: number, refunded: number, cleared: number, balance: number }>();

    invs.forEach(inv => {
      if (!inv.libraryName) return;
      if (!libMap.has(inv.libraryName)) libMap.set(inv.libraryName, { ordered: 0, refunded: 0, cleared: 0, balance: 0 });
      
      const stats = libMap.get(inv.libraryName)!;
      const invTotal = inv.totalAmount || 0;
      
      if (inv.type === 'order') stats.ordered += invTotal;
      else if (inv.type === 'refund') stats.refunded += invTotal;
      else if (inv.type === 'clearance') stats.cleared += invTotal;
    });

    // Subtract receipt voucher amounts from each library's balance
    vouchers.forEach(v => {
      const libName = v.libraryName || '';
      if (!libName || !libMap.has(libName)) return;
      const stats = libMap.get(libName)!;
      stats.cleared += v.amount || 0;
    });

    return Array.from(libMap.entries())
      .map(([name, stats]) => ({
        name,
        balance: stats.ordered - stats.refunded - stats.cleared
      }))
      .filter(lib => lib.balance > 0)
      .sort((a, b) => b.balance - a.balance)
      .slice(0, 5);
  });

  // 3. Critical Stock (ماذا يجب أن أطبع؟)
  criticalStock = computed(() => {
    const invt: Book[] = this.inventory();
    const invs: Invoice[] = this.filteredInvoices();
    
    // Calculate total sold for each item to know its demand
    const demandMap = new Map<number, number>();
    invs.forEach(inv => {
      if (inv.type === 'order' && inv.items) {
        inv.items.forEach(item => {
          demandMap.set(item.bookId, (demandMap.get(item.bookId) || 0) + (item.quantity || 0));
        });
      }
    });

    return invt
      .filter(item => (item.stockQuantity || 0) < this.CRITICAL_STOCK_THRESHOLD)
      .map(item => ({
        name: item.name,
        remaining: item.stockQuantity || 0,
        demand: demandMap.get(item.id) || 0
      }))
      .sort((a, b) => a.remaining - b.remaining) // Sort by lowest remaining first
      .slice(0, 5);
  });

  // 4. Most Refunded (ما هي المشكلة؟)
  mostRefunded = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const refundMap = new Map<string, number>();

    invs.forEach(inv => {
      if (inv.type === 'refund' && inv.items) {
        inv.items.forEach(item => {
          const key = item.bookName || 'غير محدد';
          refundMap.set(key, (refundMap.get(key) || 0) + (item.quantity || 0));
        });
      }
    });

    return Array.from(refundMap.entries())
      .map(([name, count]) => ({ name, count }))
      .sort((a, b) => b.count - a.count)
      .slice(0, 5);
  });

  // 5. Chart Data: Sales by Term (متى نبيع أكثر؟)
  chartData = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const termMap = new Map<string, number>();

    invs.forEach(inv => {
      const term = inv.termCode || 'A';
      inv.items?.forEach(item => {
        if (inv.type === 'order') termMap.set(term, (termMap.get(term) || 0) + (item.total || 0));
        if (inv.type === 'refund') termMap.set(term, (termMap.get(term) || 0) - (item.total || 0));
      });
    });

    const data = Array.from(termMap.entries())
      .map(([term, revenue]) => ({ term: term === 'A' ? 'الترم الأول' : 'الترم الثاني', revenue: Math.max(revenue, 0) }))
      .sort((a, b) => b.revenue - a.revenue);

    const maxRevenue = Math.max(...data.map(d => d.revenue), 1);

    const colors = ['bg-primary hover:bg-primary-container', 'bg-info hover:bg-info/80', 'bg-warning/80 hover:bg-warning', 'bg-success/80 hover:bg-success'];

    const bars = data.map((d, index) => ({
      term: d.term,
      revenue: d.revenue,
      heightPercent: Math.max((d.revenue / maxRevenue) * 95, 5),
      colorClass: colors[index % colors.length]
    }));

    const formatter = new Intl.NumberFormat('ar-SA', { notation: 'compact' });

    return {
      bars,
      label100: formatter.format(Math.round(maxRevenue)),
      label50: formatter.format(Math.round(maxRevenue * 0.5)),
      label0: '0',
      hasData: bars.length > 0
    };
  });

  // --- Classic Analytics Mode Data ---
  
  classicChartData = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const mode = this.classicDisplayMode();
    const yearMap = new Map<number, number | Set<string>>();

    invs.forEach(inv => {
      const sem = this.settingsService.allSemesters().find(s => s.id === inv.semesterId);
      const yearStr = sem?.academicYearName ? sem.academicYearName.split('-')[0] : '';
      const year = yearStr ? parseInt(yearStr, 10) : new Date(inv.date || new Date()).getFullYear();
      
      if (mode === 'libraries') {
        if (!yearMap.has(year)) yearMap.set(year, new Set<string>());
        (yearMap.get(year) as Set<string>).add(inv.libraryName || 'غير محدد');
      } else {
        let val = 0;
        inv.items?.forEach(item => {
          if (mode === 'revenue') {
            if (inv.type === 'order') val += (item.total || 0);
            if (inv.type === 'refund') val -= (item.total || 0);
          } else if (mode === 'quantity') {
            if (inv.type === 'order') val += (item.quantity || 0);
            if (inv.type === 'refund') val -= (item.quantity || 0);
          }
        });
        yearMap.set(year, (yearMap.get(year) as number || 0) + val);
      }
    });

    const data = Array.from(yearMap.entries())
      .sort((a, b) => a[0] - b[0])
      .map(([year, value]) => {
        const val = mode === 'libraries' ? (value as Set<string>).size : Math.max((value as number), 0);
        return { year, value: val };
      });

    const maxValue = Math.max(...data.map(d => d.value), 1);
    
    const colors = ['bg-[#C6D2FD]', 'bg-[#3A7CF6]', 'bg-[#002060]'];

    const bars = data.map((d, index) => ({
      year: d.year,
      value: d.value,
      heightPercent: Math.max((d.value / maxValue) * 90, 5),
      colorClass: colors[index % colors.length]
    }));

    return {
      bars,
      hasData: bars.length > 0 
    };
  });

  classicTableData = computed(() => {
    const invs: Invoice[] = this.filteredInvoices();
    const mode = this.classicDisplayMode();
    
    // Group by Year and Term
    const groupMap = new Map<string, { year: number, term: string, ordered: number, refunded: number, net: number, libSales: Map<string, number> }>();

    invs.forEach(inv => {
      const sem = this.settingsService.allSemesters().find(s => s.id === inv.semesterId);
      const yearStr = sem?.academicYearName ? sem.academicYearName.split('-')[0] : '';
      const year = yearStr ? parseInt(yearStr, 10) : new Date(inv.date || new Date()).getFullYear();
      const term = inv.termCode === 'B' || inv.semesterName === 'الفصل الثاني'
        ? 'الترم الثاني'
        : inv.termCode === 'A' || inv.semesterName === 'الفصل الأول'
          ? 'الترم الأول'
          : (inv.semesterName || 'غير محدد');
      const key = `${year}-${term}`;
        
        if (!groupMap.has(key)) {
          groupMap.set(key, { year, term, ordered: 0, refunded: 0, net: 0, libSales: new Map() });
        }
        
        const group = groupMap.get(key)!;
        const libName = inv.libraryName || 'غير محدد';

      inv.items?.forEach(item => {
        const qty = item.quantity || 0;
        const total = item.total || 0;
        const metric = mode === 'revenue' ? total : qty;

        if (inv.type === 'order') {
          group.ordered += qty;
          group.net += total;
          group.libSales.set(libName, (group.libSales.get(libName) || 0) + metric);
        } else if (inv.type === 'refund') {
          group.refunded += qty;
          group.net -= total;
          group.libSales.set(libName, (group.libSales.get(libName) || 0) - metric);
        }
      });
    });

    const rows = Array.from(groupMap.values()).map(g => {
      let bestLib = '-';
      let maxVal = -1;
      g.libSales.forEach((val, lib) => {
        if (val > maxVal) {
          maxVal = val;
          bestLib = lib;
        }
      });

      return {
        year: g.year,
        term: g.term,
        bestLibrary: bestLib,
        ordered: g.ordered,
        refunded: g.refunded,
        netSales: g.net
      };
    });

    return rows.sort((a, b) => b.year - a.year || b.term.localeCompare(a.term));
  });
}
