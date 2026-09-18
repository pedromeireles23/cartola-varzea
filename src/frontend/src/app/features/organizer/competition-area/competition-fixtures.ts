import { CompetitionDetails, ModalityProfile } from '../competition.service';

/**
 * Dados de exemplo para os testes de componente da área de campeonato. Os perfis
 * repetem o que a API devolve em `GET /modality-profiles` (01 §7 e §9).
 */
export const PERFIS: readonly ModalityProfile[] = [
  perfil('Fut7', 7, [1, 2, 2, 2], 100, [
    [4, 3, 5],
    [3, 4, 6],
    [2, 5, 8],
  ]),
  perfil('Futsal', 5, [1, 1, 2, 1], 80, [
    [4, 2, 4],
    [3, 3, 5],
    [2, 4, 7],
  ]),
  perfil('Field', 11, [1, 4, 3, 3], 135, [
    [4, 4, 6],
    [3, 6, 8],
    [2, 8, 11],
  ]),
];

export function campeonato(parcial: Partial<CompetitionDetails> = {}): CompetitionDetails {
  return {
    id: 'c1',
    organizationId: 'o1',
    organizationName: 'Liga da Várzea',
    viewerRole: 'Owner',
    name: 'Copa da Várzea',
    season: '2026',
    modality: 'Fut7',
    status: 'Draft',
    timeZoneId: 'America/Sao_Paulo',
    marketCloseLeadTimeMinutes: 60,
    resultsSlaBusinessDays: 2,
    correctionWindowBusinessDays: 3,
    registrationDeadlineLocal: null,
    registrationWindow: {
      closesAtLocal: null,
      source: 'NotYetDefined',
      roundName: null,
      isOpen: true,
    },
    canChangeModality: true,
    modalityProfile: PERFIS[0],
    createdAt: '2026-09-16T12:00:00Z',
    updatedAt: '2026-09-16T12:00:00Z',
    version: 'AAAAAAAAB9E=',
    ...parcial,
  };
}

function perfil(
  modality: ModalityProfile['modality'],
  starters: number,
  [goalkeepers, defenders, midfielders, forwards]: readonly number[],
  budget: number,
  limites: readonly (readonly [number, number, number])[],
): ModalityProfile {
  return {
    modality,
    version: 1,
    starters,
    formation: { goalkeepers, defenders, midfielders, forwards },
    benchSize: 4,
    squadAthletes: starters + 4,
    budget,
    minimumAthletesPerRealTeam: starters + 2,
    realTeamLimits: limites.map(([activeRealTeams, maxStarters, maxAthletes]) => ({
      activeRealTeams,
      maxStarters,
      maxAthletes,
    })),
  };
}
