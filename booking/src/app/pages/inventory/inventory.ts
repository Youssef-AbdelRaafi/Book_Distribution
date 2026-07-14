import { Component, inject, signal, computed, Input, effect, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { InventoryService } from '../../core/services/inventory.service';
import { Book } from '../../core/models/inventory.model';
import { FormsModule } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { SettingsService } from '../../core/services/settings.service';
import { ToastService } from '../../core/services/toast.service';
import { ActivityService } from '../../core/services/activity.service';
import { LS_INVNT_LIST_COLLAPSED } from '../../core/constants/local-storage-keys';

@Component({
  selector: 'app-inventory',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './inventory.html'
})
export class InventoryComponent {
  @Input() isCompact = false;
  trackByIndex = (i: number) => i;  
  public inventoryService = inject(InventoryService);
  public settingsService = inject(SettingsService);
  private toastService = inject(ToastService);
  private activityService = inject(ActivityService);

  getStockStatus(qty: number): string {
    if (qty > 50) return 'high';
    if (qty > 10) return 'medium';
    return 'low';
  }

  // Filters
  selectedSubject = signal('كل المواد');
  selectedGrade = signal('كل الصفوف');
  selectedTerm = signal('كل الأترام');

  isListCollapsed = signal(localStorage.getItem(LS_INVNT_LIST_COLLAPSED) === 'true');
  toggleList() {
    this.isListCollapsed.set(!this.isListCollapsed());
    localStorage.setItem(LS_INVNT_LIST_COLLAPSED, String(this.isListCollapsed()));
  }

  onFilterChange() {
    if (this.isListCollapsed()) {
      this.toggleList();
    }
  }

  isEditMode = signal(false);
  draftPrice = signal<number | null>(null);

  inventoryList = signal<Book[]>([]);

  // Unique lists for filters
  subjectsList = computed(() => {
    const subjects = this.inventoryList().map(b => b.subject).filter(s => !!s);
    return ['كل المواد', ...Array.from(new Set(subjects))];
  });

  gradesList = computed(() => {
    const grades = this.inventoryList().map(b => b.grade).filter(g => !!g);
    return ['كل الصفوف', ...Array.from(new Set(grades))];
  });

  private destroyRef = inject(DestroyRef);

  constructor() {
    this.inventoryService.inventory$.pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe(items => {
      this.inventoryList.set(items);
    });
    effect(() => {
      const activeCode = this.settingsService.activeSemester()?.code;
      if (activeCode === 'A') this.selectedTerm.set('الفصل الأول');
      else if (activeCode === 'B') this.selectedTerm.set('الفصل الثاني');
    });
  }

  filteredInventory = computed(() => {
    const activeYear = this.settingsService.activeSemester()?.academicYearName;
    const activeSemIds = this.settingsService.allSemesters()
      .filter(s => s.academicYearName === activeYear)
      .map(s => s.id);
    const sem1Id = this.settingsService.allSemesters().find(s =>
      s.name === 'الفصل الأول' && (!activeYear || s.academicYearName === activeYear))?.id;
    const sem2Id = this.settingsService.allSemesters().find(s =>
      s.name === 'الفصل الثاني' && (!activeYear || s.academicYearName === activeYear))?.id;

    return this.inventoryList().filter(item => {
      const matchSubject = this.selectedSubject() === 'كل المواد' || item.subject === this.selectedSubject();
      const matchGrade = this.selectedGrade() === 'كل الصفوف' || item.grade === this.selectedGrade();
      const matchTerm = this.selectedTerm() === 'كل الأترام'
        ? activeSemIds.includes(item.semesterId)
        : (this.selectedTerm() === 'الفصل الأول' ? item.semesterId === sem1Id : item.semesterId === sem2Id);
      return matchSubject && matchGrade && matchTerm;
    });
  });

  trackByBookId(index: number, item: Book): number {
    return item.id;
  }

  toggleEditMode() {
    this.isEditMode.set(!this.isEditMode());
  }

  updatePrice(book: Book, newPrice: number | null) {
    if (newPrice === null || newPrice < 0) return;
    if (!book.id) return;

    this.inventoryService.updateBook(book.id, { price: newPrice }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.activityService.logActivity('تحديث السعر', `تم تحديث سعر ${book.name} إلى ${newPrice}`, 'UPDATE');
        this.toastService.show('تم تحديث السعر بنجاح', 'success');
      },
      error: (err) => this.toastService.show(err.error?.message || 'تعذر تحديث السعر', 'error')
    });
  }

  updateStockQuantity(book: Book, newStock: number | null) {
    if (newStock === null || newStock < 0) return;
    if (!book.id) return;

    this.inventoryService.updateBook(book.id, { stockQuantity: newStock }).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.activityService.logActivity('تحديث المخزون', `تم تحديث مخزون ${book.name} إلى ${newStock}`, 'UPDATE');
        this.toastService.show('تم تحديث المخزون بنجاح', 'success');
      },
      error: (err) => this.toastService.show(err.error?.message || 'تعذر تحديث المخزون', 'error')
    });
  }

  showAddModal = signal(false);
  newBook = {
    name: '',
    grade: '',
    subject: '',
    price: 0,
    stockQuantity: 0
  };

  openAddModal() {
    this.showAddModal.set(true);
  }

  closeAddModal() {
    this.showAddModal.set(false);
    this.newBook = { name: '', grade: '', subject: '', price: 0, stockQuantity: 0 };
  }

  saveNewBook() {
    if (!this.newBook.name || !this.newBook.grade || !this.newBook.subject) {
      this.toastService.show('الرجاء إكمال البيانات الأساسية', 'error');
      return;
    }

    const semesterId = this.settingsService.activeSemester()?.id || this.settingsService.allSemesters()[0]?.id;
    if (!semesterId) {
      this.toastService.show('لا يوجد فصل دراسي نشط متاح', 'error');
      return;
    }

    const bookToAdd = {
      name: this.newBook.name,
      grade: this.newBook.grade,
      subject: this.newBook.subject,
      price: this.newBook.price,
      stockQuantity: this.newBook.stockQuantity,
      semesterId
    };

    this.inventoryService.addBook(bookToAdd).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.activityService.logActivity('إضافة كتاب', `تم إضافة ${bookToAdd.name} للمخزون`, 'ADD');
        this.toastService.show('تم إضافة الكتاب للمخزون العام بنجاح', 'success');
        this.closeAddModal();
      },
      error: (err) => this.toastService.show(err.error?.message || 'تعذر إضافة الكتاب', 'error')
    });
  }
}
