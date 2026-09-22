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
 * Título, descrição e metadados de compartilhamento de cada tela (Fase 8).
 *
 * A aplicação é renderizada no cliente, então o que um robô que não executa JavaScript
 * lê é o `index.html`; estas tags servem para quem executa — incluindo o cartão de
 * pré-visualização de WhatsApp e Telegram, que é por onde um campeonato de várzea
 * circula. A renderização no servidor fica para quando houver necessidade medida, e
 * está registrada no 03.
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
    // Sem imagem própria o cartão grande fica com um espaço vazio; o resumo não.
    this.meta.updateTag({ name: 'twitter:card', content: 'summary' });

    this.canonical();
  }

  private url(): string {
    return this.document.location?.href ?? '';
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
    link.setAttribute('href', this.document.location?.origin + this.document.location?.pathname);
    if (!existente) {
      head.appendChild(link);
    }
  }
}
