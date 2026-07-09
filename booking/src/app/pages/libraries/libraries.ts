import { Component, computed, signal, inject, ChangeDetectorRef, Input, NgZone } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LibraryService } from '../../core/services/library.service';
import { InvoiceService } from '../../core/services/invoice.service';
import { Invoice, InvoiceItem } from '../../core/models/invoice.model';
import { ToastService } from '../../core/services/toast.service';
import { ActivityService } from '../../core/services/activity.service';
import { Library, City } from '../../core/models/library.model';
import { SettingsService } from '../../core/services/settings.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';
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
  standalone: true,
  imports: [CommonModule, FormsModule, InvoicePrintFooterComponent],
  templateUrl: './libraries.html'
})
export class LibrariesComponent {
  @Input() isCompact = false;
  
  public settingsService = inject(SettingsService);
  public libraryService = inject(LibraryService);
  readonly assetUrls = ASSET_URLS;
  private invoiceService = inject(InvoiceService);
  private toast = inject(ToastService);
  private cdr = inject(ChangeDetectorRef);
  private activityService = inject(ActivityService);
  private zone = inject(NgZone);

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
  libraryInvoices = signal<Invoice[]>([]);

  isAddFormCollapsed = signal(localStorage.getItem('lib_addFormCollapsed') === 'true');
  toggleAddForm() {
    this.isAddFormCollapsed.set(!this.isAddFormCollapsed());
    localStorage.setItem('lib_addFormCollapsed', String(this.isAddFormCollapsed()));
  }

  isListCollapsed = signal(localStorage.getItem('lib_listCollapsed') === 'true');
  isListEditMode = signal(false);

  toggleList() {
    this.isListCollapsed.set(!this.isListCollapsed());
    localStorage.setItem('lib_listCollapsed', String(this.isListCollapsed()));
  }

  clearanceLibrary = signal<any>(null);
  clearanceItems = signal<{grade: string, items: ClearanceSummaryItem[]}[]>([]);
  clearanceTotal = signal<number>(0);
  clearanceDate = new Date().toLocaleDateString('ar-SA');
  currentClearanceNumber = signal<string>('');
  clearanceBatchInvoices = signal<Invoice[]>([]);

  Math = Math;

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

  constructor() {
    this.libraryService.libraries$.subscribe(items => {
      this.librariesList.set(items);
      this.cdr.detectChanges();
    });
  }

  // Filtered cities based on selected governorate
  filteredCities() {
    const govs = this.libraryService.governorates();
    const gov = govs.find(g => g.id == this.selectedGovernorateId);
    return gov?.cities || [];
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
    
    // Fetch invoices for this library from API
    this.invoiceService.getInvoicesByLibraryId(lib.id).subscribe({
      next: (res: any) => {
        const data = res.data || res;
        const invs = Array.isArray(data) ? data : [];
        invs.sort((a: any, b: any) => {
          const d1 = a.date ? new Date(a.date).getTime() : 0;
          const d2 = b.date ? new Date(b.date).getTime() : 0;
          return d2 - d1;
        });
        this.libraryInvoices.set(invs);
      },
      error: () => this.libraryInvoices.set([])
    });
    this.isDetailsModalOpen = true;
  }

  triggerEditLogoUpload(fileInput: HTMLInputElement) { fileInput.click(); }

  onEditLogoSelected(event: any) {
    const file = event.target.files[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = (e: any) => {
        this.zone.run(() => {
          this.editLibLogo = e.target.result;
          this.cdr.markForCheck();
          this.cdr.detectChanges();
          this.toast.show('تم تحديد الشعار الجديد بنجاح!', 'success');
        });
      };
      reader.readAsDataURL(file);
    }
  }

  saveEditedLibrary() {
    const lib = this.selectedLibraryForDetails();
    if (!lib) return;
    if (!this.editLibName.trim()) { this.toast.show('الرجاء إدخال اسم المكتبة', 'error'); return; }
    
    const updatedLib = { 
      ...lib, 
      name: this.editLibName, 
      logo: this.editLibLogo,
      responsibleName: this.editResponsibleName || '',
      responsiblePhone: this.editResponsiblePhone || '',
      responseRating: this.editResponseRating || undefined,
      paymentRating: this.editPaymentRating || undefined,
      notes: this.editLibraryNotes || undefined
    };
    this.libraryService.updateLibrary(lib.id, updatedLib).subscribe({
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
    if (confirm(`هل أنت متأكد من حذف المكتبة: ${lib.name}؟`)) {
      this.libraryService.deleteLibrary(lib.id).subscribe({
        next: () => {
          this.toast.show('تم حذف المكتبة بنجاح', 'success');
          this.closeDetails();
        },
        error: () => this.toast.show('حدث خطأ في حذف المكتبة', 'error')
      });
    }
  }

  deleteLibraryQuick(lib: Library, event: Event) {
    event.stopPropagation();
    if (confirm(`هل أنت متأكد من حذف المكتبة: ${lib.name}؟`)) {
      this.libraryService.deleteLibrary(lib.id).subscribe({
        next: () => this.toast.show('تم حذف المكتبة بنجاح', 'success'),
        error: () => this.toast.show('حدث خطأ في حذف المكتبة', 'error')
      });
    }
  }

  closeDetails() { this.isDetailsModalOpen = false; this.selectedLibraryForDetails.set(null); }

  clearance(lib?: Library) {
    if (!lib) {
      this.clearanceLibrary.set({ id: 0, name: 'جميع المكتبات' });
    } else {
      this.clearanceLibrary.set(lib);
    }
    
    const semesterId = this.settingsService.getActiveSemesterId();
    const libId = lib ? lib.id : undefined;

    this.invoiceService.getClearancePreview(semesterId, libId).subscribe({
      next: (res: any) => {
        const preview = res.data || res;
        this.clearanceTotal.set(preview.totalAmount);
        
        const grouped = new Map<string, any[]>();
        preview.items.forEach((item: any) => {
          const grade = item.bookGrade || 'أخرى';
          if (!grouped.has(grade)) { grouped.set(grade, []); }
          grouped.get(grade)!.push({
            id: item.bookId,
            name: item.bookName,
            grade: grade,
            netQty: item.quantity,
            price: item.unitPrice,
            total: item.total
          });
        });
        
        const groupedArray = Array.from(grouped.entries()).map(([grade, items]) => ({ grade, items }));
        this.clearanceItems.set(groupedArray);
        this.isClearanceModalOpen = true;
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في جلب بيانات المخالصة', 'error');
      }
    });
  }

  closeClearance() { this.isClearanceModalOpen = false; }

  printClearance() {
    const lib = this.clearanceLibrary();
    const semesterId = this.settingsService.getActiveSemesterId();

    if (lib && lib.id) {
      this.invoiceService.createClearance({
        libraryId: lib.id,
        semesterId
      }).subscribe({
        next: (res: any) => {
          const invoice = res.data || res;
          this.currentClearanceNumber.set(
            invoice.displayNumber || `${invoice.invoiceNumber || ''}${invoice.termCode || ''}`
          );
          this.toast.show('تم تسجيل المخالصة بنجاح', 'success');
          this.cdr.detectChanges();
          printWhenImagesReady('.clearance-print-page');
        },
        error: (err: any) => {
          this.toast.show(err.error?.message || 'حدث خطأ في إنشاء المخالصة', 'error');
        }
      });
    } else {
      if (!confirm('سيتم إنشاء مخالصة لكل مكتبة لديها رصيد مستحق. هل تريد المتابعة؟')) {
        return;
      }

      this.invoiceService.createBatchClearance(semesterId).subscribe({
        next: (res: any) => {
          const result = res.data || res;
          const count = result.count ?? result.Count ?? 0;
          const invoices = result.invoices ?? result.Invoices ?? [];
          this.clearanceBatchInvoices.set(invoices);
          this.toast.show(`تم تسجيل ${count} مخالصة بنجاح`, 'success');
          this.cdr.detectChanges();
          printWhenImagesReady('.clearance-print-page', () => {
             const success = window.confirm('هل تمت الطباعة بنجاح؟');
             if (success) {
                invoices.forEach((inv: any) => {
                   if (inv.id) this.invoiceService.updatePrintStatus(inv.id, 'printed').subscribe();
                });
             }
             this.closeClearance();
          });
        },
        error: (err: any) => {
          this.toast.show(err.error?.message || 'حدث خطأ في إنشاء المخالصات', 'error');
        }
      });
    }
  }

  selectedLogoData: string | null = null;
  triggerLogoUpload(fileInput: HTMLInputElement) { fileInput.click(); }

  onLogoSelected(event: Event) {
    const file = (event.target as HTMLInputElement).files?.[0];
    if (file) {
      const reader = new FileReader();
      reader.onload = (e) => {
        this.zone.run(() => {
          this.selectedLogoData = e.target?.result as string;
          this.cdr.markForCheck();
          this.cdr.detectChanges();
          this.toast.show('تم تحديد الشعار بنجاح!', 'success');
        });
      };
      reader.readAsDataURL(file);
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
      logo: this.selectedLogoData || undefined,
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

    this.libraryService.addLibrary(newLib).subscribe({
      next: () => {
        this.activityService.logActivity('إضافة مكتبة', `تم إضافة مكتبة جديدة باسم: ${this.libraryName}`, 'ADD');
        // Reset form
        this.libraryName = '';
        this.ownerName = '';
        this.ownerPhone = '';
        this.responsibleName = '';
        this.responsiblePhone = '';
        this.landlinePhone = '';
        this.selectedLogoData = null;
        this.toast.show('تم حفظ المكتبة بنجاح!', 'success');
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ في حفظ المكتبة', 'error');
      }
    });
  }
}
