import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { ThemeService } from '../../core/services/theme.service';
import { SettingsService, PrintSettings } from '../../core/services/settings.service';
import { AuthService } from '../../core/services/auth.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule],
  templateUrl: './header.html'
})
export class HeaderComponent {
  isSettingsOpen = false;
  isHelpOpen = false;
  themeService = inject(ThemeService);
  settingsService = inject(SettingsService);
  authService = inject(AuthService);
  router = inject(Router);
  readonly assetUrls = ASSET_URLS;

  editPrintSettings: PrintSettings = { ...this.settingsService.printSettings() };
  editSectionOrder: string[] = [...this.settingsService.sectionOrder()];



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
    this.settingsService.updatePrintSettings(this.editPrintSettings);
    this.settingsService.updateSectionOrder(this.editSectionOrder);
    this.closeSettings();
  }

  getSectionName(id: string): string {
    const isMerged = localStorage.getItem('inv_isMerged') === 'true';
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
    const isMerged = localStorage.getItem('inv_isMerged') === 'true';
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
    if (confirm(`هل أنت متأكد من إغلاق العام الدراسي الحالي وبدء عام جديد (${nextYear}-${nextYear+1})؟ سيتم إيقاف جميع الفصول السابقة.`)) {
      this.settingsService.startNewYear(nextYear).subscribe({
        next: (res: any) => {
          alert('تم بدء العام الدراسي الجديد بنجاح');
          window.location.reload();
        },
        error: (err: any) => {
          alert(err.error?.message || 'حدث خطأ أثناء بدء العام الدراسي الجديد');
        }
      });
    }
  }

  closeSettings() {
    this.isSettingsOpen = false;
  }
}
