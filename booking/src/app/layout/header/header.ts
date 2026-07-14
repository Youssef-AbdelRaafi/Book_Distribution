import { Component, OnInit, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { tap, filter, switchMap } from 'rxjs/operators';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ThemeService } from '../../core/services/theme.service';
import { SettingsService, PrintSettings } from '../../core/services/settings.service';
import { AuthService } from '../../core/services/auth.service';
import { ActivityService } from '../../core/services/activity.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';
import { AppDataService } from '../../core/services/app-data.service';
import { ToastService } from '../../core/services/toast.service';
import { ConfirmService } from '../../core/services/confirm.service';
import { environment } from '../../../environments/environment';
import { LS_INV_IS_MERGED } from '../../core/constants/local-storage-keys';

@Component({
  selector: 'app-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './header.html'
})
export class HeaderComponent implements OnInit {
  isSettingsOpen = false;
  isActivityLogOpen = false;
  trackById = (i: number, item: any) => item?.id ?? i;
  trackByIndex = (i: number) => i;
  isHelpOpen = false;
  themeService = inject(ThemeService);
  settingsService = inject(SettingsService);
  authService = inject(AuthService);
  activityService = inject(ActivityService);
  router = inject(Router);
  appDataService = inject(AppDataService);
  http = inject(HttpClient);
  toast = inject(ToastService);
  confirmService = inject(ConfirmService);
  readonly assetUrls = ASSET_URLS;
  private destroyRef = inject(DestroyRef);
  allYears: any[] = [];

  ngOnInit() {
          this.fetchAcademicYears().pipe(
            takeUntilDestroyed(this.destroyRef)
          ).subscribe({ error: () => {} });
  }

  get currentYearName(): string {
    return this.settingsService.activeSemester()?.academicYearName || '';
  }

  get currentTermCode(): string {
    return this.settingsService.activeSemester()?.code || '';
  }

  onTermChange(event: Event) {
    const target = event.target as HTMLSelectElement | null;
    const code = target?.value;
    if (code) {
      this.settingsService.activateSemesterByCode(code).pipe(
        takeUntilDestroyed(this.destroyRef)
      ).subscribe({
        next: () => {
          this.appDataService.loadAuthenticatedData();
        },
        error: (err: any) => {
          this.toast.show(err.error?.message || 'حدث خطأ أثناء تغيير الترم', 'error');
        }
      });
    }
  }

  editPrintSettings: PrintSettings = { ...this.settingsService.printSettings() };
  editSectionOrder: string[] = [...this.settingsService.sectionOrder()];

  changePasswordData = {
    currentPassword: '',
    newPassword: '',
    confirmPassword: ''
  };

  changePassword() {
    const { currentPassword, newPassword, confirmPassword } = this.changePasswordData;

    if (!currentPassword || !newPassword || !confirmPassword) {
      this.toast.show('الرجاء إدخال جميع الحقول', 'error');
      return;
    }

    if (newPassword.length < 6) {
      this.toast.show('يجب أن تكون كلمة المرور الجديدة 6 أحرف على الأقل', 'error');
      return;
    }

    if (newPassword !== confirmPassword) {
      this.toast.show('كلمة المرور الجديدة وتأكيدها غير متطابقين', 'error');
      return;
    }

    this.authService.changePassword(currentPassword, newPassword).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => {
        this.toast.show('تم تغيير كلمة المرور بنجاح', 'success');
        this.changePasswordData = { currentPassword: '', newPassword: '', confirmPassword: '' };
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ أثناء تغيير كلمة المرور', 'error');
      }
    });
  }



  openActivityLog() {
    this.isActivityLogOpen = true;
  }

  closeActivityLog() {
    this.isActivityLogOpen = false;
  }

  undoActivity(activityId: string) {
    this.activityService.undoActivity(activityId);
  }

  redoActivity(activityId: string) {
    this.activityService.redoActivity(activityId);
  }

  openHelp() {
    this.isHelpOpen = true;
  }

  closeHelp() {
    this.isHelpOpen = false;
  }

  openSettings() {
    this.editPrintSettings = { ...this.settingsService.printSettings() };
    this.editSectionOrder = [...this.settingsService.sectionOrder()];
    this.isSettingsOpen = true;
  }

  savePrintSettings() {
    this.settingsService.updatePrintSettings(this.editPrintSettings).pipe(
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: () => this.toast?.show?.('تم حفظ الإعدادات', 'success'),
      error: () => {}
    });
    this.settingsService.updateSectionOrder(this.editSectionOrder);
    this.closeSettings();
  }

  getSectionName(id: string): string {
    const isMerged = localStorage.getItem(LS_INV_IS_MERGED) === 'true';
    const names: Record<string, string> = {
      'lib-form': 'إضافة مكتبة',
      'lib-list': 'قائمة المكتبات',
      'inv-form': isMerged ? 'الفواتير والطلبات' : 'العمليات',
      'inv-list': 'الفواتير المسجلة',
      'inventory': 'المخزون',
      'dashboard': 'تحليل التقدم'
    };
    return names[id] || id;
  }

  get filteredEditSectionOrder(): string[] {
    const isMerged = localStorage.getItem(LS_INV_IS_MERGED) === 'true';
    return this.editSectionOrder.filter(id => {
      if (isMerged && id === 'inv-list') return false;
      return true;
    });
  }

  moveUp(id: string) {
    const index = this.editSectionOrder.indexOf(id);
    if (index > 0) {
      const temp = this.editSectionOrder[index - 1];
      this.editSectionOrder[index - 1] = this.editSectionOrder[index];
      this.editSectionOrder[index] = temp;
    }
  }

  moveDown(id: string) {
    const index = this.editSectionOrder.indexOf(id);
    if (index > -1 && index < this.editSectionOrder.length - 1) {
      const temp = this.editSectionOrder[index + 1];
      this.editSectionOrder[index + 1] = this.editSectionOrder[index];
      this.editSectionOrder[index] = temp;
    }
  }

  startNewYear() {
    const nextYear = new Date().getFullYear();
    const message = `⚠️ تحذير هام ⚠️\n\n` +
      `هل أنت متأكد من إغلاق العام الدراسي الحالي وبدء عام جديد (${nextYear}-${nextYear+1})؟\n\n` +
      `سيتم:\n` +
      `• إيقاف جميع الفصول السابقة\n` +
      `• إنشاء عام دراسي جديد\n` +
      `• نسخ الكتالوج من العام السابق برصيد صفر\n\n` +
      `⚠️ هذا الإجراء لا يمكن التراجع عنه مباشرة، لكن يمكنك الرجوع لأي سنة سابقة من الإعدادات.\n\n` +
      `هل تريد المتابعة؟`;
    
    this.confirmService.confirm(message).pipe(
      filter(result => !!result),
      switchMap(() => this.settingsService.startNewYear(nextYear)),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      next: (res: any) => {
        this.toast.show('تم بدء العام الدراسي الجديد بنجاح', 'success');
        window.location.reload();
      },
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ أثناء بدء العام الدراسي الجديد', 'error');
      }
    });
  }

  fetchAcademicYears() {
    return this.http.get<any>(`${environment.apiUrl}/academic-years`).pipe(
      tap((res) => {
        this.allYears = res.data;
      })
    );
  }

  activateYear(event: Event) {
    const target = event.target as HTMLSelectElement | null;
    const yearId = target?.value;
    if (!yearId) return;

    const selectedYear = this.allYears.find((y: any) => y.id == yearId);
    if (!selectedYear) return;

    this.confirmService.confirm(`هل أنت متأكد من تفعيل السنة الدراسية "${selectedYear.name}"؟\n\nسيتم:\n• تفعيل السنة المختارة\n• تفعيل الفصل الأول منها\n• إيقاف السنة الحالية`).pipe(
      filter(result => !!result),
      switchMap(() => this.http.put<any>(`${environment.apiUrl}/academic-years/${yearId}/activate`, {})),
      tap(() => {
        this.toast.show(`تم تفعيل السنة الدراسية ${selectedYear.name} بنجاح`, 'success');
        this.settingsService.reloadAfterAuth();
        this.appDataService.loadAuthenticatedData();
        this.closeSettings();
      }),
      switchMap(() => this.fetchAcademicYears()),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe({
      error: (err: any) => {
        this.toast.show(err.error?.message || 'حدث خطأ أثناء تفعيل السنة الدراسية', 'error');
      }
    });

    // Reset select
    const resetTarget = event.target as HTMLSelectElement | null;
    if (resetTarget) resetTarget.value = '';
  }

  closeSettings() {
    this.isSettingsOpen = false;
  }
}
