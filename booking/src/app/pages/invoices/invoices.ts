import { Component, computed, signal, inject, Input, ChangeDetectorRef, effect, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { InventoryService } from '../../core/services/inventory.service';
import { LibraryService } from '../../core/services/library.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { ToastService } from '../../core/services/toast.service';
import { Invoice, InvoiceItem } from '../../core/models/invoice.model';
import { Library, City } from '../../core/models/library.model';
import { ActivityService } from '../../core/services/activity.service';
import { SettingsService } from '../../core/services/settings.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';
import { printWhenImagesReady } from '../../core/utils/print.utils';
import { InvoicePrintFooterComponent } from '../../shared/invoice-print-footer/invoice-print-footer';

interface DraftInvoiceItem {
  bookId: number;
  name: string;
  grade: string;
  price: number;
  stockQuantity: number;
  quantity: number | null;
  total: number | null;
  semesterId: number;
}

@Component({
  selector: 'app-invoices',
  standalone: true,
  imports: [CommonModule, FormsModule, InvoicePrintFooterComponent],
  templateUrl: './invoices.html'
})
export class InvoicesComponent {
  @Input() isCompact = false;
  @Output() addInventoryBook = new EventEmitter<void>();
  protected Math = Math;
  
  private inventoryService = inject(InventoryService);
  public libraryService = inject(LibraryService);
  private invoiceService = inject(InvoiceService);
  private toast = inject(ToastService);
  private activityService = inject(ActivityService);
  public settingsService = inject(SettingsService);
  private cdr = inject(ChangeDetectorRef);

  onAddInventoryBook() {
    this.addInventoryBook.emit();
  }

  getLibraryResponsible(libraryName: string): { name: string, phone: string } {
    const lib = this.librariesData().find(l => l.name === libraryName);
    return { 
      name: lib?.responsibleName || lib?.ownerName || '', 
      phone: lib?.responsiblePhone || lib?.ownerPhone || '' 
    };
  }

  getInvoiceDisplayNumber(invoice: Invoice | null): string {
    if (!invoice) return '';
    if (invoice.displayNumber) return invoice.displayNumber;
    return `${invoice.invoiceNumber ?? ''}${invoice.termCode ?? ''}`;
  }

  getPrintGroups(invoice: Invoice | null): { grade: string, items: (InvoiceItem & { globalIndex: number })[] }[] {
    if (!invoice) return [];
    const groupsMap = new Map<string, (InvoiceItem & { globalIndex: number })[]>();
    invoice.items.forEach((item, index) => {
      const grade = item.bookGrade || 'أخرى';
      if (!groupsMap.has(grade)) groupsMap.set(grade, []);
      groupsMap.get(grade)!.push({ ...item, globalIndex: index + 1 });
    });
    return Array.from(groupsMap.entries()).map(([grade, items]) => ({ grade, items }));
  }

  librariesData = signal<Library[]>([]);
  
  isFormCollapsed = signal(localStorage.getItem('inv_formCollapsed') === 'true');
  toggleForm() {
    this.isFormCollapsed.set(!this.isFormCollapsed());
    localStorage.setItem('inv_formCollapsed', String(this.isFormCollapsed()));
  }

  isHistoryCollapsed = signal(localStorage.getItem('inv_historyCollapsed') === 'true');
  toggleHistory() {
    this.isHistoryCollapsed.set(!this.isHistoryCollapsed());
    localStorage.setItem('inv_historyCollapsed', String(this.isHistoryCollapsed()));
  }

  // Active form state
  selectedGovernorateId = 0;
  selectedCityId = 0;
  selectedLibraryId = 0;

  filteredCities() {
    const govs = this.libraryService.governorates();
    const gov = govs.find(g => g.id == this.selectedGovernorateId);
    return gov?.cities || [];
  }

  filteredLibraries() {
    let libs = this.librariesData();
    if (this.selectedGovernorateId != 0) libs = libs.filter(l => l.governorateId == this.selectedGovernorateId);
    if (this.selectedCityId != 0) libs = libs.filter(l => l.cityId == this.selectedCityId);
    return libs;
  }

  onGovernorateChange() {
    this.selectedCityId = 0;
    this.selectedLibraryId = 0;
  }

  onCityChange() {
    this.selectedLibraryId = 0;
  }

  // History filtering
  filterType = signal('');
  filterTime = signal('all');
  filterGovernorateId = signal(0);
  filterCityId = signal(0);
  filterLibraryId = signal(0);

  filterHistoryCities = computed(() => {
    const govs = this.libraryService.governorates();
    const gov = govs.find(g => g.id === this.filterGovernorateId());
    return gov?.cities || [];
  });

  filterHistoryLibraries = computed(() => {
    let libs = this.librariesData();
    if (this.filterGovernorateId()) libs = libs.filter(l => l.governorateId === this.filterGovernorateId());
    if (this.filterCityId()) libs = libs.filter(l => l.cityId === this.filterCityId());
    return libs;
  });

  onFilterGovernorateChange() {
    this.filterCityId.set(0);
    this.filterLibraryId.set(0);
  }

  onFilterCityChange() {
    this.filterLibraryId.set(0);
  }

  filteredInvoices = computed(() => {
    let list = this.invoicesList();

    const govId = this.filterGovernorateId();
    if (govId) list = list.filter(i => {
      const lib = this.librariesData().find(l => l.id === i.libraryId);
      return lib && lib.governorateId === govId;
    });

    const cityId = this.filterCityId();
    if (cityId) list = list.filter(i => {
      const lib = this.librariesData().find(l => l.id === i.libraryId);
      return lib && lib.cityId === cityId;
    });

    const libId = this.filterLibraryId();
    if (libId) list = list.filter(i => i.libraryId === libId);

    const type = this.filterType();
    if (type) list = list.filter(i => i.type === type);

    const time = this.filterTime();
    if (time !== 'all') {
      const now = new Date();
      list = list.filter(i => {
        if (!i.date) return false;
        const d = new Date(i.date);
        if (time === 'today') {
          return d.getDate() === now.getDate() && d.getMonth() === now.getMonth() && d.getFullYear() === now.getFullYear();
        } else if (time === 'yesterday') {
          const y = new Date(now);
          y.setDate(y.getDate() - 1);
          return d.getDate() === y.getDate() && d.getMonth() === y.getMonth() && d.getFullYear() === y.getFullYear();
        } else if (time === 'week') {
          const w = new Date(now);
          w.setDate(w.getDate() - 7);
          return d >= w;
        } else if (time === 'month') {
          const m = new Date(now);
          m.setMonth(m.getMonth() - 1);
          return d >= m;
        }
        return true;
      });
    }

    return list.sort((a, b) => {
      const dateA = a.date ? new Date(a.date).getTime() : 0;
      const dateB = b.date ? new Date(b.date).getTime() : 0;
      return dateB - dateA;
    });
  });

  isMerged = signal(localStorage.getItem('inv_isMerged') === 'true');

  setMerged(val: boolean) {
    this.isMerged.set(val);
    localStorage.setItem('inv_isMerged', String(val));
  }

  isForceShowButtonVisible = signal<boolean>(JSON.parse(localStorage.getItem('inv_force_show_btn') || 'false'));
  isForceShowActive = signal<boolean>(false);

  toggleForceShowButtonVisibility() {
    this.isForceShowButtonVisible.update(v => !v);
    localStorage.setItem('inv_force_show_btn', JSON.stringify(this.isForceShowButtonVisible()));
  }



  invoicesList = this.invoiceService.invoices$;
  draftItems = signal<DraftInvoiceItem[]>([]);

  invoiceSemesterId = signal<number>(0);
  filterDraftGrade = signal<string>('');

  availableGrades = computed(() => {
    const grades = new Set<string>();
    this.draftItems().forEach(i => grades.add(i.grade || 'أخرى'));
    return Array.from(grades);
  });

  onInvoiceSemesterChange() {
    this.draftItems.update(items => items.map(i => ({ ...i, quantity: null, total: null })));
  }

  draftItemsGrouped = computed(() => {
    let items = this.draftItems();
    
    const semId = this.invoiceSemesterId();
    if (semId > 0) {
      items = items.filter(i => i.semesterId === semId);
    }

    const gradeFilter = this.filterDraftGrade();
    if (gradeFilter) {
      items = items.filter(i => (i.grade || 'أخرى') === gradeFilter);
    }

    const groups = new Map<string, DraftInvoiceItem[]>();
    items.forEach(item => {
      const grade = item.grade || 'أخرى';
      if (!groups.has(grade)) groups.set(grade, []);
      groups.get(grade)!.push(item);
    });
    return Array.from(groups.entries()).map(([grade, items]) => ({ grade, items }));
  });

  draftTotal = computed(() => {
    const semId = this.invoiceSemesterId();
    return this.draftItems()
      .filter(i => semId <= 0 || i.semesterId === semId)
      .reduce((sum, item) => sum + (item.total || 0), 0);
  });

  constructor() {
    effect(() => {
      const active = this.settingsService.activeSemester();
      const semesters = this.settingsService.allSemesters();
      if (active?.id) {
        this.invoiceSemesterId.set(active.id);
      } else if (semesters.length > 0 && this.invoiceSemesterId() === 0) {
        this.invoiceSemesterId.set(semesters[0].id);
      }
    });

    this.libraryService.libraries$.subscribe(items => {
      this.librariesData.set(items);
      this.cdr.detectChanges();
    });

    this.inventoryService.inventory$.subscribe(items => {
      const currentDrafts = this.draftItems();
      const newDrafts = items.map(i => {
        const existing = currentDrafts.find(d => d.bookId === i.id);
        return {
          bookId: i.id,
          name: i.name,
          grade: i.grade || '',
          stockQuantity: i.stockQuantity || 0,
          quantity: existing ? existing.quantity : null,
          price: i.price,
          total: existing ? existing.total : null,
          semesterId: i.semesterId
        };
      });
      this.draftItems.set(newDrafts);
    });
  }

  updateItemTotal(item: DraftInvoiceItem) {
    if (item.quantity !== null && item.quantity !== undefined && item.quantity > 0) {
      item.total = item.quantity * item.price;
    } else {
      item.total = null;
    }
    this.draftItems.update(items => [...items]);
  }

  processOrder() {
    if (!this.selectedLibraryId) {
      this.toast.show('الرجاء اختيار المكتبة أولاً', 'error');
      return;
    }

    const currentSemId = this.invoiceSemesterId();
    const itemsToProcess = this.draftItems().filter(i => (i.quantity || 0) > 0 && i.semesterId === currentSemId);
    
    if (itemsToProcess.length === 0) {
      this.toast.show('الرجاء إدخال كميات لبعض المواد على الأقل', 'error');
      return;
    }

    const orderData = {
      libraryId: this.selectedLibraryId,
      semesterId: currentSemId,
      items: itemsToProcess.map(i => ({ bookId: i.bookId, quantity: i.quantity! }))
    };

    this.invoiceService.createOrder(orderData).subscribe({
      next: (res: any) => {
        const invoice = res.data || res;
        this.toast.show('تم تسجيل طلب الشراء بنجاح وخصم الكميات!', 'success');
        this.resetDraft();
        this.inventoryService.fetchBooks(); // Refresh stock
        this.printInvoice(invoice);
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في التسجيل', 'error');
      }
    });
  }

  processRefund() {
    if (!this.selectedLibraryId) {
      this.toast.show('الرجاء اختيار المكتبة أولاً', 'error');
      return;
    }

    const currentSemId = this.invoiceSemesterId();
    const itemsToProcess = this.draftItems().filter(i => (i.quantity || 0) > 0 && i.semesterId === currentSemId);
    
    if (itemsToProcess.length === 0) {
      this.toast.show('الرجاء إدخال كميات لبعض المواد على الأقل', 'error');
      return;
    }

    const refundData = {
      libraryId: this.selectedLibraryId,
      semesterId: currentSemId,
      items: itemsToProcess.map(i => ({ bookId: i.bookId, quantity: i.quantity! }))
    };

    this.invoiceService.createRefund(refundData).subscribe({
      next: (res: any) => {
        const invoice = res.data || res;
        this.toast.show('تم تسجيل المرتجعات بنجاح وإعادتها للمخزون!', 'success');
        this.resetDraft();
        this.inventoryService.fetchBooks(); // Refresh stock
        this.printInvoice(invoice);
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في التسجيل', 'error');
      }
    });
  }

  resetDraft() {
    this.draftItems.update(items => items.map(i => ({ ...i, quantity: null, total: null })));
    this.selectedLibraryId = 0;
  }

  invoiceToPrint = signal<Invoice | null>(null);
  readonly assetUrls = ASSET_URLS;

  printInvoice(invoice: Invoice) {
    this.invoiceToPrint.set(invoice);
    this.cdr.detectChanges();
    printWhenImagesReady('.invoice-print-page', () => {
      const success = window.confirm('هل تمت الطباعة بنجاح؟');
      if (invoice.id) {
        this.invoiceService.updatePrintStatus(invoice.id, success ? 'printed' : 'pending').subscribe();
      }
      this.invoiceToPrint.set(null);
    });
  }

  retryPrint(invoice: Invoice) {
    this.printInvoice(invoice);
  }

  deleteInvoice(invoice: Invoice, event: Event) {
    event.stopPropagation();
    if (confirm(`هل أنت متأكد من حذف ${invoice.type === 'order' ? 'فاتورة البيع' : (invoice.type === 'refund' ? 'المرتجع' : 'المخالصة')} رقم ${invoice.displayNumber}؟`)) {
      if (invoice.id) {
        this.invoiceService.deleteInvoice(invoice.id).subscribe({
          next: () => {
            this.toast.show('تم الحذف بنجاح', 'success');
            this.inventoryService.fetchBooks();
          },
          error: (err: any) => {
            this.toast.show(err.error?.message || 'تعذر الحذف', 'error');
          }
        });
      }
    }
  }

  getPhoneNumbersOnly(phones: string | undefined): string {
    if (!phones) return '';
    const prefix = 'إدارة المبيعات: هاتف:';
    if (phones.includes(prefix)) {
      return phones.replace(prefix, '').trim();
    }
    return phones;
  }
}
