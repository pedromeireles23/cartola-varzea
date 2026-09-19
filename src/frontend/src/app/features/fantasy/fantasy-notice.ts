import { Injectable } from '@angular/core';

/**
 * Recado de uma tela do jogo para a próxima. O mercado aberto a partir de uma vaga
 * volta ao campo depois da compra, e é o campo que precisa anunciar o que entrou.
 * O recado é lido uma vez só: recarregar a página não o repete.
 */
@Injectable({ providedIn: 'root' })
export class FantasyNotice {
  private recado: string | null = null;

  deixar(texto: string): void {
    this.recado = texto;
  }

  retirar(): string | null {
    const texto = this.recado;
    this.recado = null;
    return texto;
  }
}
