import { Component, Input, ChangeDetectionStrategy, inject } from '@angular/core';
import { SettingsService } from '../../core/services/settings.service';
import { ASSET_URLS } from '../../core/constants/asset-urls';

@Component({
  selector: 'app-invoice-print-footer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  standalone: true,
  template: `
    <div class="mt-4 border-t border-black pt-3">
      <div class="flex justify-between items-end mb-3">
        <!-- Right Column: Receiver Info -->
        <div class="text-[13px] font-bold leading-relaxed text-right">
          <p>توقيع المستلم / ----------------------------</p>
          <p class="mt-1">الختم /</p>
          <p class="mt-2">اسم المسؤول عن المكتبة/ <span class="text-black font-extrabold">{{ responsibleName || '—' }}</span></p>
          <p class="mt-1">رقم المسؤول / <span class="text-black font-extrabold">{{ responsiblePhone || '—' }}</span></p>
        </div>

        <!-- Left Column: Owner Info -->
        <div class="text-[13px] font-bold leading-relaxed text-left">
          <p>اسم وتوقيع صاحب السلسلة/ <span class="text-black font-extrabold">{{ ownerName }}</span></p>
        </div>
      </div>

      <div class="flex justify-center items-center gap-2 mt-2 mb-3" dir="ltr">
        <img [src]="assetUrls.seriesStamp" class="h-[90px] w-[90px] object-contain" alt="ختم السلسلة">
        <img [src]="assetUrls.signatureStamp" class="h-12 object-contain -ml-3" alt="توقيع صاحب السلسلة">
        <span class="text-[13px] font-bold mr-2">الختم/</span>
      </div>

      <!-- Bottom Instructions -->
      <div class="text-[13px] font-bold leading-relaxed mt-3 pt-2 border-t border-gray-300">
        <p>فضلا بعد استلام الكتب ومراجعة أعدادها مع الفاتورة.</p>
        <p>❶ قم بتوقيع الفاتورة وختمها بختم المكتبة.</p>
        <p>❷ قم بتصوير الفاتورة وإرسال صورة منها على الواتس على الرقم <span class="font-mono">{{ whatsappNumber }}</span></p>
      </div>
    </div>
  `
})
export class InvoicePrintFooterComponent {
  @Input() responsibleName = '';
  @Input() responsiblePhone = '';

  private settingsService = inject(SettingsService);

  get ownerName(): string {
    return this.settingsService.printSettings().ownerSignatureName;
  }

  get whatsappNumber(): string {
    return this.settingsService.printSettings().whatsappNumber;
  }

  readonly assetUrls = ASSET_URLS;
}
