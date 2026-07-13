import { Injectable, signal, inject } from '@angular/core';
import { Activity, ActivityPayload } from '../models/activity.model';
import { InventoryService } from './inventory.service';
import { LibraryService } from './library.service';

@Injectable({
  providedIn: 'root'
})
export class ActivityService {
  private activitiesSignal = signal<Activity[]>([]);
  public readonly activities$ = this.activitiesSignal.asReadonly();
  private readonly storageKey = 'activity_log';
  private readonly maxActivities = 200;
  private inventoryService = inject(InventoryService);
  private libraryService = inject(LibraryService);

  constructor() {
    this.loadActivities(); 
  }

  private loadActivities() {
    const saved = localStorage.getItem(this.storageKey);
    if (saved) {
      try {
        this.activitiesSignal.set(JSON.parse(saved));
      } catch (e) {
        this.activitiesSignal.set([]);
      }
    }
  }

  logActivity(action: string, details: string, type?: 'ADD' | 'UPDATE' | 'DELETE' | 'GENERAL', payload?: ActivityPayload) {
    const newActivity: Activity = {
      id: crypto.randomUUID?.() ?? (Date.now().toString(36) + Math.random().toString(36).slice(2, 10)),
      action,
      details,
      timestamp: new Date().toISOString(),
      type,
      payload,
      status: 'active'
    };
    
    const updated = [newActivity, ...this.activitiesSignal()].slice(0, this.maxActivities);
    this.activitiesSignal.set(updated);
    localStorage.setItem(this.storageKey, JSON.stringify(updated));
  }

  undoActivity(activityId: string) {
    const activities = this.activitiesSignal();
    const index = activities.findIndex(a => a.id === activityId);
    if (index !== -1 && activities[index].status !== 'undone') {
      const activity = activities[index];
      
      // Execute compensation
      if (activity.type && activity.payload) {
        if (activity.payload?.entity === 'library') {
          this.libraryService.executeCompensation(activity);
        } else if (activity.payload?.entity === 'inventory' || !activity.payload?.entity) {
          this.inventoryService.executeCompensation(activity);
        }
      }

      // Update status
      const updated = [...activities];
      updated[index] = { ...activity, status: 'undone' };
      this.activitiesSignal.set(updated);
      localStorage.setItem(this.storageKey, JSON.stringify(updated));
    }
  }

  redoActivity(activityId: string) {
    const activities = this.activitiesSignal();
    const index = activities.findIndex(a => a.id === activityId);
    if (index !== -1 && activities[index].status === 'undone') {
      const activity = activities[index];
      
      // Execute redo
      if (activity.type && activity.payload) {
        if (activity.payload?.entity === 'library') {
          this.libraryService.executeRedo(activity);
        } else if (activity.payload?.entity === 'inventory' || !activity.payload?.entity) {
          this.inventoryService.executeRedo(activity);
        }
      }

      // Update status
      const updated = [...activities];
      updated[index] = { ...activity, status: 'active' };
      this.activitiesSignal.set(updated);
      localStorage.setItem(this.storageKey, JSON.stringify(updated));
    }
  }

  clearHistory() {
    this.activitiesSignal.set([]);
    localStorage.removeItem(this.storageKey);
  }
}
