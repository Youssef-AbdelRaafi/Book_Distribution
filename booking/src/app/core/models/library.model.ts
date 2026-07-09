export interface Library {
  id: number;
  name: string;
  governorateId: number;
  cityId: number;
  governorateName?: string;
  cityName?: string;
  logo?: string;
  // بيانات صاحب المكتبة
  ownerName: string;
  ownerPhone: string;
  // بيانات المسؤول داخل المكتبة
  responsibleName: string;
  responsiblePhone: string;
  // تليفون ثابت
  landlinePhone?: string;
  // أوقات العمل - فترتين
  shift1Start: string;
  shift1End: string;
  shift2Start?: string;
  shift2End?: string;
  // تقييم المكتبة
  responseRating?: string; // سيئ / جيد / ممتاز
  paymentRating?: string;  // سيئ / جيد / ممتاز
  notes?: string;
  isActive: boolean;

  // Backward compat
  region?: string;
  city?: string;
  status?: string;
  workingHours?: string;
}

export interface Governorate {
  id: number;
  name: string;
  cities: City[];
}

export interface City {
  id: number;
  name: string;
  governorateId: number;
}
