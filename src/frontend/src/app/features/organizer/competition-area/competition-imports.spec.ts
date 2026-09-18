import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { apiErrorInterceptor } from '../../../core/api/api-error.interceptor';
import { API_BASE_URL } from '../../../core/config/api-base-url';
import { CompetitionContext } from './competition-context';
import { campeonato } from './competition-fixtures';
import { CompetitionImportsPage } from './competition-imports';
import { ImportResult, ImportTemplate } from './import.service';

const TEMPLATES = '/api/v1/import-templates';
const PREVIEW = '/api/v1/competitions/c1/imports/times/preview';
const COMMIT = '/api/v1/competitions/c1/imports/times';

const MODELOS: ImportTemplate[] = [
  {
    kind: 'Teams',
    version: 1,
    label: 'times',
    fileName: 'times-v1.csv',
    columns: [{ name: 'nome', required: true, description: 'Nome do time.', example: 'União' }],
  },
  {
    kind: 'Athletes',
    version: 1,
    label: 'atletas',
    fileName: 'atletas-v1.csv',
    columns: [
      { name: 'nome_esportivo', required: true, description: 'Nome no jogo.', example: 'Bia' },
      { name: 'time', required: true, description: 'Time do atleta.', example: 'União' },
    ],
  },
  {
    kind: 'Coaches',
    version: 1,
    label: 'técnicos',
    fileName: 'tecnicos-v1.csv',
    columns: [{ name: 'time', required: true, description: 'Time.', example: 'União' }],
  },
  {
    kind: 'Matches',
    version: 1,
    label: 'partidas',
    fileName: 'partidas-v1.csv',
    columns: [{ name: 'rodada', required: true, description: 'Nome da rodada.', example: 'R1' }],
  },
];

function resultado(parcial: Partial<ImportResult['summary']> = {}): ImportResult {
  return {
    outcome: 'Completed',
    summary: { rows: 3, created: 3, updated: 0, unchanged: 0, ...parcial },
    issues: [],
    rejectionMessage: null,
  };
}

describe('CompetitionImportsPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CompetitionImportsPage],
      providers: [
        provideHttpClient(withInterceptors([apiErrorInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '/api/v1' },
        CompetitionContext,
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function abrir(
    papel: 'Owner' | 'Assistant' = 'Owner',
  ): Promise<ComponentFixture<CompetitionImportsPage>> {
    TestBed.inject(CompetitionContext).replace(campeonato({ viewerRole: papel }));
    const fixture = TestBed.createComponent(CompetitionImportsPage);
    await fixture.whenStable();
    http.expectOne(TEMPLATES).flush(MODELOS);
    await fixture.whenStable();
    return fixture;
  }

  function texto(fixture: ComponentFixture<CompetitionImportsPage>): string {
    return (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
  }

  function botao(
    fixture: ComponentFixture<CompetitionImportsPage>,
    rotulo: string,
  ): HTMLButtonElement | undefined {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>('button'),
    ].find((item) => item.textContent?.trim() === rotulo);
  }

  async function escolher(fixture: ComponentFixture<CompetitionImportsPage>): Promise<void> {
    const arquivo = new File(['nome\r\nUnião\r\n'], 'times.csv', { type: 'text/csv' });
    const entrada = (fixture.nativeElement as HTMLElement).querySelector('input[type=file]')!;

    // `DataTransfer` não existe no ambiente de teste; o componente só lê `files[0]`.
    Object.defineProperty(entrada, 'files', { configurable: true, value: [arquivo] });
    entrada.dispatchEvent(new Event('change'));
    await fixture.whenStable();
  }

  it('documenta as colunas do modelo escolhido e oferece o download', async () => {
    const fixture = await abrir();

    expect(texto(fixture)).toContain('Modelo times-v1.csv, versão 1.');
    expect(texto(fixture)).toContain('Nome do time.');

    const link = (fixture.nativeElement as HTMLElement).querySelector('a.acao');
    expect(link?.getAttribute('href')).toBe('/api/v1/competitions/c1/imports/times/template');
    expect(link?.getAttribute('download')).toBe('times-v1.csv');
  });

  it('trocar de aba mostra as colunas daquele modelo', async () => {
    const fixture = await abrir();

    botao(fixture, 'Atletas')?.click();
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Modelo atletas-v1.csv');
    expect(texto(fixture)).toContain('nome_esportivo');
  });

  it('importar só libera depois de conferir, e a prévia avisa que nada foi gravado', async () => {
    const fixture = await abrir();
    await escolher(fixture);

    expect(botao(fixture, 'Importar')?.disabled).toBe(true);
    expect(texto(fixture)).toContain('Confira o arquivo antes de importar.');

    botao(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    const previa = http.expectOne(PREVIEW);
    expect(previa.request.body instanceof FormData).toBe(true);
    previa.flush(resultado());
    await fixture.whenStable();

    expect(texto(fixture)).toContain('3 linhas lidas: 3 a criar, 0 a alterar, 0 sem mudança.');
    expect(texto(fixture)).toContain('Nada foi gravado ainda.');
    expect(botao(fixture, 'Importar')?.disabled).toBe(false);
  });

  it('importa o mesmo arquivo conferido e limpa a escolha', async () => {
    const fixture = await abrir();
    await escolher(fixture);
    botao(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    http.expectOne(PREVIEW).flush(resultado());
    await fixture.whenStable();

    botao(fixture, 'Importar')?.click();
    await fixture.whenStable();
    http.expectOne(COMMIT).flush(resultado({ created: 0, unchanged: 3 }));
    await fixture.whenStable();

    expect(texto(fixture)).toContain('3 linhas lidas: 0 criados, 0 alterados, 3 sem mudança.');
    expect(botao(fixture, 'Conferir arquivo')?.disabled).toBe(true);
  });

  it('partidas conferidas mostram as rodadas que o arquivo vai criar', async () => {
    const fixture = await abrir();
    botao(fixture, 'Partidas')?.click();
    await fixture.whenStable();
    expect(texto(fixture)).toContain('Modelo partidas-v1.csv');

    await escolher(fixture);
    botao(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    http.expectOne('/api/v1/competitions/c1/imports/partidas/preview').flush({
      ...resultado(),
      notes: ['Rodadas a criar em rascunho, no fim da ordem: Rodada 1, Rodada 2.'],
    });
    await fixture.whenStable();

    expect(texto(fixture)).toContain('3 linhas lidas: 3 a criar');
    expect(texto(fixture)).toContain(
      'Rodadas a criar em rascunho, no fim da ordem: Rodada 1, Rodada 2.',
    );
  });

  it('linha inválida aparece com o número da linha e nada é dado como gravado', async () => {
    const fixture = await abrir();
    await escolher(fixture);
    botao(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    http.expectOne(PREVIEW).flush(
      {
        title: 'Arquivo com linhas inválidas',
        code: 'import_rows_invalid',
        issues: [{ line: 3, column: 'time', message: '`Time Fantasma` não é um time.' }],
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('Linha 3');
    expect(texto(fixture)).toContain('não é um time.');
    expect(texto(fixture)).toContain('Nada foi gravado.');
    expect(botao(fixture, 'Importar')?.disabled).toBe(true);
  });

  it('arquivo recusado pelo servidor explica o motivo', async () => {
    const fixture = await abrir();
    await escolher(fixture);
    botao(fixture, 'Conferir arquivo')?.click();
    await fixture.whenStable();
    http.expectOne(PREVIEW).flush(
      {
        title: 'Arquivo recusado',
        detail: 'O arquivo não está em UTF-8. Na planilha, salve como CSV UTF-8.',
        code: 'import_file_rejected',
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await fixture.whenStable();

    expect(texto(fixture)).toContain('não está em UTF-8');
  });

  it('auxiliar baixa o modelo, mas não recebe o formulário de envio', async () => {
    const fixture = await abrir('Assistant');

    expect(texto(fixture)).toContain('Somente quem é proprietário da organização importa.');
    expect((fixture.nativeElement as HTMLElement).querySelector('input[type=file]')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('a.acao')).not.toBeNull();
  });
});
