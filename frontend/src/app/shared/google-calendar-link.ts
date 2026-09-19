// Monta uma URL de "quick add" do Google Calendar — abre o evento pré-preenchido pro usuário
// clicar em Salvar. Não precisa de OAuth/API key: é só uma URL pública do Google Calendar.
export function buildGoogleCalendarLink(params: {
  title: string;
  date: string; // yyyy-MM-dd
  details: string;
}): string {
  const start = params.date.replaceAll('-', '');
  const end = formatNextDay(params.date);

  const search = new URLSearchParams({
    action: 'TEMPLATE',
    text: params.title,
    dates: `${start}/${end}`,
    details: params.details,
  });

  return `https://calendar.google.com/calendar/render?${search.toString()}`;
}

function formatNextDay(date: string): string {
  const [year, month, day] = date.split('-').map(Number);
  const next = new Date(Date.UTC(year, month - 1, day + 1));
  const y = next.getUTCFullYear();
  const m = String(next.getUTCMonth() + 1).padStart(2, '0');
  const d = String(next.getUTCDate()).padStart(2, '0');
  return `${y}${m}${d}`;
}
