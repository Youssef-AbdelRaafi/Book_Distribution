import { Component, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { LibrariesComponent } from '../libraries/libraries';
import { InvoicesComponent } from '../invoices/invoices';
import { InventoryComponent } from '../inventory/inventory';
import { DashboardComponent } from '../dashboard/dashboard';
import { ActivityService } from '../../core/services/activity.service';
import { RouterModule } from '@angular/router';
import { SettingsService } from '../../core/services/settings.service';

@Component({
  selector: 'app-single-page',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, LibrariesComponent, InvoicesComponent, InventoryComponent, DashboardComponent],
  templateUrl: './single-page.html'
})
export class SinglePageComponent {
  private activityService = inject(ActivityService);
  public settingsService = inject(SettingsService);
  
  isHistoryModalOpen = false;

  getOrder(section: string): number {
    const index = this.settingsService.sectionOrder().indexOf(section);
    return index !== -1 ? index : 99;
  }
  searchQuery = signal('');
  timeFilter = signal<'all'|'today'|'yesterday'|'week'>('all');

  activities = this.activityService.activities$;

  filteredActivities = computed(() => {
    let list = this.activities();
    
    // Text search
    const q = this.searchQuery().toLowerCase().trim();
    if (q) {
      list = list.filter(a => a.action.toLowerCase().includes(q) || a.details.toLowerCase().includes(q));
    }
    
    // Time filter
    const tf = this.timeFilter();
    if (tf !== 'all') {
      const now = new Date();
      list = list.filter(a => {
        const d = new Date(a.timestamp);
        if (tf === 'today') {
          return d.getDate() === now.getDate() && d.getMonth() === now.getMonth() && d.getFullYear() === now.getFullYear();
        } else if (tf === 'yesterday') {
          const yesterday = new Date(now);
          yesterday.setDate(yesterday.getDate() - 1);
          return d.getDate() === yesterday.getDate() && d.getMonth() === yesterday.getMonth() && d.getFullYear() === yesterday.getFullYear();
        } else if (tf === 'week') {
          const weekAgo = new Date(now);
          weekAgo.setDate(weekAgo.getDate() - 7);
          return d >= weekAgo;
        }
        return true;
      });
    }

    return list;
  });

  openHistory() {
    this.isHistoryModalOpen = true;
  }

  closeHistory() {
    this.isHistoryModalOpen = false;
  }

  undoActivity(id: string) {
    this.activityService.undoActivity(id);
  }

  redoActivity(id: string) {
    this.activityService.redoActivity(id);
  }

  canUndoLatest = computed(() => {
    return !!this.activities().find(a => a.status !== 'undone' && a.type);
  });

  canRedoLatest = computed(() => {
    return !!this.activities().find(a => a.status === 'undone' && a.type);
  });

  undoLatest() {
    const act = this.activities().find(a => a.status !== 'undone' && a.type);
    if (act) {
      this.undoActivity(act.id);
    }
  }

  redoLatest() {
    const act = this.activities().find(a => a.status === 'undone' && a.type);
    if (act) {
      this.redoActivity(act.id);
    }
  }
}
