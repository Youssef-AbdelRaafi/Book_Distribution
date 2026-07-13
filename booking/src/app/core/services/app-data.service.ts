import { Injectable, inject } from '@angular/core';
import { SettingsService } from './settings.service';
import { InventoryService } from './inventory.service';
import { LibraryService } from './library.service';
import { InvoiceService } from './invoice.service';
import { ReceiptVoucherService } from './receipt-voucher.service';

@Injectable({ providedIn: 'root' })
export class AppDataService {
  private settingsService = inject(SettingsService);
  private inventoryService = inject(InventoryService);
  private libraryService = inject(LibraryService);
  private invoiceService = inject(InvoiceService);
  private receiptVoucherService = inject(ReceiptVoucherService);

  loadAuthenticatedData(): void {
    this.settingsService.reloadAfterAuth();
    this.inventoryService.fetchBooks();
    this.libraryService.fetchLibraries();
    this.libraryService.fetchGovernorates();
    this.invoiceService.fetchInvoices();
    this.receiptVoucherService.fetchVouchers();
  }
}
