"""Mini IA do parceiro nas raids do Sword/Shield (D31): escolhe o aluguel da Dynamax Adventure e o golpe de cada
batalha, e o golpe da Max Raid em que o TradeRock entra.

É uma escolha "no papel", antes de cada batalha: o pokeldn só responde ao jogo com um golpe fixo por batalha
(pokeldn/swsh/raid_battle.py, `action`), sem ler HP, clima ou status. A nota de um golpe contra um alvo é a força
(60 para as variáveis) × a efetividade de tipo × 1,5 com o mesmo tipo do atacante (STAB) × a precisão; golpes de
status valem zero.

Na Dynamax Adventure, a semente da aventura (ChikaBattleSeed) dá os seis aluguéis e os dez Pokémon que podem
aparecer (`lair.adventure`), e o lendário vem no ChikaBigBossFixed; qual dos dez aparece em cada sala é
desconhecido (docs/swsh_raid.md do pokeldn). Por isso as três primeiras batalhas usam o golpe com a melhor média
contra os dez, e a quarta, o melhor contra o lendário.

Os tipos seguem a ordem do jogo: 0 Normal, 1 Fighting, 2 Flying, 3 Poison, 4 Ground, 5 Rock, 6 Bug, 7 Ghost,
8 Steel, 9 Fire, 10 Water, 11 Grass, 12 Electric, 13 Psychic, 14 Ice, 15 Dragon, 16 Dark, 17 Fairy.
"""
import json
import os

NORMAL, FIGHTING, FLYING, POISON, GROUND, ROCK, BUG, GHOST, STEEL, FIRE, WATER, GRASS, ELECTRIC, PSYCHIC, ICE, \
    DRAGON, DARK, FAIRY = range(18)

# Ataque → {defesa: multiplicador} (da Gen 6 em diante); o que não está aqui vale 1.
CHART = {
    NORMAL: {ROCK: 0.5, GHOST: 0, STEEL: 0.5},
    FIGHTING: {NORMAL: 2, FLYING: 0.5, POISON: 0.5, ROCK: 2, BUG: 0.5, GHOST: 0, STEEL: 2, PSYCHIC: 0.5, ICE: 2,
               DARK: 2, FAIRY: 0.5},
    FLYING: {FIGHTING: 2, ROCK: 0.5, BUG: 2, STEEL: 0.5, GRASS: 2, ELECTRIC: 0.5},
    POISON: {POISON: 0.5, GROUND: 0.5, ROCK: 0.5, GHOST: 0.5, STEEL: 0, GRASS: 2, FAIRY: 2},
    GROUND: {FLYING: 0, POISON: 2, ROCK: 2, BUG: 0.5, STEEL: 2, FIRE: 2, GRASS: 0.5, ELECTRIC: 2},
    ROCK: {FIGHTING: 0.5, FLYING: 2, GROUND: 0.5, BUG: 2, STEEL: 0.5, FIRE: 2, ICE: 2},
    BUG: {FIGHTING: 0.5, FLYING: 0.5, POISON: 0.5, GHOST: 0.5, STEEL: 0.5, FIRE: 0.5, GRASS: 2, PSYCHIC: 2, DARK: 2,
          FAIRY: 0.5},
    GHOST: {NORMAL: 0, GHOST: 2, PSYCHIC: 2, DARK: 0.5},
    STEEL: {ROCK: 2, STEEL: 0.5, FIRE: 0.5, WATER: 0.5, ELECTRIC: 0.5, ICE: 2, FAIRY: 2},
    FIRE: {ROCK: 0.5, BUG: 2, STEEL: 2, FIRE: 0.5, WATER: 0.5, GRASS: 2, ICE: 2, DRAGON: 0.5},
    WATER: {GROUND: 2, ROCK: 2, FIRE: 2, WATER: 0.5, GRASS: 0.5, DRAGON: 0.5},
    GRASS: {FLYING: 0.5, POISON: 0.5, GROUND: 2, ROCK: 2, BUG: 0.5, STEEL: 0.5, FIRE: 0.5, WATER: 2, GRASS: 0.5,
            DRAGON: 0.5},
    ELECTRIC: {FLYING: 2, GROUND: 0, WATER: 2, GRASS: 0.5, ELECTRIC: 0.5, DRAGON: 0.5},
    PSYCHIC: {FIGHTING: 2, POISON: 2, STEEL: 0.5, PSYCHIC: 0.5, DARK: 0},
    ICE: {FLYING: 2, GROUND: 2, STEEL: 0.5, FIRE: 0.5, WATER: 0.5, GRASS: 2, ICE: 0.5, DRAGON: 2},
    DRAGON: {STEEL: 0.5, DRAGON: 2, FAIRY: 0},
    DARK: {FIGHTING: 0.5, GHOST: 2, PSYCHIC: 2, DARK: 0.5, FAIRY: 0.5},
    FAIRY: {FIGHTING: 2, POISON: 0.5, STEEL: 0.5, FIRE: 0.5, DRAGON: 2, DARK: 2},
}

VARIABLE_POWER = 60   # Low Kick, Gyro Ball, Heavy Slam...: a força depende da batalha
STAB = 1.5
LEGENDARY_WEIGHT = 0.6  # na escolha do aluguel: o lendário pesa mais que os dez da aventura
MOVES_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data", "moves.json")

_moves = None


def moves():
    """id do golpe → (tipo, força, precisão, categoria) da tabela gerada por scripts/fetch-move-data.py."""
    global _moves
    if _moves is None:
        with open(MOVES_FILE, encoding="utf-8") as f:
            _moves = {int(k): tuple(v) for k, v in json.load(f)["moves"].items()}
    return _moves


def effectiveness(move_type, target_types):
    result = 1.0
    for t in set(target_types):
        result *= CHART[move_type].get(t, 1)
    return result


def score(move, target_types, attacker_types=()):
    """A nota de um golpe contra um Pokémon com `target_types`; 0 para status ou golpe desconhecido."""
    info = moves().get(move)
    if not move or info is None or info[3] == 0:
        return 0.0
    move_type, power, accuracy, _ = info
    value = (power or VARIABLE_POWER) * effectiveness(move_type, target_types)
    if move_type in attacker_types:
        value *= STAB
    return value * (accuracy or 100) / 100


def average(move, targets, attacker_types=()):
    """A média da nota contra vários alvos (listas de tipos)."""
    return sum(score(move, t, attacker_types) for t in targets) / len(targets) if targets else 0.0


def best_slot(move_list, targets, attacker_types=()):
    """O índice (0 a 3) do golpe com a melhor média contra `targets`; o primeiro golpe em empate ou sem dados."""
    notes = [average(m, targets, attacker_types) for m in move_list]
    if not notes or max(notes) <= 0:
        return 0
    return notes.index(max(notes))


def row_types(row):
    """[espécie, forma, lendário, nível, tipo 1, tipo 2, golpes...] (lair_pokemon.json do pokeldn) → os tipos."""
    return (row[4], row[5])


def rental_note(row, encounters, legendary):
    """A nota de um aluguel: o melhor golpe contra o lendário (peso maior) e a melhor média contra os dez."""
    attacker, move_list = row_types(row), row[6:10]
    others = [row_types(r) for r in encounters]
    against_legendary = max((score(m, row_types(legendary), attacker) for m in move_list), default=0) if legendary else 0
    against_others = max((average(m, others, attacker) for m in move_list), default=0)
    if legendary is None:
        return against_others
    return LEGENDARY_WEIGHT * against_legendary + (1 - LEGENDARY_WEIGHT) * against_others


def choose_rental(candidates, encounters, legendary):
    """O índice do melhor aluguel entre `candidates` (linhas da aventura); o primeiro em empate."""
    notes = [rental_note(row, encounters, legendary) for row in candidates]
    return notes.index(max(notes)) if notes else 0


class LairPartner:
    """O que a IA sabe da aventura em andamento: o aluguel escolhido, os dez da aventura e o lendário."""

    BATTLES = 4  # três salas e o lendário

    def __init__(self, log=print):
        self.log = log
        self.row = None
        self.encounters = []
        self.legendary = None
        self.battle = 0

    def choose(self, offer, encounters, legendary, places):
        """Escolhe entre os lugares `places` da oferta e guarda o contexto. → o lugar escolhido."""
        self.encounters, self.legendary = list(encounters), legendary
        pick = places[choose_rental([offer[p] for p in places], self.encounters, legendary)]
        self.row = offer[pick]
        self.log(f"[ai] aluguel: lugar {pick} (espécie {self.row[0]}) entre {[offer[p][0] for p in places]}, "
                 f"contra o lendário {legendary[0] if legendary else '?'}")
        return pick

    def next_move(self, fallback):
        """O golpe da próxima batalha: média contra os dez nas primeiras, o lendário na última."""
        if self.row is None:
            return fallback
        self.battle += 1
        move_list = [m for m in self.row[6:10] if m]
        last = self.battle >= self.BATTLES and self.legendary is not None
        targets = [row_types(self.legendary)] if last else [row_types(r) for r in self.encounters]
        if not move_list or not targets:
            return fallback
        move = move_list[best_slot(move_list, targets, row_types(self.row))]
        self.log(f"[ai] batalha {self.battle}: golpe {move} "
                 f"({'contra o lendário' if last else 'melhor média contra os da aventura'})")
        return move


def install_lair(log=print):
    """Liga a IA na Dynamax Adventure em que o TradeRock entra (swsh_connect.py --preset lair): troca a escolha do
    aluguel do `lair.Guest` e o golpe de cada `raid_battle.action` com o modelo da aventura (LAIR_ACTION)."""
    from pokeldn.swsh import lair, raid_battle

    partner = LairPartner(log)
    original_select = lair.Guest._select
    original_action = raid_battle.action

    def _select(self, taken):
        if self.rental_choice is None and self.pk8 is None and self.seed is not None:
            offer, encounters = lair.adventure(self.seed, self.partners)
            legendary = next((r for r in lair.table() if r[2] and r[0] == self.legendary), None)
            self.rental_choice = partner.choose(offer, encounters, legendary, [p for p in range(3) if p != taken])
        return original_select(self, taken)

    def action(move, target=raid_battle.SINGLE_TARGET, template=raid_battle.ACTION):
        if template is raid_battle.LAIR_ACTION:
            move = partner.next_move(move)
        return original_action(move, target, template)

    lair.Guest._select = _select
    raid_battle.action = action
    return partner
