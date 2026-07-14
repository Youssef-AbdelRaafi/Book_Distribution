export interface ActivityPayload {
  entity: 'library' | 'inventory' | 'invoice';
  id?: number;
  previous?: Record<string, unknown>;
  current?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface Activity {
  id: string;
  action: string;
  details: string;
  timestamp: string;
  type?: 'ADD' | 'UPDATE' | 'DELETE' | 'GENERAL';
  payload?: ActivityPayload;
  status?: 'active' | 'undone';
}
