import { DOCUMENT } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Meta, Title } from '@angular/platform-browser';

import { PageMetaService } from './page-meta';

describe('PageMetaService', () => {
  let meta: Meta;
  let origem: string;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    meta = TestBed.inject(Meta);
    origem = TestBed.inject(DOCUMENT).location.origin;
    TestBed.inject(PageMetaService).set({
      title: 'Copa da Vila',
      description: 'Tabela, partidas e ranking da Copa da Vila.',
    });
  });

  function conteudo(seletor: string): string | undefined {
    return meta.getTag(seletor)?.content;
  }

  it('põe a marca no título e repete título e descrição no cartão', () => {
    expect(TestBed.inject(Title).getTitle()).toBe('Copa da Vila · Cartola Várzea');
    expect(conteudo('property="og:title"')).toBe('Copa da Vila · Cartola Várzea');
    expect(conteudo('property="og:description"')).toBe(
      'Tabela, partidas e ranking da Copa da Vila.',
    );
    expect(conteudo('property="og:locale"')).toBe('pt_BR');
  });

  it('publica a imagem própria em endereço absoluto, com tamanho e texto alternativo', () => {
    expect(conteudo('property="og:image"')).toBe(`${origem}/og/cartola-varzea.jpg`);
    expect(conteudo('property="og:image:type"')).toBe('image/jpeg');
    expect(conteudo('property="og:image:width"')).toBe('1200');
    expect(conteudo('property="og:image:height"')).toBe('630');
    expect(conteudo('property="og:image:alt"')).toContain('sem apostas em dinheiro');
  });

  it('com imagem, o cartão do Twitter/X é o grande', () => {
    expect(conteudo('name="twitter:card"')).toBe('summary_large_image');
    expect(conteudo('name="twitter:image"')).toBe(`${origem}/og/cartola-varzea.jpg`);
  });
});
