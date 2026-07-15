import { Component, computed, signal, inject, ChangeDetectorRef, Input, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { BehaviorSubject, combineLatest } from 'rxjs';
import { switchMap, filter, map, of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { LibraryService } from '../../core/services/library.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { ReceiptVoucherService } from '../../core/services/receipt-voucher.service';
import { Invoice } from '../../core/models/invoice.model';
import { ReceiptVoucher } from '../../core/models/receipt-voucher.model';
import { ToastService } from '../../core/services/toast.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { ActivityService } from '../../core/services/activity.service';
import { Library } from '../../core/models/library.model';
import { SettingsService } from '../../core/services/settings.service';
import { formatAmountRials, formatAmountBaisa } from '../../core/utils/format.utils';
import { ASSET_URLS } from '../../core/constants/asset-urls';
import { LS_LIB_ADD_FORM_COLLAPSED, LS_LIB_LIST_COLLAPSED } from '../../core/constants/local-storage-keys';
import { printWhenImagesReady } from '../../core/utils/print.utils';
import { InvoicePrintFooterComponent } from '../../shared/invoice-print-footer/invoice-print-footer';

interface ClearanceSummaryItem {
  id: number;
  name: string;
  grade: string;
  subject: string;
  orderedQty: number;
  refundedQty: number;
  netQty: number;
  price: number;
  total: number;
}

@Component({
  selector: 'app-libraries',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule, InvoicePrintFooterComponent],
  templateUrl: './libraries.html'
})
export class LibrariesComponent {
  @Input() isCompact = false;
  trackById = (i: number, item: any) => item?.id ?? i;
  trackByIndex = (i: number) => i;

  public settingsService = inject(SettingsService);
  public libraryService = inject(LibraryService);
  readonly assetUrls = ASSET_URLS;
  private invoiceService = inject(InvoiceService);
  private receiptVoucherService = inject(ReceiptVoucherService);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private cdr = inject(ChangeDetectorRef);
  private activityService = inject(ActivityService);

  librariesList = signal<Library[]>([]);
  
  // Modals state
  isClearanceModalOpen = false;
  isMapModalOpen = false;
  isDetailsModalOpen = false;
  
  selectedLibraryForMap = signal<Library | null>(null);
  selectedLibraryForDetails = signal<Library | null>(null);

  isEditingLibrary = false;
  editLibName = '';
  editLibLogo = '';
  editResponseRating = '';
  editPaymentRating = '';
  editLibraryNotes = '';
  editResponsibleName = '';
  editResponsiblePhone = '';
  editOwnerName = '';
  editOwnerPhone = '';
  editLandlinePhone = '';
  editShift1Start = '08:00';
  editShift1End = '13:00';
  editShift2Start = '16:00';
  editShift2End = '22:00';
  libraryInvoices = signal<any[]>([]);
  libraryVouchers = signal<ReceiptVoucher[]>([]);
  expandedInvoiceId = signal<number | null>(null);

  // Receipt Voucher Modal State
  isReceiptVoucherModalOpen = false;
  receiptVoucherLibrary = signal<Library | null>(null);
  receiptVoucherToPrint = signal<ReceiptVoucher | null>(null);
  rvAmount = 0;
  rvPaymentMethod: 'cash' | 'cheque' = 'cash';
  rvChequeNumber = '';
  rvBankName = '';
  rvPurpose = '';
  rvDate = new Date().toISOString().split('T')[0];

  isAddFormCollapsed = signal(localStorage.getItem(LS_LIB_ADD_FORM_COLLAPSED) === 'true');
  toggleAddForm() {
    this.isAddFormCollapsed.set(!this.isAddFormCollapsed());
    localStorage.setItem(LS_LIB_ADD_FORM_COLLAPSED, String(this.isAddFormCollapsed()));
  }

  isListCollapsed = signal(localStorage.getItem(LS_LIB_LIST_COLLAPSED) === 'true');
  isListEditMode = signal(false);
  searchTerm = '';
  private searchTerm$ = new BehaviorSubject<string>('');

  filteredLibraries$ = combineLatest([
    this.searchTerm$,
    this.libraryService.libraries$
  ]).pipe(
    map(([term, libs]) => {
      const t = term.trim().toLowerCase();
      if (!t) return libs;
      return libs.filter(l =>
        l.name.toLowerCase().includes(t) ||
        (l.governorateName || l.region || '').toLowerCase().includes(t) ||
        (l.cityName || l.city || '').toLowerCase().includes(t)
      );
    })
  );

  onSearchTermChange(term: string) {
    this.searchTerm = term;
    this.searchTerm$.next(term);
  }

  toggleList() {
    this.isListCollapsed.set(!this.isListCollapsed());
    localStorage.setItem(LS_LIB_LIST_COLLAPSED, String(this.isListCollapsed()));
  }

  clearanceLibrary = signal<any>(null);
  clearanceItems = signal<{grade: string, items: ClearanceSummaryItem[]}[]>([]);
  clearanceTotal = signal<number>(0);
  clearancePaidAmount = signal<number>(0);
  clearanceDate = new Date().toLocaleDateString('ar-SA', { calendar: 'gregory' });
  currentClearanceNumber = signal<string>('');
  clearanceSearchTerm = signal('');
  clearanceGradeFilter = signal('');

  filteredClearanceItems = computed(() => {
    const term = this.clearanceSearchTerm().trim().toLowerCase();
    const gradeFilter = this.clearanceGradeFilter();
    return this.clearanceItems()
      .map(group => ({
        grade: group.grade,
        items: group.items.filter(item => {
          if (term && !item.name.toLowerCase().includes(term)) return false;
          if (gradeFilter && item.grade !== gradeFilter) return false;
          return true;
        })
      }))
      .filter(group => group.items.length > 0);
  });

  clearanceGradeList = computed(() => {
    return ['كل الصفوف', ...new Set(this.clearanceItems().map(g => g.grade))];
  });

  clearanceToPrint = signal<any | null>(null);

  getInvoiceDisplayNumber(invoice: any): string {
    if (!invoice) return '';
    if (invoice.displayNumber) return invoice.displayNumber;
    return `${invoice.invoiceNumber ?? ''}${invoice.termCode ?? ''}`;
  }

  getPrintTypeLabel(type: string | undefined): string {
    switch (type) {
      case 'order': return 'فاتورة رقم';
      case 'refund': return 'مرتجع رقم';
      default: return 'مخالصة رقم';
    }
  }

  getPrintGroups(invoice: any): { grade: string, items: any[] }[] {
    if (!invoice) return [];
    const groupsMap = new Map<string, any[]>();
    (invoice.items || []).forEach((item: any, index: number) => {
      const grade = item.bookGrade || 'أخرى';
      if (!groupsMap.has(grade)) groupsMap.set(grade, []);
      groupsMap.get(grade)!.push({ ...item, globalIndex: index + 1 });
    });
    return Array.from(groupsMap.entries()).map(([grade, items]) => ({ grade, items }));
  }

  clearanceBatchInvoices = signal<Invoice[]>([]);
  showBatchClearanceView = signal(false);
  batchSearchTerm = signal('');
  expandedBatchCard = signal<number | null>(null);

  filteredBatchClearances = computed(() => {
    const term = this.batchSearchTerm().trim().toLowerCase();
    return this.clearanceBatchInvoices().filter(inv =>
      !term || inv.libraryName?.toLowerCase().includes(term)
    );
  });

  Math = Math;
  formatAmountRials = formatAmountRials;
  formatAmountBaisa = formatAmountBaisa;

  // Add form fields
  libraryName = '';
  ownerName = '';
  ownerPhone = '';
  responsibleName = '';
  responsiblePhone = '';
  landlinePhone = '';
  selectedGovernorateId = 0;
  selectedCityId = 0;
  shift1Start = '08:00';
  shift1End = '13:00';
  shift2Start = '16:00';
  shift2End = '22:00';
  
  selectedGovName = '';
  selectedCityName = '';

  private destroyRef = inject(DestroyRef);

  constructor() {
    this.libraryService.libraries$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(items => {
      this.librariesList.set(items);
      this.cdr.markForCheck();
    });

  }

  // Filtered cities based on selected governorate
  filteredCities() {
    const govs = this.libraryService.governorates();
    const gov = govs.find(g => g.id === Number(this.selectedGovernorateId));
    if (!gov) return [];
    return gov.cities;
  }

  onGovInput(val: string) {
    this.selectedGovName = val;
    const gov = this.libraryService.governorates().find(g => g.name === val);
    this.selectedGovernorateId = gov ? gov.id : 0;
    this.selectedCityId = 0;
    this.selectedCityName = '';
  }

  onCityInput(val: string) {
    this.selectedCityName = val;
    const city = this.filteredCities().find(c => c.name === val);
    this.selectedCityId = city ? city.id : 0;
  }

  onGovernorateChange() {
    this.selectedCityId = 0;
  }

  getLibraryStatus(lib?: Library | null): { text: string; colorClass: string; bgClass: string } {
    if (!lib) return { text: 'غير محدد', colorClass: 'text-on-surface-variant', bgClass: 'bg-surface-variant' };
    
    const shift1Start = lib.shift1Start;
    const shift1End = lib.shift1End;
    const shift2Start = lib.shift2Start;
    const shift2End = lib.shift2End;

    if (!shift1Start) return { text: 'نشط', colorClass: 'text-success', bgClass: 'bg-success' };

    const now = new Date();
    const currentMinutes = now.getHours() * 60 + now.getMinutes();

    const toMinutes = (time: string): number => {
      const parts = time.split(':');
      return parseInt(parts[0]) * 60 + parseInt(parts[1]);
    };

    // Check shift 1
    let isOpen = false;
    if (shift1Start && shift1End) {
      const s1 = toMinutes(shift1Start);
      const e1 = toMinutes(shift1End);
      isOpen = currentMinutes >= s1 && currentMinutes <= e1;
    }

    // Check shift 2
    if (!isOpen && shift2Start && shift2End) {
      const s2 = toMinutes(shift2Start);
      const e2 = toMinutes(shift2End);
      isOpen = currentMinutes >= s2 && currentMinutes <= e2;
    }

    if (isOpen) {
      return { text: 'مفتوح الآن', colorClass: 'text-success', bgClass: 'bg-success' };
    } else {
      return { text: 'مغلق', colorClass: 'text-error', bgClass: 'bg-error' };
    }
  }

  showDetails(lib: Library) {
    this.selectedLibraryForDetails.set(lib);
    this.isEditingLibrary = false;
    this.editLibName = lib.name;
    this.editLibLogo = lib.logo || '';
    this.editResponseRating = lib.responseRating || '';
    this.editPaymentRating = lib.paymentRating || '';
    this.editLibraryNotes = lib.notes || '';
    this.editResponsibleName = lib.responsibleName || '';
    this.editResponsiblePhone = lib.responsiblePhone || '';
    this.editOwnerName = lib.ownerName || '';
    this.editOwnerPhone = lib.ownerPhone || '';
    this.editLandlinePhone = lib.landlinePhone || '';
    this.editShift1Start = lib.shift1Start || '08:00';
    this.editShift1End = lib.shift1End || '13:00';
    this.editShift2Start = lib.shift2Start || '16:00';
    this.editShift2End = lib.shift2End || '22:00';
    
    // Fetch invoices for this library from API
    this.invoiceService.getInvoicesByLibraryId(lib.id).pipe(
      switchMap((res) => {
        const data = res.data;
        const invs = Array.isArray(data) ? data : [];
        return this.receiptVoucherService.getByLibraryId(lib.id).pipe(
          switchMap((vRes) => {
            const vData = vRes.data;
            const vouchers = Array.isArray(vData) ? vData.map((v: any) => ({
              ...v,
              type: 'receipt_voucher',
              totalAmount: v.amount
            })) : [];

            const combined = [...invs, ...vouchers];
            combined.sort((a: any, b: any) => {
              const d1 = a.date ? new Date(a.date).getTime() : 0;
              const d2 = b.date ? new Date(b.date).getTime() : 0;
              return d2 - d1;
            });
            this.libraryInvoices.set(combined);
            return [];
          })
        );
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      error: () => this.libraryInvoices.set([])
    });
    this.isDetailsModalOpen = true;
  }

  triggerEditLogoUpload(fileInput: HTMLInputElement) { fileInput.click(); }

  onEditLogoSelected(event: any) {
    const file = event.target.files[0];
    const lib = this.selectedLibraryForDetails();
    if (file && lib) {
      this.libraryService.uploadLogo(lib.id, file).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: (res: any) => {
          const logo = res.data?.logo || res.logo;
          if (logo) this.editLibLogo = logo;
          this.toast.show('تم رفع الشعار بنجاح', 'success');
        },
        error: () => this.toast.show('تعذر رفع الشعار', 'error')
      });
    }
  }

  saveEditedLibrary() {
    const lib = this.selectedLibraryForDetails();
    if (!lib) return;
    if (!this.editLibName.trim()) { this.toast.show('الرجاء إدخال اسم المكتبة', 'error'); return; }
    
    const updatedLib: Library = { 
      ...lib, 
      name: this.editLibName, 
      logo: this.editLibLogo || lib.logo,
      ownerName: this.editOwnerName || '',
      ownerPhone: this.editOwnerPhone || '',
      responsibleName: this.editResponsibleName || '',
      responsiblePhone: this.editResponsiblePhone || '',
      landlinePhone: this.editLandlinePhone || undefined,
      shift1Start: this.editShift1Start || '08:00',
      shift1End: this.editShift1End || '13:00',
      shift2Start: this.editShift2Start || undefined,
      shift2End: this.editShift2End || undefined,
      responseRating: this.editResponseRating || undefined,
      paymentRating: this.editPaymentRating || undefined,
      notes: this.editLibraryNotes || undefined
    };
    this.libraryService.updateLibrary(lib.id, updatedLib).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.selectedLibraryForDetails.set(updatedLib);
        this.isEditingLibrary = false;
        this.toast.show('تم تحديث بيانات المكتبة بنجاح!', 'success');
      },
      error: () => this.toast.show('حدث خطأ في تحديث البيانات', 'error')
    });
  }

  deleteLibrary() {
    const lib = this.selectedLibraryForDetails();
    if (!lib) return;
    this.confirmService.confirm(`هل أنت متأكد من حذف المكتبة: ${lib.name}؟`).pipe(
      filter(result => !!result),
      switchMap(() => this.libraryService.deleteLibrary(lib.id)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.toast.show('تم حذف المكتبة بنجاح', 'success');
        this.closeDetails();
      },
      error: () => this.toast.show('حدث خطأ في حذف المكتبة', 'error')
    });
  }

  deleteLibraryQuick(lib: Library, event: Event) {
    event.stopPropagation();
    this.confirmService.confirm(`هل أنت متأكد من حذف المكتبة: ${lib.name}؟`).pipe(
      filter(result => !!result),
      switchMap(() => this.libraryService.deleteLibrary(lib.id)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => this.toast.show('تم حذف المكتبة بنجاح', 'success'),
      error: () => this.toast.show('حدث خطأ في حذف المكتبة', 'error')
    });
  }

  closeDetails() { this.isDetailsModalOpen = false; this.selectedLibraryForDetails.set(null); }

  clearanceLoading = signal(false);
  clearanceError = signal('');
  clearance(lib?: Library) {
    const semesterId = this.settingsService.getActiveSemesterId();
    if (!semesterId) {
      this.clearanceError.set('لا يوجد فصل دراسي نشط. الرجاء تفعيل فصل دراسي من الإعدادات أولاً');
      return;
    }

    if (!lib) {
      this.clearanceLibrary.set({ id: 0, name: 'جميع المكتبات' });
    } else {
      this.clearanceLibrary.set(lib);
    }
    this.clearanceError.set('');
    this.isClearanceModalOpen = true;
    this.clearanceLoading.set(true);
    this.clearanceItems.set([]);
    this.clearanceTotal.set(0);
    this.clearancePaidAmount.set(0);
    this.clearanceBatchInvoices.set([]);
    this.clearanceSearchTerm.set('');
    this.clearanceGradeFilter.set('');

    const libId = lib?.id;
    this.invoiceService.getClearancePreview(semesterId, libId).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (res) => {
        const preview = res.data!;
        this.clearancePaidAmount.set(preview.paidAmount || 0);
        const netAmount = Math.max((preview.totalAmount || 0) - (preview.paidAmount || 0), 0);
        this.clearanceTotal.set(netAmount);

        const grouped = new Map<string, any[]>();
        (preview.items || []).forEach((item: any) => {
          const grade = item.bookGrade || 'أخرى';
          if (!grouped.has(grade)) { grouped.set(grade, []); }
          const gradeItems = grouped.get(grade);
          if (gradeItems) gradeItems.push({
            id: item.bookId,
            name: item.bookName,
            grade: grade,
            orderedQty: item.orderedQty || 0,
            refundedQty: item.refundedQty || 0,
            netQty: item.quantity,
            price: item.unitPrice,
            total: item.total
          });
        });

        const groupedArray = Array.from(grouped.entries()).map(([grade, items]) => ({ grade, items }));
        this.clearanceItems.set(groupedArray);
        this.clearanceLoading.set(false);
      },
      error: (err: any) => {
        this.clearanceLoading.set(false);
        this.clearanceError.set(err.error?.message || 'حدث خطأ في جلب بيانات المخالصة');
      }
    });
  }

  closeClearance() {
    this.isClearanceModalOpen = false;
    this.clearanceError.set('');
  }

  closeBatchView() {
    this.showBatchClearanceView.set(false);
    this.batchSearchTerm.set('');
    this.expandedBatchCard.set(null);
    this.clearanceBatchInvoices.set([]);
  }

  toggleBatchCard(id: number | undefined) {
    if (id == null) return;
    this.expandedBatchCard.set(this.expandedBatchCard() === id ? null : id);
  }

  toggleInvoiceDetails(inv: any) {
    const id = inv?.id ?? inv?.voucherNumber;
    if (id == null) return;
    this.expandedInvoiceId.set(this.expandedInvoiceId() === id ? null : id);
  }

  printBatchInvoice(inv: any) {
    const all = this.clearanceBatchInvoices();
    this.clearanceBatchInvoices.set([inv]);
    this.cdr.detectChanges();
    if (inv.id) {
      window.onafterprint = () => {
        this.invoiceService.updatePrintStatus(inv.id, 'printed').pipe(
          takeUntilDestroyed(this.destroyRef)
        ).subscribe({ error: () => {} });
        window.onafterprint = null;
        this.clearanceBatchInvoices.set(all);
      };
    }
    printWhenImagesReady('.invoice-print-page', () => {});
  }

  printAllBatchClearances() {
    this.showBatchClearanceView.set(false);
    this.cdr.detectChanges();
    const invoices = this.clearanceBatchInvoices();
    window.onafterprint = () => {
      invoices.forEach((inv: any) => {
        if (inv.id) {
          this.invoiceService.updatePrintStatus(inv.id, 'printed').pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe({ error: () => {} });
        }
      });
      window.onafterprint = null;
    };
    printWhenImagesReady('.invoice-print-page', () => {});
  }

  printClearance() {
    const lib = this.clearanceLibrary();
    const semesterId = this.settingsService.getActiveSemesterId();
    if (semesterId == null) { this.toast.show('الرجاء تحديد فصل دراسي فعال', 'error'); return; }

    if (lib && lib.id) {
      this.invoiceService.createClearance({
        libraryId: lib.id,
        semesterId
      }).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: (res) => {
          const invoice = res.data!;
          this.clearanceToPrint.set(invoice);
          this.activityService.logActivity('مخالصة', `تم إنشاء مخالصة للمكتبة "${invoice.libraryName}" بقيمة ${invoice.totalAmount} ريال`, 'ADD', { entity: 'invoice', id: invoice.id });
          this.toast.show('تم تسجيل المخالصة بنجاح', 'success');
          this.cdr.detectChanges();
          if (invoice.id) {
          window.onafterprint = () => {
            this.invoiceService.updatePrintStatus(invoice.id!, 'printed').pipe(
              takeUntilDestroyed(this.destroyRef)
            ).subscribe({ error: () => {} });
            window.onafterprint = null;
          };
          }
          printWhenImagesReady('.invoice-print-page', () => {
            this.clearanceToPrint.set(null);
            this.closeClearance();
          });
        },
        error: (err: any) => {
          this.toast.show(err.error?.message || 'حدث خطأ في إنشاء المخالصة', 'error');
        }
      });
    } else {
      this.confirmService.confirm('سيتم إنشاء مخالصة لكل مكتبة لديها رصيد مستحق. هل تريد المتابعة؟').pipe(
        filter(result => !!result),
        switchMap(() => this.invoiceService.createBatchClearance(semesterId)),
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: (res) => {
          const result = res.data!;
          const count = result.count;
          const invoices = result.invoices;
          this.clearanceBatchInvoices.set(invoices);
          (invoices || []).forEach((inv: any) => {
            this.activityService.logActivity('مخالصة', `تم إنشاء مخالصة للمكتبة "${inv.libraryName}" بقيمة ${inv.totalAmount} ريال`, 'ADD', { entity: 'invoice', id: inv.id });
          });
          this.toast.show(`تم تسجيل ${count} مخالصة بنجاح`, 'success');
          this.showBatchClearanceView.set(true);
        },
        error: (err: any) => {
          this.toast.show(err.error?.message || 'حدث خطأ في إنشاء المخالصات', 'error');
        }
      });
    }
  }

  selectedLogoData: string | null = null;
  private pendingLogoFile: File | null = null;
  triggerLogoUpload(fileInput: HTMLInputElement) { fileInput.click(); }

  onLogoSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      this.pendingLogoFile = file;
      this.selectedLogoData = URL.createObjectURL(file);
      this.toast.show('تم تحديد الشعار بنجاح!', 'success');
    }
  }

  saveLibrary() {
    if (!this.libraryName.trim()) { this.toast.show('الرجاء إدخال اسم المكتبة', 'error'); return; }
    if (!this.selectedGovernorateId) { this.toast.show('الرجاء اختيار المحافظة', 'error'); return; }
    if (!this.selectedCityId) { this.toast.show('الرجاء اختيار الولاية', 'error'); return; }

    const newLib: Partial<Library> = {
      name: this.libraryName,
      governorateId: this.selectedGovernorateId,
      cityId: this.selectedCityId,
      ownerName: this.ownerName,
      ownerPhone: this.ownerPhone,
      responsibleName: this.responsibleName,
      responsiblePhone: this.responsiblePhone,
      landlinePhone: this.landlinePhone || undefined,
      shift1Start: this.shift1Start,
      shift1End: this.shift1End,
      shift2Start: this.shift2Start || undefined,
      shift2End: this.shift2End || undefined
    };

    this.libraryService.addLibrary(newLib).pipe(
      switchMap((res: any) => {
        const libId = res.data?.id ?? res.id;
        if (libId && this.pendingLogoFile) {
          const file = this.pendingLogoFile;
          this.pendingLogoFile = null;
          return this.libraryService.uploadLogo(libId, file).pipe(map(() => res));
        }
        return of(res);
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (res: any) => {
        const libId = res.data?.id ?? res.id;
        this.activityService.logActivity('إضافة مكتبة', `تم إضافة مكتبة جديدة باسم: ${this.libraryName}`, 'ADD', { entity: 'library', id: libId });
        this.libraryName = '';
        this.ownerName = '';
        this.ownerPhone = '';
        this.responsibleName = '';
        this.responsiblePhone = '';
        this.landlinePhone = '';
        this.selectedLogoData = null;
        this.selectedGovernorateId = 0;
        this.selectedGovName = '';
        this.selectedCityId = 0;
        this.selectedCityName = '';
        this.toast.show('تم حفظ المكتبة بنجاح!', 'success');
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في حفظ المكتبة', 'error');
      }
    });
  }

  // ===== Receipt Voucher Methods =====

  openReceiptVoucher(lib: Library) {
    this.receiptVoucherLibrary.set(lib);
    this.rvAmount = 0;
    this.rvPaymentMethod = 'cash';
    this.rvChequeNumber = '';
    this.rvBankName = '';
    this.rvPurpose = '';
    this.rvDate = new Date().toISOString().split('T')[0];

    // Try to pre-fill amount from clearance if available
    const semesterId = this.settingsService.getActiveSemesterId();
    if (semesterId && lib.id) {
      this.invoiceService.getClearancePreview(semesterId, lib.id).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: (res) => {
          const preview = res.data!;
          if (preview.totalAmount > 0) {
            this.rvAmount = preview.totalAmount;
            this.rvPurpose = `تسوية حساب الفصل الدراسي ${preview.semesterName || ''}`;
          }
        },
        error: () => {} // Ignore — user can fill manually
      });
    }

    this.isReceiptVoucherModalOpen = true;
  }

  closeReceiptVoucher() {
    this.isReceiptVoucherModalOpen = false;
    this.receiptVoucherToPrint.set(null);
  }

  saveAndPrintReceiptVoucher() {
    const lib = this.receiptVoucherLibrary();
    if (!lib) return;

    if (!this.rvAmount || this.rvAmount <= 0) {
      this.toast.show('الرجاء إدخال مبلغ صحيح', 'error');
      return;
    }
    if (!this.rvPurpose.trim()) {
      this.toast.show('الرجاء إدخال الغرض من سند القبض', 'error');
      return;
    }
    if (this.rvPaymentMethod === 'cheque' && !this.rvChequeNumber.trim()) {
      this.toast.show('الرجاء إدخال رقم الشيك', 'error');
      return;
    }

    const voucherData = {
      libraryId: lib.id,
      semesterId: this.settingsService.getActiveSemesterId() || undefined,
      amount: this.rvAmount,
      paymentMethod: this.rvPaymentMethod,
      chequeNumber: this.rvPaymentMethod === 'cheque' ? this.rvChequeNumber : undefined,
      bankName: this.rvPaymentMethod === 'cheque' ? this.rvBankName : undefined,
      purpose: this.rvPurpose,
      date: new Date(this.rvDate).toISOString()
    };

    this.receiptVoucherService.create(voucherData).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (res) => {
        const voucher = res.data!;
        this.activityService.logActivity('سند قبض', `تم إنشاء سند قبض للمكتبة "${lib.name}" بقيمة ${this.rvAmount} ريال`, 'ADD', { entity: 'invoice', id: voucher.id });
        this.toast.show('تم إنشاء سند القبض بنجاح', 'success');
        this.receiptVoucherToPrint.set(voucher);
        this.cdr.detectChanges();
        printWhenImagesReady('.receipt-voucher-print-page', () => {
          this.closeReceiptVoucher();
        });
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في إنشاء سند القبض', 'error');
      }
    });
  }
}
