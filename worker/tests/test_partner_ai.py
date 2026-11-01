"""Mini IA do parceiro nas raids do Sword/Shield (D31)."""
import os
import subprocess
import sys
import textwrap
import unittest

from traderock_worker import partner_ai as ai

TESTS = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(TESTS))

# Golpes da tabela (ids do jogo).
THUNDERBOLT, EARTHQUAKE, ICE_BEAM, SURF, TOXIC, FLAMETHROWER, TACKLE, LOW_KICK = 85, 89, 58, 57, 92, 53, 33, 67


class TabelaTests(unittest.TestCase):
    def test_tabela_de_tipos(self):
        self.assertEqual(ai.effectiveness(ai.ELECTRIC, [ai.GROUND]), 0)
        self.assertEqual(ai.effectiveness(ai.ICE, [ai.DRAGON, ai.FLYING]), 4)
        self.assertEqual(ai.effectiveness(ai.FIRE, [ai.WATER, ai.ROCK]), 0.25)
        self.assertEqual(ai.effectiveness(ai.FAIRY, [ai.DRAGON]), 2)
        self.assertEqual(ai.effectiveness(ai.NORMAL, []), 1)
        # Linhas completas, 18 tipos, só multiplicadores válidos.
        self.assertEqual(sorted(ai.CHART), list(range(18)))
        self.assertTrue(all(v in (0, 0.5, 2) for row in ai.CHART.values() for v in row.values()))

    def test_golpes_da_tabela(self):
        moves = ai.moves()
        self.assertEqual(moves[THUNDERBOLT], (ai.ELECTRIC, 90, 100, 2))
        self.assertEqual(moves[TOXIC][3], 0)               # status
        self.assertIsNone(moves[LOW_KICK][1])               # força variável
        self.assertGreaterEqual(max(moves), 826)            # até a Gen 8

    def test_nota(self):
        self.assertEqual(ai.score(TOXIC, [ai.GRASS]), 0)
        self.assertEqual(ai.score(THUNDERBOLT, [ai.GROUND]), 0)
        self.assertEqual(ai.score(THUNDERBOLT, [ai.WATER], (ai.ELECTRIC,)), 90 * 2 * 1.5)
        self.assertEqual(ai.score(LOW_KICK, [ai.NORMAL]), ai.VARIABLE_POWER * 2)
        self.assertEqual(ai.score(0, [ai.NORMAL]), 0)

    def test_melhor_golpe(self):
        moves = [TACKLE, THUNDERBOLT, ICE_BEAM, TOXIC]
        self.assertEqual(ai.best_slot(moves, [[ai.DRAGON, ai.FLYING]]), 2)        # Ice Beam 4x
        self.assertEqual(ai.best_slot(moves, [[ai.WATER]], (ai.ELECTRIC,)), 1)   # Thunderbolt 2x com STAB
        self.assertEqual(ai.best_slot([TOXIC, 0], [[ai.WATER]]), 0)              # sem golpe de dano: o primeiro
        # Média contra vários: Surf vence Thunderbolt contra Ground + Water.
        self.assertEqual(ai.best_slot([THUNDERBOLT, SURF], [[ai.GROUND], [ai.WATER]]), 1)


def row(species, t1, t2, *moves):
    return [species, 0, False, 65, t1, t2, *moves, *([0] * (4 - len(moves)))]


class AluguelTests(unittest.TestCase):
    def test_escolhe_o_aluguel_contra_o_lendario(self):
        legendary = [384, 0, True, 70, ai.DRAGON, ai.FLYING, 0, 0, 0, 0]       # Rayquaza
        encounters = [row(1, ai.WATER, ai.WATER)] * 10
        candidates = [row(25, ai.ELECTRIC, ai.ELECTRIC, THUNDERBOLT),
                      row(131, ai.WATER, ai.ICE, ICE_BEAM, SURF),
                      row(19, ai.NORMAL, ai.NORMAL, TACKLE)]
        self.assertEqual(ai.choose_rental(candidates, encounters, legendary), 1)

    def test_parceiro_escolhe_o_golpe_de_cada_batalha(self):
        lines = []
        partner = ai.LairPartner(lines.append)
        legendary = [384, 0, True, 70, ai.DRAGON, ai.FLYING, 0, 0, 0, 0]
        encounters = [row(76, ai.ROCK, ai.GROUND)] * 10
        offer = [row(25, ai.ELECTRIC, ai.ELECTRIC, THUNDERBOLT),
                 row(9, ai.WATER, ai.WATER, ICE_BEAM, SURF, TACKLE),
                 row(19, ai.NORMAL, ai.NORMAL, TACKLE)] + [row(1, 0, 0, TACKLE)] * 3
        self.assertEqual(partner.choose(offer, encounters, legendary, [1, 2]), 1)
        self.assertEqual([partner.next_move(TACKLE) for _ in range(4)], [SURF, SURF, SURF, ICE_BEAM])
        self.assertTrue(any("aluguel" in line for line in lines))
        self.assertEqual(ai.LairPartner().next_move(TACKLE), TACKLE)  # sem aventura: o golpe de antes


def pokeldn_python():
    from tests.test_pokeldn import pokeldn_python as find
    return find()


class AventuraRealTests(unittest.TestCase):
    """A IA ligada ao lair.Guest e ao raid_battle.action do pokeldn de verdade, numa semente conhecida."""

    def test_ia_no_pokeldn(self):
        python = pokeldn_python()
        if python is None:
            self.skipTest("pokeldn e as dependências dele não estão disponíveis")
        code = textwrap.dedent(f"""
            import sys
            sys.path[:0] = [{os.path.join(REPO, "engines", "pokeldn")!r}, {os.path.join(REPO, "worker")!r}]
            from pokeldn.swsh import lair, raid_battle
            from traderock_worker import partner_ai
            lines = []
            partner = partner_ai.install_lair(lines.append)
            guest = lair.Guest(0x1234, log=lambda line: None)
            guest.seed, guest.legendary = 173502832, 144
            guest._select(0)
            assert partner.row is not None and guest.rental_choice in (1, 2), guest.rental_choice
            offer, encounters = lair.adventure(173502832, 2)
            assert partner.row == offer[guest.rental_choice]
            moves = [m for m in partner.row[6:10] if m]
            chosen = []
            for _ in range(4):
                raid_battle.action(moves[0], template=raid_battle.LAIR_ACTION)
                chosen.append(partner.battle)
            assert chosen == [1, 2, 3, 4], chosen
            # A Max Raid comum não passa pela IA.
            raid_battle.action(moves[0])
            assert partner.battle == 4
            print("ok", len(lines))
            """)
        r = subprocess.run([python, "-c", code], capture_output=True, text=True, timeout=60)
        self.assertIn("ok", r.stdout, r.stderr)


if __name__ == "__main__":
    unittest.main()
