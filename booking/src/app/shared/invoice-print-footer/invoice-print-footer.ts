import { Component, Input } from '@angular/core';
import { ASSET_URLS } from '../../core/constants/asset-urls';

@Component({
  selector: 'app-invoice-print-footer',
  standalone: true,
  template: `
    <div class="mt-8 border-t-2 border-black pt-6">
      <div class="flex justify-between items-start mb-6">
        <!-- Right Column: Receiver Info + Instructions -->
        <div class="text-[20px] font-bold leading-loose text-right">
          <p>توقيع المستلم / ----------------------------</p>
          <p class="mt-2">الختم /</p>
          <p class="mt-4">اسم المسؤول عن المكتبة/ <span class="text-black text-[20px] font-extrabold">{{ responsibleName || '—' }}</span></p>
          <p class="mt-2 mb-6">رقم المسؤول / <span class="text-black text-[20px] font-extrabold">{{ responsiblePhone || '—' }}</span></p>
        </div>


        <!-- Left Column: Owner Info -->
        <div class="text-[20px] font-bold leading-loose text-left">
          <p>اسم وتوقيع صاحب السلسلة/ <span class="text-black text-[20px] font-extrabold">{{ ownerName }}</span></p>
          <div class="mt-2 pr-14">
            
            <!-- Stamp and Signature container (stuck together) -->
            <span class="flex gap-0" dir="ltr">
              
              <!-- Stamp on the left -->
              <img [src]="assetUrls.seriesStamp" class="h-[110px] w-[110px] object-contain z-10" alt="ختم السلسلة">
              <!-- Signature on the right -->
              <img [src]="assetUrls.signatureStamp" class="h-16 object-contain -ml-6" alt="توقيع صاحب السلسلة">
              <p class="mb-1">/الختم </p>
            </span>
          </div>
        </div>
      </div>
          <!-- Bottom Instructions (Now on the right) -->
          <div class="text-[20px] font-bold leading-relaxed">
            <p class="text-black font-extrabold">فضلا بعد استلام الكتب ومراجعة أعدادها مع الفاتورة.</p>
            <p class="text-black font-extrabold">❶ قم بتوقيع الفاتورة وختمها بختم المكتبة.</p>
            <p class="text-black font-extrabold">❷ قم بتصوير الفاتورة وإرسال صورة منها على الواتس على الرقم <span class="font-mono">91913020</span></p>
          </div>
    </div>
  `
})
export class InvoicePrintFooterComponent {
  @Input() responsibleName = '';
  @Input() responsiblePhone = '';
  @Input() ownerName = 'مدحت محمد عبد الستار';

  readonly assetUrls = ASSET_URLS;
}
