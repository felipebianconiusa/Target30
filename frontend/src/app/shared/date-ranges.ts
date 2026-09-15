export interface DateRange {
  from: string;
  to: string;
}

function toIso(date: Date): string {
  return date.toISOString().slice(0, 10);
}

export function last7Days(): DateRange {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 6);
  return { from: toIso(from), to: toIso(to) };
}

export function last30Days(): DateRange {
  const to = new Date();
  const from = new Date();
  from.setDate(from.getDate() - 29);
  return { from: toIso(from), to: toIso(to) };
}

export function currentMonth(): DateRange {
  const now = new Date();
  const from = new Date(now.getFullYear(), now.getMonth(), 1);
  const to = new Date(now.getFullYear(), now.getMonth() + 1, 0);
  return { from: toIso(from), to: toIso(to) };
}

export function previousMonth(): DateRange {
  const now = new Date();
  const from = new Date(now.getFullYear(), now.getMonth() - 1, 1);
  const to = new Date(now.getFullYear(), now.getMonth(), 0);
  return { from: toIso(from), to: toIso(to) };
}
