import {
  Component,
  computed,
  signal,
  inject,
  Input,
  ChangeDetectorRef,
  effect,
  Output,
  EventEmitter,
  DestroyRef,
  ChangeDetectionStrategy,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { filter, switchMap } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { InventoryService } from '../../core/services/inventory.service';
import { LibraryService } from '../../core/services/library.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { ToastService } from '../../core/services/toast.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { Invoice, InvoiceItem } from '../../core/models/invoice.model';
import { Library } from '../../core/models/library.model';
import { ActivityService } from '../../core/services/activity.service';
import { SettingsService } from '../../core/services/settings.service';
import { formatAmountRials, formatAmountBaisa } from '../../core/utils/format.utils';
import { ReceiptVoucherService } from '../../core/services/receipt-voucher.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';
import {
  LS_INV_FORM_COLLAPSED,
  LS_INV_HISTORY_COLLAPSED,
  LS_INV_IS_MERGED,
  LS_INV_FORCE_SHOW_BTN,
} from '../../core/constants/local-storage-keys';
import { printWhenImagesReady } from '../../core/utils/print.utils';
import { InvoicePrintFooterComponent } from '../../shared/invoice-print-footer/invoice-print-footer';
import { ReceiptVoucher } from '../../core/models/receipt-voucher.model';

type InvoiceListItem =
  Invoice | (ReceiptVoucher & { type: 'receipt_voucher'; totalAmount: number; items: never[] });

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

interface RefundSettlementItem {
  bookName: string;
  bookGrade: string;
  totalSent: number;
  totalReturned: number;
  totalSold: number;
  unitPrice: number;
  amountDue: number;
}

interface RefundSettlementGroup {
  grade: string;
  items: RefundSettlementItem[];
}

interface AllBooksPrintItem {
  globalIndex: number;
  bookId: number;
  bookName: string;
  bookGrade: string;
  quantity: number;
  unitPrice: number;
  total: number;
  isOrdered: boolean;
}

@Component({
  selector: 'app-invoices',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule, InvoicePrintFooterComponent],
  templateUrl: './invoices.html',
})
export class InvoicesComponent {
  @Input() isCompact = false;
  trackById = (i: number, item: any) => item?.id ?? i;
  trackByIndex = (i: number) => i;
  @Output() addInventoryBook = new EventEmitter<void>();
  formatAmountRials = formatAmountRials;
  formatAmountBaisa = formatAmountBaisa;

  private inventoryService = inject(InventoryService);
  public libraryService = inject(LibraryService);
  private invoiceService = inject(InvoiceService);
  private receiptVoucherService = inject(ReceiptVoucherService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private activityService = inject(ActivityService);
  public settingsService = inject(SettingsService);
  private cdr = inject(ChangeDetectorRef);
  private gradeOrder = signal<string[]>([]);

  private compareGrades(firstGrade: string, secondGrade: string): number {
    const grades = this.gradeOrder();
    const firstIndex = grades.indexOf(firstGrade);
    const secondIndex = grades.indexOf(secondGrade);
    const normalizedFirst = firstIndex === -1 ? Number.MAX_SAFE_INTEGER : firstIndex;
    const normalizedSecond = secondIndex === -1 ? Number.MAX_SAFE_INTEGER : secondIndex;
    return normalizedFirst === normalizedSecond
      ? firstGrade.localeCompare(secondGrade, 'ar')
      : normalizedFirst - normalizedSecond;
  }

  getAcademicYearForSemester(semesterId: number): string {
    const sem = this.settingsService.allSemesters().find((s) => s.id === semesterId);
    return sem?.academicYearName || '';
  }

  onAddInventoryBook() {
    this.addInventoryBook.emit();
  }

  getLibraryResponsible(libraryId: number): { name: string; phone: string } {
    const lib = this.librariesData().find((l) => l.id === libraryId);
    return {
      name: lib?.responsibleName || lib?.ownerName || '',
      phone: lib?.responsiblePhone || lib?.ownerPhone || '',
    };
  }

  getLibraryFullDetails(libraryId: number): {
    ownerName: string;
    ownerPhone: string;
    responsibleName: string;
    responsiblePhone: string;
    landlinePhone: string;
  } {
    const lib = this.librariesData().find((l) => l.id === libraryId);
    return {
      ownerName: lib?.ownerName || '',
      ownerPhone: lib?.ownerPhone || '',
      responsibleName: lib?.responsibleName || '',
      responsiblePhone: lib?.responsiblePhone || '',
      landlinePhone: lib?.landlinePhone || '',
    };
  }

  getInvoiceDisplayNumber(invoice: Invoice | null): string {
    if (!invoice) return '';
    if (invoice.displayNumber) return invoice.displayNumber;
    return `${invoice.invoiceNumber ?? ''}${invoice.termCode ?? ''}`;
  }

  getPrintGroups(
    invoice: Invoice | null,
  ): { grade: string; items: (InvoiceItem & { globalIndex: number })[] }[] {
    if (!invoice) return [];
    const groupsMap = new Map<string, (InvoiceItem & { globalIndex: number })[]>();
    invoice.items.forEach((item, index) => {
      const grade = item.bookGrade || 'أخرى';
      if (!groupsMap.has(grade)) groupsMap.set(grade, []);
      groupsMap.get(grade)!.push({ ...item, globalIndex: index + 1 });
    });
    return Array.from(groupsMap.entries())
      .sort((a, b) => this.compareGrades(a[0], b[0]))
      .map(([grade, items]) => ({ grade, items }));
  }

  /** Returns print groups with ALL books in the semester, not just ordered ones */
  getOrderPrintGroupsWithAllBooks(
    invoice: Invoice | null,
  ): { grade: string; items: AllBooksPrintItem[] }[] {
    if (!invoice) return [];
    const semId = invoice.semesterId;
    const allBooks = this.draftItems().filter((i) => i.semesterId === semId);
    const invoiceItemMap = new Map<number, any>();
    (invoice.items || []).forEach((item: any) => invoiceItemMap.set(item.bookId, item));

    const gradeMap = new Map<string, AllBooksPrintItem[]>();
    let index = 1;

    // First add all books from inventory
    for (const book of allBooks) {
      const grade = book.grade || 'أخرى';
      if (!gradeMap.has(grade)) gradeMap.set(grade, []);
      const invoiceItem = invoiceItemMap.get(book.bookId);
      gradeMap.get(grade)!.push({
        globalIndex: index++,
        bookId: book.bookId,
        bookName: book.name,
        bookGrade: book.grade,
        quantity: invoiceItem ? invoiceItem.quantity : 0,
        unitPrice: invoiceItem ? invoiceItem.unitPrice : book.price,
        total: invoiceItem ? invoiceItem.total : 0,
        isOrdered: !!invoiceItem,
      });
      invoiceItemMap.delete(book.bookId);
    }

    // Add any invoice items not in inventory (edge case)
    for (const [bookId, item] of invoiceItemMap) {
      const grade = item.bookGrade || 'أخرى';
      if (!gradeMap.has(grade)) gradeMap.set(grade, []);
      gradeMap.get(grade)!.push({
        globalIndex: index++,
        bookId: item.bookId,
        bookName: item.bookName,
        bookGrade: item.bookGrade,
        quantity: item.quantity,
        unitPrice: item.unitPrice,
        total: item.total,
        isOrdered: true,
      });
    }

    return Array.from(gradeMap.entries())
      .sort((a, b) => this.compareGrades(a[0], b[0]))
      .map(([grade, items]) => ({ grade, items }));
  }

  /** Compute refund settlement: total sent, returned, sold, amount due per book */
  // Refund settlement is now fetched from the backend

  // Settlement totals are tracked via signals now

  /** Get total sent books for an order invoice */
  getTotalBooksCount(invoice: Invoice | null): number {
    if (!invoice?.items) return 0;
    return invoice.items.reduce((sum: number, item: InvoiceItem) => sum + (item.quantity || 0), 0);
  }

  getTypeColor(type: string): string {
    switch (type) {
      case 'order':
        return 'bg-primary';
      case 'refund':
        return 'bg-error';
      case 'receipt_voucher':
        return 'bg-[#1a237e]';
      default:
        return 'bg-secondary';
    }
  }

  getTypeBadgeClass(type: string): string {
    switch (type) {
      case 'order':
        return 'bg-primary/10 text-primary';
      case 'refund':
        return 'bg-error/10 text-error';
      case 'receipt_voucher':
        return 'bg-[#1a237e]/10 text-[#1a237e]';
      default:
        return 'bg-secondary/10 text-secondary';
    }
  }

  getTypeAmountColor(type: string): string {
    switch (type) {
      case 'order':
        return 'text-primary';
      case 'refund':
        return 'text-error';
      case 'receipt_voucher':
        return 'text-[#1a237e]';
      default:
        return 'text-secondary';
    }
  }

  getTypeLabel(type: string): string {
    switch (type) {
      case 'order':
        return 'طلبية بيع';
      case 'refund':
        return 'مرتجع';
      case 'receipt_voucher':
        return 'سند قبض';
      default:
        return 'مخالصة';
    }
  }

  getPrintTypeLabel(type: string | undefined): string {
    switch (type) {
      case 'order':
        return 'فاتورة رقم';
      case 'refund':
        return 'مرتجع رقم';
      default:
        return 'مخالصة رقم';
    }
  }

  librariesData = signal<Library[]>([]);

  isFormCollapsed = signal(localStorage.getItem(LS_INV_FORM_COLLAPSED) === 'true');
  toggleForm() {
    this.isFormCollapsed.set(!this.isFormCollapsed());
    localStorage.setItem(LS_INV_FORM_COLLAPSED, String(this.isFormCollapsed()));
  }

  isHistoryCollapsed = signal(localStorage.getItem(LS_INV_HISTORY_COLLAPSED) === 'true');
  toggleHistory() {
    this.isHistoryCollapsed.set(!this.isHistoryCollapsed());
    localStorage.setItem(LS_INV_HISTORY_COLLAPSED, String(this.isHistoryCollapsed()));
  }

  // Active form state
  selectedGovernorateId = 0;
  selectedGovName = '';
  selectedCityId = 0;
  selectedCityName = '';
  selectedLibraryId = 0;
  selectedLibName = '';
  isCreatingOrder = false;
  isCreatingRefund = false;

  filteredCities() {
    const govs = this.libraryService.governorates();
    const gov = govs.find((g) => g.id === this.selectedGovernorateId);
    if (!gov) return [];
    const libsInGov = this.librariesData().filter(
      (l) => l.governorateId === this.selectedGovernorateId,
    );
    const cityIdsWithLibraries = new Set(libsInGov.map((l) => l.cityId));
    return gov.cities.filter((c) => cityIdsWithLibraries.has(c.id));
  }

  filteredLibraries() {
    let libs = this.librariesData().filter((l) => l.isActive !== false);
    if (this.selectedGovernorateId != 0)
      libs = libs.filter((l) => l.governorateId == this.selectedGovernorateId);
    if (this.selectedCityId != 0) libs = libs.filter((l) => l.cityId == this.selectedCityId);
    return libs;
  }

  onGovChange(id: number) {
    this.selectedGovernorateId = id;
    this.selectedCityId = 0;
    this.selectedLibraryId = 0;
  }

  onCityChange(id: number) {
    this.selectedCityId = id;
    this.selectedLibraryId = 0;
  }

  onLibChange(id: number) {
    this.selectedLibraryId = id;
    if (id > 0) {
      const lib = this.librariesData().find((l) => l.id === id);
      if (lib) {
        this.selectedGovernorateId = lib.governorateId;
        this.selectedCityId = lib.cityId;
      }
    }
  }

  // History filtering
  filterType = signal('');
  filterTime = signal('all');
  filterGovernorateId = signal(0);
  filterCityId = signal(0);
  filterLibraryId = signal(0);
  filterSemesterId = signal<number>(0);

  filterHistoryCities = computed(() => {
    const govs = this.libraryService.governorates();
    const gov = govs.find((g) => g.id === this.filterGovernorateId());
    return gov?.cities || [];
  });

  filterHistoryLibraries = computed(() => {
    let libs = this.librariesData();
    if (this.filterGovernorateId())
      libs = libs.filter((l) => l.governorateId === this.filterGovernorateId());
    if (this.filterCityId()) libs = libs.filter((l) => l.cityId === this.filterCityId());
    return libs;
  });

  librariesWithHistoryIds = computed(() => {
    const invs = this.invoicesList();
    const vouchs = this.receiptVoucherService.vouchers$();
    const ids = new Set<number>();
    invs.forEach((i) => {
      if (i.libraryId) ids.add(i.libraryId);
    });
    vouchs.forEach((v) => {
      if (v.libraryId) ids.add(v.libraryId);
    });
    return ids;
  });

  govsWithHistoryIds = computed(() => {
    const ids = new Set<number>();
    const libIds = this.librariesWithHistoryIds();
    const libs = this.librariesData();
    libs.forEach((l) => {
      if (libIds.has(l.id)) {
        ids.add(l.governorateId);
      }
    });
    return ids;
  });

  citiesWithHistoryIds = computed(() => {
    const ids = new Set<number>();
    const libIds = this.librariesWithHistoryIds();
    const libs = this.librariesData();
    libs.forEach((l) => {
      if (libIds.has(l.id)) {
        ids.add(l.cityId);
      }
    });
    return ids;
  });

  onFilterGovChange(id: number) {
    this.filterGovernorateId.set(id);
    this.filterCityId.set(0);
    this.filterLibraryId.set(0);
  }

  onFilterCityChange(id: number) {
    this.filterCityId.set(id);
    this.filterLibraryId.set(0);
  }

  onFilterLibChange(id: number) {
    this.filterLibraryId.set(id);
  }

  filteredInvoices = computed(() => {
    let list: InvoiceListItem[] = [...this.invoicesList()];

    // Mix in receipt vouchers
    const vouchers = this.receiptVoucherService.vouchers$();
    const mappedVouchers: InvoiceListItem[] = vouchers.map((v) => ({
      ...v,
      type: 'receipt_voucher' as const,
      totalAmount: v.amount,
      items: [],
    }));

    list = [...list, ...mappedVouchers];

    const semId = this.filterSemesterId();
    if (semId > 0) {
      list = list.filter((i) => i.semesterId == null || i.semesterId === semId);
    }

    const govId = this.filterGovernorateId();
    if (govId)
      list = list.filter((i) => {
        const lib = this.librariesData().find((l) => l.id === i.libraryId);
        return lib && lib.governorateId === govId;
      });

    const cityId = this.filterCityId();
    if (cityId)
      list = list.filter((i) => {
        const lib = this.librariesData().find((l) => l.id === i.libraryId);
        return lib && lib.cityId === cityId;
      });

    const libId = this.filterLibraryId();
    if (libId) list = list.filter((i) => i.libraryId === libId);

    const type = this.filterType();
    if (type) list = list.filter((i) => i.type === type);

    const time = this.filterTime();
    if (time !== 'all') {
      const now = new Date();
      list = list.filter((i) => {
        if (!i.date) return false;
        const d = new Date(i.date);
        if (time === 'today') {
          return (
            d.getDate() === now.getDate() &&
            d.getMonth() === now.getMonth() &&
            d.getFullYear() === now.getFullYear()
          );
        } else if (time === 'yesterday') {
          const y = new Date(now);
          y.setDate(y.getDate() - 1);
          return (
            d.getDate() === y.getDate() &&
            d.getMonth() === y.getMonth() &&
            d.getFullYear() === y.getFullYear()
          );
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
    }) as any[];
  });

  isMerged = signal(localStorage.getItem(LS_INV_IS_MERGED) === 'true');

  setMerged(val: boolean) {
    this.isMerged.set(val);
    localStorage.setItem(LS_INV_IS_MERGED, String(val));
  }

  isForceShowButtonVisible = signal<boolean>(
    JSON.parse(localStorage.getItem(LS_INV_FORCE_SHOW_BTN) || 'false'),
  );
  isForceShowActive = signal<boolean>(false);

  toggleForceShowButtonVisibility() {
    this.isForceShowButtonVisible.update((v) => !v);
    localStorage.setItem(LS_INV_FORCE_SHOW_BTN, JSON.stringify(this.isForceShowButtonVisible()));
  }

  invoicesList = this.invoiceService.invoices$;
  draftItems = signal<DraftInvoiceItem[]>([]);

  invoiceSemesterId = signal<number>(0);
  filterDraftGrade = signal<string>('');

  activeYearSemesters = computed(() => {
    const active = this.settingsService.activeSemester();
    if (!active) return this.settingsService.allSemesters();
    return this.settingsService
      .allSemesters()
      .filter((s) => s.academicYearName === active.academicYearName);
  });

  availableGrades = computed(() => {
    const grades = new Set<string>();
    this.draftItems().forEach((i) => grades.add(i.grade || 'أخرى'));
    return Array.from(grades);
  });

  trackByBookId(index: number, item: any): number {
    return item.bookId;
  }

  trackByGrade(index: number, item: any): string {
    return item.grade;
  }

  draftItemsGrouped = computed(() => {
    let items = this.draftItems();

    const semId = this.invoiceSemesterId();
    if (semId > 0) {
      items = items.filter((i) => i.semesterId === semId);
    }

    const gradeFilter = this.filterDraftGrade();
    if (gradeFilter) {
      items = items.filter((i) => (i.grade || 'أخرى') === gradeFilter);
    }

    const groups = new Map<string, DraftInvoiceItem[]>();
    items.forEach((item) => {
      const grade = item.grade || 'أخرى';
      if (!groups.has(grade)) groups.set(grade, []);
      groups.get(grade)!.push(item);
    });
    return Array.from(groups.entries()).map(([grade, items]) => ({ grade, items }));
  });

  draftTotal = computed(() => {
    const semId = this.invoiceSemesterId();
    return this.draftItems()
      .filter((i) => semId <= 0 || i.semesterId === semId)
      .reduce((sum, item) => sum + (item.total || 0), 0);
  });

  private destroyRef = inject(DestroyRef);

  constructor() {
    this.invoiceService
      .getGradeOrder()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((response) => this.gradeOrder.set(response.data ?? []));

    effect(() => {
      const active = this.settingsService.activeSemester();
      const semesters = this.settingsService.allSemesters();
      if (active?.id) {
        this.invoiceSemesterId.set(active.id);
        this.filterSemesterId.set(active.id);
        this.draftItems.update((items) => items.filter((i) => i.semesterId === active.id));
      } else if (semesters.length > 0 && this.invoiceSemesterId() === 0) {
        this.invoiceSemesterId.set(semesters[0].id);
        if (this.filterSemesterId() === 0) this.filterSemesterId.set(semesters[0].id);
      }
    });

    this.libraryService.libraries$.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((items) => {
      this.librariesData.set(items);
    });

    this.inventoryService.inventory$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((items) => {
        const activeId = this.settingsService.activeSemester()?.id;
        const filtered = activeId ? items.filter((i) => i.semesterId === activeId) : items;
        const currentDrafts = this.draftItems();
        const newDrafts = filtered.map((i) => {
          const existing = currentDrafts.find((d) => d.bookId === i.id);
          return {
            bookId: i.id,
            name: i.name,
            grade: i.grade || '',
            stockQuantity: i.stockQuantity || 0,
            quantity: existing ? existing.quantity : null,
            price: i.price,
            total: existing ? existing.total : null,
            semesterId: i.semesterId,
          };
        });
        this.draftItems.set(newDrafts);
      });
  }

  selectInputContent(event: FocusEvent) {
    (event.target as HTMLInputElement).select();
  }

  updateItemTotal(item: DraftInvoiceItem) {
    this.draftItems.update((items) =>
      items.map((i) =>
        i.bookId === item.bookId
          ? { ...i, total: (item.quantity ?? 0) > 0 ? item.quantity! * item.price : null }
          : i,
      ),
    );
  }

  processOrder() {
    if (this.isCreatingOrder || this.isCreatingRefund) return;

    if (!this.selectedLibraryId) {
      this.toast.show('الرجاء اختيار المكتبة أولاً', 'error');
      return;
    }

    const currentSemId = this.invoiceSemesterId();
    if (!currentSemId) {
      this.toast.show('لم يتم تحميل الفصل الدراسي بعد، يرجى الانتظار', 'error');
      return;
    }
    const itemsToProcess = this.draftItems().filter(
      (i) => (i.quantity || 0) > 0 && i.semesterId === currentSemId,
    );

    if (itemsToProcess.length === 0) {
      this.toast.show('الرجاء إدخال كميات لبعض المواد على الأقل', 'error');
      return;
    }

    const orderData = {
      libraryId: this.selectedLibraryId,
      semesterId: currentSemId,
      items: itemsToProcess.map((i) => ({ bookId: i.bookId, quantity: i.quantity as number })),
    };

    this.isCreatingOrder = true;
    this.invoiceService
      .createOrder(orderData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.isCreatingOrder = false;
          const invoice = res.data!;
          this.activityService.logActivity(
            'طلب بيع',
            `تم إنشاء طلب بيع للمكتبة "${invoice.libraryName}" بقيمة ${invoice.totalAmount} ريال`,
            'ADD',
            { entity: 'invoice', id: invoice.id, current: invoice as any },
          );
          this.toast.show('تم تسجيل طلب الشراء بنجاح وخصم الكميات!', 'success');
          this.resetDraft();
          this.inventoryService.fetchBooks(); // Refresh stock
          this.printInvoice(invoice);
        },
        error: (err: HttpErrorResponse) => {
          this.isCreatingOrder = false;
          this.toast.show(err.error?.message || 'حدث خطأ في التسجيل', 'error');
        },
      });
  }

  processRefund() {
    if (this.isCreatingOrder || this.isCreatingRefund) return;

    if (!this.selectedLibraryId) {
      this.toast.show('الرجاء اختيار المكتبة أولاً', 'error');
      return;
    }

    const currentSemId = this.invoiceSemesterId();
    if (!currentSemId) {
      this.toast.show('لم يتم تحميل الفصل الدراسي بعد، يرجى الانتظار', 'error');
      return;
    }
    const itemsToProcess = this.draftItems().filter(
      (i) => (i.quantity || 0) > 0 && i.semesterId === currentSemId,
    );

    if (itemsToProcess.length === 0) {
      this.toast.show('الرجاء إدخال كميات لبعض المواد على الأقل', 'error');
      return;
    }

    const refundData = {
      libraryId: this.selectedLibraryId,
      semesterId: currentSemId,
      items: itemsToProcess.map((i) => ({ bookId: i.bookId, quantity: i.quantity as number })),
    };

    this.isCreatingRefund = true;
    this.invoiceService
      .hasExistingRefund(this.selectedLibraryId, currentSemId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (response) => {
          if (!response.data?.exists) {
            this.executeRefund(refundData);
            return;
          }

          const message =
            'لقد أجرت هذه المكتبة عملية استرجاع سابقة في هذا الفصل الدراسي بالفعل. هل أنت متأكد من تسجيل عملية استرجاع أخرى؟';
          this.confirmService
            .confirm(message)
            .pipe(takeUntilDestroyed(this.destroyRef))
            .subscribe((confirmed) => {
              if (confirmed) this.executeRefund(refundData);
              else this.isCreatingRefund = false;
            });
        },
        error: (error: HttpErrorResponse) => {
          this.isCreatingRefund = false;
          this.toast.show(error.error?.message || 'تعذر التحقق من المرتجعات السابقة', 'error');
        },
      });
  }

  private executeRefund(refundData: {
    libraryId: number;
    semesterId: number;
    items: { bookId: number; quantity: number }[];
  }) {
    this.invoiceService
      .createRefund(refundData)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (res) => {
          this.isCreatingRefund = false;
          const invoice = res.data!;
          this.activityService.logActivity(
            'مرتجع',
            `تم تسجيل مرتجع للمكتبة "${invoice.libraryName}" بقيمة ${invoice.totalAmount} ريال`,
            'ADD',
            { entity: 'invoice', id: invoice.id, current: invoice as any },
          );
          this.toast.show('تم تسجيل المرتجعات بنجاح وإعادتها للمخزون!', 'success');
          this.resetDraft();
          this.inventoryService.fetchBooks(); // Refresh stock
          this.printInvoice(invoice);
        },
        error: (err: HttpErrorResponse) => {
          this.isCreatingRefund = false;
          this.toast.show(err.error?.message || 'حدث خطأ في التسجيل', 'error');
        },
      });
  }

  resetDraft() {
    this.draftItems.update((items) => items.map((i) => ({ ...i, quantity: null, total: null })));
    this.selectedLibraryId = 0;
    this.selectedLibName = '';
  }

  invoiceToPrint = signal<any>(null);
  previewInvoice = signal<any>(null);
  readonly assetUrls = ASSET_URLS;

  refundSettlementData = signal<RefundSettlementGroup[]>([]);
  settlementTotal = signal<number>(0);
  settlementTotalReturned = signal<number>(0);

  viewInvoice(invoice: InvoiceListItem) {
    this.previewInvoice.set(invoice);
  }

  closePreview() {
    this.previewInvoice.set(null);
  }

  printInvoice(invoice: InvoiceListItem) {
    this.invoiceToPrint.set(invoice);

    if (invoice.type === 'refund') {
      this.invoiceService
        .getClearancePreview(invoice.semesterId, invoice.libraryId)
        .subscribe((res) => {
          const items = res.data?.items || [];
          const groupsMap = new Map<string, RefundSettlementItem[]>();
          let sumReturned = 0;
          let sumDue = 0;

          items.forEach((item) => {
            const grade = item.bookGrade || 'أخرى';
            if (!groupsMap.has(grade)) groupsMap.set(grade, []);

            const sold = item.soldQuantity || 0;
            sumReturned += item.refundedQty || 0;
            const amountDue = item.amountDue || 0;
            sumDue += amountDue;

            groupsMap.get(grade)!.push({
              bookName: item.bookName,
              bookGrade: item.bookGrade,
              totalSent: item.orderedQty || 0,
              totalReturned: item.refundedQty || 0,
              totalSold: sold,
              unitPrice: item.unitPrice,
              amountDue,
            });
          });

          const groups: RefundSettlementGroup[] = Array.from(groupsMap.entries())
            .sort((a, b) => this.compareGrades(a[0], b[0]))
            .map(([grade, groupItems]) => ({ grade, items: groupItems }));

          this.refundSettlementData.set(groups);
          this.settlementTotal.set(sumDue);
          this.settlementTotalReturned.set(sumReturned);
          this.cdr.detectChanges();
          this.triggerPrint(invoice);
        });
      return;
    }

    this.cdr.detectChanges();
    this.triggerPrint(invoice);
  }

  private triggerPrint(invoice: InvoiceListItem) {
    if (invoice.type !== 'receipt_voucher' && invoice.id) {
      const handleAfterPrint = () => {
        this.invoiceService
          .updatePrintStatus(invoice.id!, 'printed')
          .pipe(takeUntilDestroyed(this.destroyRef))
          .subscribe({ error: () => console.error('Failed to update print status') });
      };
      window.addEventListener('afterprint', handleAfterPrint, { once: true });
    }
    const selector =
      invoice.type === 'receipt_voucher' ? '.receipt-voucher-print-page' : '.invoice-print-page';
    printWhenImagesReady(selector, () => {
      this.invoiceToPrint.set(null);
    });
  }

  retryPrint(invoice: InvoiceListItem) {
    this.printInvoice(invoice);
  }

  deleteInvoice(invoice: InvoiceListItem, event: Event) {
    event.stopPropagation();

    const msg =
      invoice.type === 'receipt_voucher'
        ? `هل أنت متأكد من حذف سند القبض رقم ${invoice.displayNumber}؟`
        : `هل أنت متأكد من حذف ${invoice.type === 'order' ? 'فاتورة البيع' : invoice.type === 'refund' ? 'المرتجع' : 'المخالصة'} رقم ${invoice.displayNumber}؟`;

    this.confirmService
      .confirm(msg)
      .pipe(
        filter((result) => !!result && !!invoice.id),
        switchMap(() => {
          if (invoice.type === 'receipt_voucher') {
            return this.receiptVoucherService.delete(invoice.id!);
          }
          return this.invoiceService.deleteInvoice(invoice.id!);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => {
          this.activityService.logActivity(
            'حذف فاتورة',
            `تم حذف ${invoice.type === 'order' ? 'فاتورة بيع' : invoice.type === 'refund' ? 'مرتجع' : 'سند قبض'} رقم ${(invoice as any).displayNumber}`,
            'DELETE',
            {
              entity: invoice.type === 'receipt_voucher' ? 'receipt_voucher' : 'invoice',
              id: invoice.id,
              previous: { ...(invoice as any) },
            },
          );
          this.toast.show('تم الحذف بنجاح', 'success');
          if (invoice.type !== 'receipt_voucher') {
            this.inventoryService.fetchBooks();
          }
        },
        error: (err: HttpErrorResponse) =>
          this.toast.show(err.error?.message || 'تعذر الحذف', 'error'),
      });
  }

  getPhoneNumbersOnly(phones: string | undefined): string {
    if (!phones) return '';
    const prefixes = ['إدارة المبيعات: هاتف:', 'هاتف:', 'إدارة المبيعات:'];
    for (const prefix of prefixes) {
      if (phones.startsWith(prefix)) {
        return phones.substring(prefix.length).trim();
      }
    }
    return phones;
  }
}
