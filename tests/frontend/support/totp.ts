import { createHmac } from 'node:crypto';

const ALFABETO = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';

/**
 * Código de seis dígitos do aplicativo autenticador (RFC 6238), calculado como o celular
 * calcula, a partir da chave que a tela de Conta mostra.
 */
export function codigoTotp(chave: string, instante = Date.now()): string {
  const contador = Buffer.alloc(8);
  contador.writeBigUInt64BE(BigInt(Math.floor(instante / 30_000)));

  const hash = createHmac('sha1', base32(chave)).update(contador).digest();
  const deslocamento = hash[hash.length - 1] & 0x0f;
  const valor = (hash.readUInt32BE(deslocamento) & 0x7fffffff) % 1_000_000;
  return valor.toString().padStart(6, '0');
}

function base32(chave: string): Buffer {
  const bytes: number[] = [];
  let acumulado = 0;
  let bits = 0;

  for (const letra of chave.replace(/[\s=]/g, '').toUpperCase()) {
    acumulado = (acumulado << 5) | ALFABETO.indexOf(letra);
    bits += 5;
    if (bits >= 8) {
      bits -= 8;
      bytes.push(acumulado >> bits);
      acumulado &= (1 << bits) - 1;
    }
  }

  return Buffer.from(bytes);
}
