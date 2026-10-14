/** Iniciais do escudo, sem as preposições do nome: "União da Vila" vira "UV". */
export function teamInitials(nome: string): string {
  const partes = nome
    .split(/\s+/)
    .filter(Boolean)
    .filter(
      (parte) => !['da', 'de', 'do', 'das', 'dos'].includes(parte.toLocaleLowerCase('pt-BR')),
    );
  return (partes.length > 0 ? partes : [nome])
    .slice(0, 2)
    .map((parte) => parte[0])
    .join('')
    .toLocaleUpperCase('pt-BR');
}
