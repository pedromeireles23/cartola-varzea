import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import qrcode from 'qrcode-generator';

/** Margem branca em volta do código, em módulos: os leitores pedem quatro. */
const MARGEM = 4;

/**
 * QR code desenhado pelo próprio template, em SVG: sem imagem gerada nem HTML injetado,
 * que a sanitização do Angular e a CSP barrariam. Cada módulo escuro é um trecho do mesmo
 * caminho. Fica nesta pasta, e não no kit, para a biblioteca só ir no pedaço do perfil.
 */
@Component({
  selector: 'app-qr-code',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <svg
      class="qr"
      role="img"
      [attr.aria-label]="label()"
      [attr.viewBox]="'0 0 ' + lado() + ' ' + lado()"
      shape-rendering="crispEdges"
    >
      <rect class="qr__fundo" [attr.width]="lado()" [attr.height]="lado()" />
      <path class="qr__modulos" [attr.d]="caminho()" />
    </svg>
  `,
  styles: `
    :host {
      display: block;
      width: min(100%, 220px);
    }

    .qr {
      display: block;
      width: 100%;
      height: auto;
    }

    // Preto no branco em qualquer tema: o leitor do celular não lê o código invertido.
    .qr__fundo {
      fill: var(--primitive-color-neutral-0);
    }

    .qr__modulos {
      fill: var(--primitive-color-graphite-1000);
    }
  `,
})
export class QrCode {
  /** O conteúdo do código. */
  readonly value = input.required<string>();

  /** O que o leitor de tela anuncia no lugar do desenho. */
  readonly label = input.required<string>();

  private readonly codigo = computed(() => {
    const qr = qrcode(0, 'M');
    qr.addData(this.value());
    qr.make();
    return qr;
  });

  protected readonly lado = computed(() => this.codigo().getModuleCount() + 2 * MARGEM);

  protected readonly caminho = computed(() => {
    const qr = this.codigo();
    const modulos = qr.getModuleCount();
    const trechos: string[] = [];

    for (let linha = 0; linha < modulos; linha++) {
      for (let coluna = 0; coluna < modulos; coluna++) {
        if (qr.isDark(linha, coluna)) {
          trechos.push(`M${coluna + MARGEM} ${linha + MARGEM}h1v1h-1z`);
        }
      }
    }

    return trechos.join('');
  });
}
