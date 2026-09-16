import { ComponentFixture, TestBed } from '@angular/core/testing';

import { StageForm } from './stage-form';
import { Stage, StageInput } from './stage.service';

const FASE_DE_GRUPOS: Stage = {
  id: 's1',
  name: 'Fase de grupos',
  format: 'Groups',
  sequence: 1,
  groups: [
    { id: 'g1', name: 'Grupo A' },
    { id: 'g2', name: 'Grupo B' },
  ],
  tiebreakers: ['GoalDifference', 'Wins'],
  version: 'AAAAAAAAB9E=',
};

describe('StageForm', () => {
  let enviados: StageInput[];

  async function abrir(initial: Stage | null = null): Promise<ComponentFixture<StageForm>> {
    const fixture = TestBed.createComponent(StageForm);
    fixture.componentRef.setInput('submitLabel', 'Salvar');
    fixture.componentRef.setInput('initial', initial);
    enviados = [];
    fixture.componentInstance.submitted.subscribe((valor) => enviados.push(valor));
    await fixture.whenStable();
    return fixture;
  }

  function elemento(fixture: ComponentFixture<StageForm>): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function campo(fixture: ComponentFixture<StageForm>, rotulo: string) {
    const label = [...elemento(fixture).querySelectorAll('label')].find((item) =>
      item.textContent?.trim().startsWith(rotulo),
    );
    expect(label, `Campo "${rotulo}" não encontrado`).toBeDefined();
    return elemento(fixture).querySelector(`#${label!.htmlFor}`) as
      HTMLInputElement | HTMLSelectElement;
  }

  async function escrever(fixture: ComponentFixture<StageForm>, rotulo: string, valor: string) {
    const alvo = campo(fixture, rotulo);
    alvo.value = valor;
    alvo.dispatchEvent(new Event(alvo instanceof HTMLSelectElement ? 'change' : 'input'));
    await fixture.whenStable();
  }

  function botao(fixture: ComponentFixture<StageForm>, texto: string): HTMLButtonElement {
    const encontrado = [...elemento(fixture).querySelectorAll('button')].find(
      (item) => item.textContent?.replace(/\s+/g, ' ').trim() === texto,
    );
    expect(encontrado, `Botão "${texto}" não encontrado`).toBeDefined();
    return encontrado!;
  }

  async function enviar(fixture: ComponentFixture<StageForm>): Promise<void> {
    elemento(fixture).querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('nova fase começa como grupos A e B com o desempate padrão', async () => {
    const fixture = await abrir();

    await escrever(fixture, 'Nome da fase', ' Primeira fase ');
    await enviar(fixture);

    expect(enviados).toEqual([
      {
        name: 'Primeira fase',
        format: 'Groups',
        groups: [
          { id: null, name: 'Grupo A' },
          { id: null, name: 'Grupo B' },
        ],
        tiebreakers: [
          'Wins',
          'GoalDifference',
          'GoalsFor',
          'HeadToHead',
          'FewestRedCards',
          'FewestYellowCards',
        ],
      },
    ]);
  });

  it('muda a quantidade de grupos mantendo os nomes já escritos', async () => {
    const fixture = await abrir();

    await escrever(fixture, 'Nome do grupo 1', 'Chave Norte');
    await escrever(fixture, 'Quantidade de grupos', '4');

    expect(campo(fixture, 'Nome do grupo 1').value).toBe('Chave Norte');
    expect(campo(fixture, 'Nome do grupo 3').value).toBe('Grupo A');
    expect(campo(fixture, 'Nome do grupo 4').value).toBe('Grupo C');

    await escrever(fixture, 'Quantidade de grupos', '1');
    expect(elemento(fixture).querySelectorAll('input[type="text"]')).toHaveLength(2);
  });

  it('edita mantendo os identificadores e a ordem escolhida do desempate', async () => {
    const fixture = await abrir(FASE_DE_GRUPOS);

    expect(campo(fixture, 'Nome do grupo 2').value).toBe('Grupo B');
    await escrever(fixture, 'Nome do grupo 2', 'Grupo Sul');
    botao(fixture, 'Subir Mais vitórias').click();
    await fixture.whenStable();
    const marcaSaldo = [...elemento(fixture).querySelectorAll('input[type="checkbox"]')][1];
    (marcaSaldo as HTMLInputElement).click();
    await enviar(fixture);

    expect(enviados[0].groups).toEqual([
      { id: 'g1', name: 'Grupo A' },
      { id: 'g2', name: 'Grupo Sul' },
    ]);
    expect(enviados[0].tiebreakers).toEqual(['Wins']);
  });

  it('mata-mata não envia grupos nem desempate', async () => {
    const fixture = await abrir(FASE_DE_GRUPOS);

    const mataMata = elemento(fixture).querySelector('input[value="Knockout"]') as HTMLInputElement;
    mataMata.checked = true;
    mataMata.dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(elemento(fixture).textContent).not.toContain('Quantidade de grupos');

    await enviar(fixture);

    expect(enviados).toEqual([
      { name: 'Fase de grupos', format: 'Knockout', groups: [], tiebreakers: [] },
    ]);
  });

  it('recusa grupos com nomes repetidos e desempate vazio sem enviar', async () => {
    const fixture = await abrir();

    await escrever(fixture, 'Nome da fase', 'Grupos');
    await escrever(fixture, 'Nome do grupo 2', ' grupo a ');
    for (const marca of elemento(fixture).querySelectorAll<HTMLInputElement>(
      'input[type="checkbox"]',
    )) {
      marca.click();
    }
    await enviar(fixture);

    expect(enviados).toHaveLength(0);
    expect(elemento(fixture).textContent).toContain('Os grupos precisam de nomes diferentes.');
    expect(elemento(fixture).textContent).toContain('Marque ao menos um critério de desempate.');
    expect(elemento(fixture).textContent).toContain('Revise 2 campos antes de salvar.');
  });
});
