import { DOCUMENT, Injectable, inject } from '@angular/core';
import { Meta, Title } from '@angular/platform-browser';

/** O que uma página precisa dizer sobre si para busca e compartilhamento. */
export interface PageMeta {
  /** Sem o sufixo da marca: ele é acrescentado aqui, para não variar de tela em tela. */
  readonly title: string;
  readonly description: string;
  /** `article` em página de conteúdo; o resto do site é `website`. */
  readonly type?: 'website' | 'article';
}

const MARCA = 'Cartola Várzea';

/**
 * A imagem de compartilhamento (V5), gerada por `infra/og/gerar-og.mjs` a partir de um
 * HTML versionado, só com ativos próprios. Uma para o site todo: o que muda de página
 * para página é o título e a descrição do cartão.
 */
const IMAGEM = {
  caminho: '/og/cartola-varzea.jpg',
  tipo: 'image/jpeg',
  largura: '1200',
  altura: '630',
  alt: 'Cartola Várzea: monte, dispute, acompanhe. Fantasy de futebol amador, sem apostas em dinheiro.',
} as const;

/**
 * Título, descrição e metadados de compartilhamento de cada tela (Fase 8).
 *
 * A aplicação é renderizada no cliente, então o que um robô que não executa JavaScript
 * lê é o `index.html` — e é o caso do cartão de pré-visualização de WhatsApp e
 * Telegram, que é por onde um campeonato de várzea circula. Por isso o `index.html`
 * traz o cartão do site todo, e estas tags afinam título e descrição para quem executa
 * JavaScript, como os buscadores. A renderização no servidor fica para quando houver
 * necessidade medida, e está registrada no 03.
 *
 * A URL canônica sai do documento, e não de configuração, porque em demonstração o
 * endereço muda de ambiente para ambiente e uma constante ficaria mentindo.
 */
@Injectable({ providedIn: 'root' })
export class PageMetaService {
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);
  private readonly document = inject(DOCUMENT);

  set(page: PageMeta): void {
    const titulo = `${page.title} · ${MARCA}`;
    this.title.setTitle(titulo);

    this.meta.updateTag({ name: 'description', content: page.description });
    this.meta.updateTag({ property: 'og:site_name', content: MARCA });
    this.meta.updateTag({ property: 'og:title', content: titulo });
    this.meta.updateTag({ property: 'og:description', content: page.description });
    this.meta.updateTag({ property: 'og:type', content: page.type ?? 'website' });
    this.meta.updateTag({ property: 'og:url', content: this.url() });
    this.meta.updateTag({ property: 'og:locale', content: 'pt_BR' });

    // O Open Graph pede o endereço absoluto da imagem; ele sai da origem do documento,
    // como o canônico, porque o domínio muda de ambiente para ambiente.
    const imagem = this.origem() + IMAGEM.caminho;
    this.meta.updateTag({ property: 'og:image', content: imagem });
    this.meta.updateTag({ property: 'og:image:type', content: IMAGEM.tipo });
    this.meta.updateTag({ property: 'og:image:width', content: IMAGEM.largura });
    this.meta.updateTag({ property: 'og:image:height', content: IMAGEM.altura });
    this.meta.updateTag({ property: 'og:image:alt', content: IMAGEM.alt });
    this.meta.updateTag({ name: 'twitter:card', content: 'summary_large_image' });
    this.meta.updateTag({ name: 'twitter:image', content: imagem });
    this.meta.updateTag({ name: 'twitter:image:alt', content: IMAGEM.alt });

    this.canonical();
  }

  private url(): string {
    return this.document.location?.href ?? '';
  }

  private origem(): string {
    return this.document.location?.origin ?? '';
  }

  /**
   * Uma busca que chega por `?busca=` não deve competir com a lista sem filtro, então o
   * canônico aponta para o endereço sem a query.
   */
  private canonical(): void {
    const head = this.document.head;
    if (!head) {
      return;
    }

    const existente = head.querySelector<HTMLLinkElement>('link[rel="canonical"]');
    const link = existente ?? this.document.createElement('link');
    link.setAttribute('rel', 'canonical');
    link.setAttribute('href', this.origem() + (this.document.location?.pathname ?? ''));
    if (!existente) {
      head.appendChild(link);
    }
  }
}
