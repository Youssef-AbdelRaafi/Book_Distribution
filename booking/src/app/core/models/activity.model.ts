export interface Activity {
  id: string;
  action: string;
  details: string;
  timestamp: string;
  type?: 'ADD' | 'UPDATE' | 'DELETE' | 'GENERAL';
  payload?: any;
  status?: 'active' | 'undone';
}
