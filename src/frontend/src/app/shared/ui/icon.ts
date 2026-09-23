import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import type { IconNode } from 'lucide';

/**
 * Ícone da biblioteca Lucide (06 §8): `<svg [appIcon]="House" />`.
 *
 * Recebe o desenho do pacote `lucide`, que é só dado, e o renderiza com o traço e o
 * tamanho do design system. Um componente único, em vez de um componente por ícone,
 * porque cada ícone do `@lucide/angular` carrega o próprio template compilado — cerca de
 * 4 kB por ícone contra meio kB aqui (medido em 2026-09-23).
 *
 * É sempre decorativo: o rótulo vai no texto ao lado ou no `aria-label` do botão, e o
 * ícone some do leitor de tela.
 */
@Component({
  // O componente é o próprio <svg>: só assim os filhos nascem no namespace SVG.
  // eslint-disable-next-line @angular-eslint/component-selector
  selector: 'svg[appIcon]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'lucide',
    xmlns: 'http://www.w3.org/2000/svg',
    viewBox: '0 0 24 24',
    fill: 'none',
    stroke: 'currentColor',
    'stroke-linecap': 'round',
    'stroke-linejoin': 'round',
    'aria-hidden': 'true',
    focusable: 'false',
    '[attr.width]': 'size()',
    '[attr.height]': 'size()',
    '[attr.stroke-width]': 'strokeWidth()',
  },
  template: `
    @for (node of appIcon(); track $index) {
      @let a = node[1];
      @switch (node[0]) {
        @case ('path') {
          <svg:path [attr.d]="a['d']" />
        }
        @case ('circle') {
          <svg:circle [attr.cx]="a['cx']" [attr.cy]="a['cy']" [attr.r]="a['r']" />
        }
        @case ('rect') {
          <svg:rect
            [attr.x]="a['x']"
            [attr.y]="a['y']"
            [attr.width]="a['width']"
            [attr.height]="a['height']"
            [attr.rx]="a['rx']"
            [attr.ry]="a['ry']"
          />
        }
        @case ('line') {
          <svg:line
            [attr.x1]="a['x1']"
            [attr.y1]="a['y1']"
            [attr.x2]="a['x2']"
            [attr.y2]="a['y2']"
          />
        }
        @case ('polyline') {
          <svg:polyline [attr.points]="a['points']" />
        }
        @case ('polygon') {
          <svg:polygon [attr.points]="a['points']" />
        }
        @case ('ellipse') {
          <svg:ellipse
            [attr.cx]="a['cx']"
            [attr.cy]="a['cy']"
            [attr.rx]="a['rx']"
            [attr.ry]="a['ry']"
          />
        }
      }
    }
  `,
})
export class Icon {
  readonly appIcon = input.required<IconNode>();

  /** 18 px no corpo do texto; 20 px em ação principal e navegação do celular. */
  readonly size = input(18);

  readonly strokeWidth = input(1.75);
}
