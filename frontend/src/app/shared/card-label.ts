// Texto corrido (alertas, título de evento do Calendar, etc.): o apelido, se houver, sempre
// acompanhado do nome original do banco.
export function cardLabel(nickname: string | null | undefined, name: string): string {
  return nickname ? `${nickname} (${name})` : name;
}
