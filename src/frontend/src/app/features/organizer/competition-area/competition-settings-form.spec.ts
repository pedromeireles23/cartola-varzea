import { ComponentFixture, TestBed } from '@angular/core/testing';

import { CompetitionSettings } from '../competition.service';
import { campeonato, PERFIS } from './competition-fixtures';
import { CompetitionSettingsForm } from './competition-settings-form';

describe('CompetitionSettingsForm', () => {
  let enviados: CompetitionSettings[];

  async function abrir(
    inputs: Partial<{ initial: CompetitionSettings; modalityLocked: boolean }> = {},
  ): Promise<ComponentFixture<CompetitionSettingsForm>> {
    const fixture = TestBed.createComponent(CompetitionSettingsForm);
    fixture.componentRef.setInput('profiles', PERFIS);
    fixture.componentRef.setInput('submitLabel', 'Salvar');
    for (const [nome, valor] of Object.entries(inputs)) {
      fixture.componentRef.setInput(nome, valor);
    }
    enviados = [];
    fixture.componentInstance.submitted.subscribe((valor) => enviados.push(valor));
    await fixture.whenStable();
    return fixture;
  }

  function elemento(fixture: ComponentFixture<CompetitionSettingsForm>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function campo(fixture: ComponentFixture<CompetitionSettingsForm>, rotulo: string) {
    const label = [...elemento(fixture).querySelectorAll('label')].find((item) =>
      item.textContent?.includes(rotulo),
    );
    expect(label, `Campo "${rotulo}" não encontrado`).toBeDefined();
    return elemento(fixture).querySelector(`#${label!.htmlFor}`) as
      HTMLInputElement | HTMLSelectElement;
  }

  function escrever(
    fixture: ComponentFixture<CompetitionSettingsForm>,
    rotulo: string,
    valor: string,
  ): void {
    const alvo = campo(fixture, rotulo);
    alvo.value = valor;
    alvo.dispatchEvent(new Event(alvo instanceof HTMLSelectElement ? 'change' : 'input'));
  }

  function marcarModalidade(fixture: ComponentFixture<CompetitionSettingsForm>, valor: string) {
    const radio = elemento(fixture).querySelector(
      `input[type="radio"][value="${valor}"]`,
    ) as HTMLInputElement;
    radio.checked = true;
    radio.dispatchEvent(new Event('change'));
  }

  async function enviar(fixture: ComponentFixture<CompetitionSettingsForm>): Promise<void> {
    elemento(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('mostra as três modalidades com formação, banco e orçamento', async () => {
    const fixture = await abrir();
    const texto = elemento(fixture).textContent ?? '';

    expect(elemento(fixture).querySelectorAll('input[type="radio"]')).toHaveLength(3);
    expect(texto).toContain('Futsal');
    expect(texto).toContain('5 titulares: 1 goleiro, 1 defensor, 2 meio-campistas e 1 atacante');
    expect(texto).toContain('Futebol de campo');
    expect(texto).toContain('135 créditos');
  });

  it('começa com os padrões do produto e não envia sem nome e modalidade', async () => {
    const fixture = await abrir();

    expect(campo(fixture, 'Fuso horário').value).toBe('America/Sao_Paulo');
    expect(campo(fixture, 'Fechamento do mercado').value).toBe('0');
    expect(campo(fixture, 'Prazo para publicar').value).toBe('2');
    expect(campo(fixture, 'Janela de correção').value).toBe('3');

    await enviar(fixture);

    expect(enviados).toHaveLength(0);
    expect(elemento(fixture).textContent).toContain('Revise 2 campos antes de salvar.');
    expect(campo(fixture, 'Nome do campeonato').getAttribute('aria-invalid')).toBe('true');
    expect(elemento(fixture).textContent).toContain('Escolha a modalidade.');
    const grupo = elemento(fixture).querySelector('fieldset[aria-describedby]');
    expect(grupo?.getAttribute('aria-describedby')).toBe('modalidade-erro');
  });

  it('recusa prazos fora de 1 a 10 dias úteis', async () => {
    const fixture = await abrir({ initial: campeonato() });

    escrever(fixture, 'Prazo para publicar', '0');
    escrever(fixture, 'Janela de correção', '2.5');
    await enviar(fixture);

    expect(enviados).toHaveLength(0);
    expect(campo(fixture, 'Prazo para publicar').getAttribute('aria-invalid')).toBe('true');
    expect(campo(fixture, 'Janela de correção').getAttribute('aria-invalid')).toBe('true');
  });

  it('envia os valores escolhidos, com textos aparados', async () => {
    const fixture = await abrir();

    escrever(fixture, 'Nome do campeonato', '  Copa de Verão  ');
    escrever(fixture, 'Temporada', ' 2027 ');
    marcarModalidade(fixture, 'Futsal');
    escrever(fixture, 'Fuso horário', 'America/Manaus');
    escrever(fixture, 'Fechamento do mercado', '120');
    escrever(fixture, 'Prazo para publicar', '4');
    await enviar(fixture);

    expect(enviados).toEqual([
      {
        name: 'Copa de Verão',
        season: '2027',
        modality: 'Futsal',
        timeZoneId: 'America/Manaus',
        marketCloseLeadTimeMinutes: 120,
        resultsSlaBusinessDays: 4,
        correctionWindowBusinessDays: 3,
        registrationDeadlineLocal: null,
      },
    ]);
  });

  it('envia o prazo de inscrição escolhido e volta ao padrão quando apagado', async () => {
    const fixture = await abrir({
      initial: campeonato({ registrationDeadlineLocal: '2026-10-04T18:00' }),
    });
    expect(campo(fixture, 'Prazo de inscrição').value).toBe('2026-10-04T18:00');

    escrever(fixture, 'Prazo de inscrição', '2026-10-11T18:00');
    await enviar(fixture);
    escrever(fixture, 'Prazo de inscrição', '');
    await enviar(fixture);

    expect(enviados.map((item) => item.registrationDeadlineLocal)).toEqual([
      '2026-10-11T18:00',
      null,
    ]);
  });

  it('preenche com a configuração existente e trava a modalidade quando pedido', async () => {
    const fixture = await abrir({
      initial: campeonato({ modality: 'Field', marketCloseLeadTimeMinutes: 90 }),
      modalityLocked: true,
    });

    expect(campo(fixture, 'Nome do campeonato').value).toBe('Copa da Várzea');
    const radios = [...elemento(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')];
    expect(radios.every((radio) => radio.disabled)).toBe(true);
    expect(radios.find((radio) => radio.checked)?.value).toBe('Field');
    expect(elemento(fixture).textContent).toContain('a modalidade não muda mais');

    // Uma antecedência fora das opções comuns continua disponível, sem ser trocada.
    expect(campo(fixture, 'Fechamento do mercado').value).toBe('90');
  });
});
