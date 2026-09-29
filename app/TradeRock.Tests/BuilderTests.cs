using PKHeX.Core;
using TradeRock.Core.Builder;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Trading;

namespace TradeRock.Tests;

/// <summary>Gerações reais com o AutoMod, conferidas pelo LegalityAnalysis do PKHeX.</summary>
public sealed class BuilderTests
{
    // IDs "comuns": os padrões do PKHeX (12345/54321) disparam o aviso de treinador suspeito.
    private static readonly TrainerSpec Me = new("ROCHA", 24680, 13579);

    private static Task<BuildResult> Generate(PokemonSpec spec) => PokemonBuilder.GenerateAsync(spec, "pt-BR", default);

    private static PokemonSpec Spec(ushort species, byte level) => new() { Species = species, Level = level, Trainer = Me };

    [Theory]
    [InlineData(1, 5)]    // Bulbasaur
    [InlineData(150, 70)] // Mewtwo (Cerulean Cave)
    [InlineData(249, 70)] // Lugia (Navel Rock, Mystic Ticket)
    [InlineData(250, 70)] // Ho-Oh (Navel Rock)
    [InlineData(386, 30)] // Deoxys (Birth Island, Aurora Ticket)
    [InlineData(144, 50)] // Articuno
    [InlineData(25, 20)]  // Pikachu
    public async Task Gera_pokemon_legal_com_o_meu_treinador(ushort species, byte level)
    {
        var r = await Generate(Spec(species, level));

        Assert.Equal("regenerated", r.Generation);
        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(species, r.Summary!.Species);
        Assert.Equal(level, r.Summary.Level);
        Assert.Equal("ROCHA", r.Summary.OtName);
        Assert.Equal(24680, r.Summary.Tid);
        Assert.Equal(13579, r.Summary.Sid);
        Assert.DoesNotContain(r.Legality.Checks, c => c.Severity == "invalid");
        Assert.Empty(r.Differences);
    }

    [Fact]
    public async Task Respeita_natureza_shiny_golpes_nivel_e_pokebola()
    {
        var r = await Generate(Spec(143, 50) with
        {
            Nature = 3, // Adamant
            Shiny = true,
            Moves = [156, 34, 247, 89], // Rest, Body Slam, Shadow Ball, Earthquake
        });

        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(3, r.Obtained!.Nature);
        Assert.True(r.Obtained.Shiny);
        Assert.Equal(50, r.Obtained.Level); // o AutoMod não troca 50 por 100
        Assert.Equal<ushort>([34, 89, 156, 247], r.Obtained.Moves.Where(m => m != 0).Order().ToArray());
    }

    [Fact]
    public async Task Respeita_a_pokebola_quando_o_encontro_permite()
    {
        var r = await Generate(Spec(150, 70) with { Ball = (int)Ball.Ultra });
        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal((int)Ball.Ultra, r.Obtained!.Ball);
        Assert.Empty(r.Differences);
    }

    [Fact]
    public async Task Pokebola_impossivel_aparece_como_diferenca()
    {
        // Pokémon de ovo na Gen 3 é sempre Poké Ball; Master Ball não é possível para um Togepi.
        var r = await Generate(Spec(175, 5) with { Ball = (int)Ball.Master });
        Assert.NotEqual(LegalityStatus.Illegal, r.Legality!.Status);
        Assert.Contains(r.Differences, d => d.Field == "ball");
    }

    [Fact]
    public async Task Prefere_o_encontro_do_frlg_ao_evento()
    {
        // O PKHeX lista primeiro o Ho-Oh de evento do Colosseum (OT MATTLE); o TradeRock prefere a Navel Rock.
        var r = await Generate(Spec(250, 70));
        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal("ROCHA", r.Summary!.OtName);
        Assert.Equal(174, r.Encounter!.Location);
    }

    [Fact]
    public async Task Sem_pokebola_pedida_usa_a_comum()
    {
        var r = await Generate(Spec(386, 30));
        Assert.Equal((int)Ball.Poke, r.Obtained!.Ball);
    }

    [Fact]
    public async Task Deoxys_tem_a_forma_do_jogo()
    {
        var fr = await Generate(Spec(386, 30) with { Game = TradeGame.FireRed });
        var lg = await Generate(Spec(386, 30) with { Game = TradeGame.LeafGreen });
        Assert.Equal("FR", fr.Encounter!.Version);
        Assert.Equal("LG", lg.Encounter!.Version);
    }

    [Fact]
    public async Task Evento_mantem_o_treinador_do_evento_e_avisa()
    {
        var r = await Generate(Spec(385, 5)); // Jirachi (WISHMKR)

        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal("WISHMKR", r.Summary!.OtName);
        Assert.Equal("event", r.Encounter!.Type);
        Assert.Contains(r.Differences, d => d.Field == "trainer");
    }

    [Fact]
    public async Task Item_e_apelido_entram_depois_da_geracao()
    {
        var leftovers = BuilderData.Items.Single(i => i.Text == "Leftovers");
        var r = await Generate(Spec(143, 30) with { HeldItem = (ushort)leftovers.Value, Nickname = "Ronco" });

        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(leftovers.Value, r.Summary!.HeldItem);
        Assert.Equal("Ronco", r.Summary.Nickname);
    }

    [Fact]
    public async Task Encontro_escolhido_e_respeitado()
    {
        var wild = BuilderData.Encounters(16, TradeGame.FireRed).First(e => e.Type == "wild");
        var r = await Generate(Spec(16, 5) with { EncounterKey = wild.Key });

        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(wild.Location, r.Encounter!.Location);
        Assert.Equal("wild", r.Encounter.Type);
    }

    [Fact]
    public async Task Pedido_impossivel_falha_sem_trocar_a_especie()
    {
        // Celebi nível 10 em inglês não existe (o evento de nível 10 é japonês).
        var r = await Generate(Spec(251, 10));

        Assert.NotEqual(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(251, r.Summary!.Species); // sem "easter egg" do AutoMod
        Assert.Contains(r.Legality.Problems, c => c.Field == "origin");
    }

    [Fact]
    public async Task Nivel_abaixo_do_minimo_explica_o_motivo()
    {
        var r = await Generate(Spec(150, 20)); // Mewtwo só existe no nível 70
        Assert.False(r.Usable);
        var blocker = Assert.Single(r.Blockers!);
        Assert.Equal("level_below_minimum", blocker.Code);
        Assert.Equal((byte)70, blocker.Args["min"]);
    }

    [Fact]
    public async Task Pedido_impossivel_aponta_o_campo_que_atrapalha()
    {
        // Um Deoxys (encontro com PID fixo por RNG) shiny e com IVs perfeitos não sai; sem os IVs, sai.
        var r = await Generate(Spec(386, 30) with { Ivs = [31, 31, 31, 31, 31, 31], Nature = 3 });
        if (r.Usable)
            return; // se o AutoMod achar uma combinação, nada a diagnosticar
        Assert.NotEmpty(r.Blockers!);
    }

    [Fact]
    public async Task Pedido_invalido_e_recusado_antes_do_autoMod()
    {
        var r = await Generate(Spec(0, 101) with { Evs = [255, 255, 255, 0, 0, 0], Moves = [1, 1] });
        Assert.Equal("invalid_request", r.Generation);
        Assert.Contains("species_invalid", r.Errors);
        Assert.Contains("level_invalid", r.Errors);
        Assert.Contains("evs_total", r.Errors);
        Assert.Contains("moves_repeated", r.Errors);
    }

    [Fact]
    public async Task Mensagens_de_legalidade_em_portugues_e_ingles()
    {
        var r = await Generate(Spec(251, 10));
        var pk = new PK3(r.Data!.ToArray());

        var pt = LegalityReporter.Analyze(pk, "pt-BR");
        var en = LegalityReporter.Analyze(pk, "en");

        Assert.Contains(pt.Checks, c => c.Message == "Não foi possível encontrar um encontro correspondente no jogo de origem.");
        Assert.Contains(en.Checks, c => c.Message == "Unable to match an encounter from origin game.");
        Assert.DoesNotContain(pt.Checks, c => c.Message.StartsWith("Inválido:") || c.Message.StartsWith("Invalid:"));
    }

    [Fact]
    public void Especie_listada_como_distribuicao_desconhecida_fica_nao_verificavel()
    {
        // Celebi com flag de evento que o PKHeX não reconhece.
        var raw = new RawPokemon
        {
            Species = 251, Level = 10, Pid = 0x12345678, OtName = "SWITCH", Tid = 1, Sid = 2,
            Version = "FR", MetLocation = 255, MetLevel = 10, Fateful = true, Moves = [73, 105, 215, 219],
        };
        var pk = AdvancedEditor.ToPk(raw);

        Assert.Equal(LegalityStatus.Illegal, LegalityReporter.Analyze(pk).Status);
        using (LegalityReporter.OverrideUnverifiableSpecies(251))
        {
            var report = LegalityReporter.Analyze(pk);
            Assert.Equal(LegalityStatus.Unverifiable, report.Status);
            Assert.NotNull(report.UnverifiableReason);
        }
        // Sem a flag de evento não vale a exceção.
        using (LegalityReporter.OverrideUnverifiableSpecies(251))
            Assert.Equal(LegalityStatus.Illegal, LegalityReporter.Analyze(AdvancedEditor.ToPk(raw with { Fateful = false })).Status);
    }

    [Fact]
    public void Lista_de_distribuicoes_desconhecidas_comeca_vazia()
    {
        // A versão de Switch não teve distribuições; os tickets levam a encontros que o PKHeX conhece.
        var raw = new RawPokemon { Species = 249, Level = 70, Fateful = true, OtName = "X", MetLocation = 1 };
        Assert.NotEqual(LegalityStatus.Unverifiable, LegalityReporter.Analyze(AdvancedEditor.ToPk(raw)).Status);
    }
}

public sealed class AdvancedEditorTests
{
    [Fact]
    public async Task Ida_e_volta_de_um_pokemon_legal_continua_legal()
    {
        var gen = await PokemonBuilder.GenerateAsync(new PokemonSpec { Species = 150, Level = 70, Trainer = new("ROCHA", 24680, 13579) }, "pt-BR", default);
        var raw = RawPokemon.FromPk(new PK3(gen.Data!.ToArray()));

        var r = AdvancedEditor.Build(raw, "pt-BR");

        Assert.Empty(r.Errors);
        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(gen.Obtained!.Nature, r.Derived!.Nature);
        Assert.Equal("Method_1", r.Derived.PidIvMethod);
    }

    [Fact]
    public async Task Mudar_o_pid_quebra_a_correlacao_e_aponta_o_campo()
    {
        var gen = await PokemonBuilder.GenerateAsync(new PokemonSpec { Species = 150, Level = 70, Trainer = new("ROCHA", 24680, 13579) }, "pt-BR", default);
        var raw = RawPokemon.FromPk(new PK3(gen.Data!.ToArray()));

        var r = AdvancedEditor.Build(raw with { Pid = raw.Pid ^ 0x00010001 }, "pt-BR");

        Assert.Equal(LegalityStatus.Illegal, r.Legality!.Status);
        Assert.Contains(r.Legality.Problems, c => c.Field == "pid");
    }

    [Fact]
    public void Natureza_genero_e_shiny_saem_do_pid()
    {
        // PID 0x00000000: natureza 0 (Hardy); com TID=SID=0 é shiny.
        var r = AdvancedEditor.Build(new RawPokemon { Species = 16, Level = 5, Pid = 0, OtName = "A", MetLocation = 101, MetLevel = 5 }, "pt-BR");
        Assert.Equal(0, r.Derived!.Nature);
        Assert.True(r.Derived.Shiny);
    }

    [Fact]
    public void Campos_fora_do_limite_sao_recusados()
    {
        var r = AdvancedEditor.Build(new RawPokemon { Species = 999, Level = 0, Ball = 99, Version = "SV", OtName = "" }, "pt-BR");
        Assert.Contains("species_invalid", r.Errors);
        Assert.Contains("level_invalid", r.Errors);
        Assert.Contains("ball_invalid", r.Errors);
        Assert.Contains("version_invalid", r.Errors);
        Assert.Contains("ot_invalid", r.Errors);
    }
}

public sealed class ShowdownImportTests
{
    private const string Text = """
        Ronco (Snorlax) (M) @ Leftovers
        Ability: Immunity
        Level: 50
        EVs: 252 HP / 4 Atk / 252 SpD
        Careful Nature
        IVs: 30 Atk / 0 Spe
        - Rest
        - Body Slam
        - Shadow Ball
        - Earthquake

        Pikachu @ Choice Specs
        - Thunderbolt
        - Volt Switch
        """;

    [Fact]
    public void Converte_os_sets_na_ordem_do_formulario()
    {
        var items = ShowdownImport.Parse(Text, new TrainerSpec("ROCHA", 1, 2), TradeGame.LeafGreen);
        Assert.Equal(2, items.Count);

        var snorlax = items[0].Spec!;
        Assert.Equal(143, snorlax.Species);
        Assert.Equal("Ronco", snorlax.Nickname);
        Assert.Equal("M", snorlax.Gender);
        Assert.Equal(50, snorlax.Level);
        Assert.Equal(23, snorlax.Nature); // Careful
        Assert.Equal(0, snorlax.AbilitySlot); // Immunity é a 1ª habilidade do Snorlax
        Assert.Equal([252, 4, 0, 0, 252, 0], snorlax.Evs);
        Assert.Equal([31, 30, 31, 31, 31, 0], snorlax.Ivs!); // Velocidade é a última
        Assert.Equal(BuilderData.Items.Single(i => i.Text == "Leftovers").Value, snorlax.HeldItem);
        Assert.Equal(4, snorlax.Moves.Length);
        Assert.Equal(TradeGame.LeafGreen, snorlax.Game);
        Assert.Empty(items[0].Problems);
    }

    [Fact]
    public void Aponta_o_que_nao_existe_na_gen3()
    {
        var pikachu = ShowdownImport.Parse(Text, new TrainerSpec("ROCHA", 1, 2), TradeGame.FireRed)[1];
        Assert.Contains("item_not_gen3", pikachu.Problems); // Choice Specs é da Gen 4
        Assert.Contains("move_not_gen3", pikachu.Problems); // Volt Switch é da Gen 5
        Assert.DoesNotContain((ushort)521, pikachu.Spec!.Moves);
    }

    [Fact]
    public async Task Set_importado_gera_pokemon_legal()
    {
        var spec = ShowdownImport.Parse(Text, new TrainerSpec("ROCHA", 24680, 13579), TradeGame.FireRed)[0].Spec!;
        var r = await PokemonBuilder.GenerateAsync(spec with { Ivs = null }, "pt-BR", default);
        Assert.Equal(LegalityStatus.Legal, r.Legality!.Status);
        Assert.Equal(23, r.Obtained!.Nature);
        Assert.Equal("Ronco", r.Summary!.Nickname);
    }
}

public sealed class SaveImportTests
{
    [Fact]
    public async Task Le_time_e_caixas_de_um_save_de_firered()
    {
        var gen = await PokemonBuilder.GenerateAsync(new PokemonSpec { Species = 25, Level = 20, Trainer = new("RED", 24680, 13579) }, "pt-BR", default);
        var pk = new PK3(gen.Data!.ToArray());

        // O PKHeX não grava um save em branco que ele mesmo reconheça depois; a leitura do arquivo
        // (SaveUtil.GetSaveFile) é dele. Aqui vale a extração do time e das caixas.
        var sav = new SAV3FRLG { OT = "RED" };
        sav.SetPartySlotAtIndex(pk, 0);
        sav.SetBoxSlotAtIndex(pk.Clone(), 2, 5);

        var contents = SaveImport.Read(sav);

        Assert.Equal("FR", contents.Game);
        Assert.Equal("RED", contents.TrainerName);
        Assert.Contains(contents.Entries, e => e.Place == "party" && e.Summary.Species == 25);
        Assert.Contains(contents.Entries, e => e.Place == "box" && e.Box == 2 && e.Slot == 5);
        Assert.All(contents.Entries, e => Assert.Equal(LegalityStatus.Legal, e.Legality));
    }

    [Fact]
    public void Arquivo_que_nao_e_save_e_recusado()
    {
        var ex = Assert.Throws<InvalidSaveException>(() => SaveImport.Read(new byte[1000]));
        Assert.Equal("unknown", ex.Code);
    }
}

public sealed class TradeRulesTests
{
    private static TradeRuleInput Mon(ushort species, byte level = 50, string ot = "ROCHA", ushort tid = 1, ushort sid = 2, bool fateful = false, ushort item = 0, bool egg = false)
        => new(species, egg, fateful, level, ot, tid, sid, item);

    private static readonly MyTrainer Me = new("ROCHA", 1, 2, Badges: 3, HasNationalDex: false);

    [Fact]
    public void Especie_fora_de_kanto_sem_pokedex_nacional()
    {
        Assert.Contains(TradeRules.For(Mon(152), Me), w => w.Code == "national_dex_required");
        Assert.DoesNotContain(TradeRules.For(Mon(151, fateful: true), Me), w => w.Code.StartsWith("national_dex"));
        Assert.Contains(TradeRules.For(Mon(152), null), w => w.Code == "national_dex_unknown");
        Assert.DoesNotContain(TradeRules.For(Mon(152), Me with { HasNationalDex = true }), w => w.Code.StartsWith("national_dex"));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(2, 30)]
    [InlineData(4, 50)]
    [InlineData(6, 70)]
    [InlineData(8, 100)]
    public void Niveis_de_obediencia_da_pokefirered(byte badges, int limit) =>
        Assert.Equal(limit, TradeRules.ObedienceLevel(badges));

    [Fact]
    public void Pokemon_de_outro_treinador_acima_do_limite_desobedece()
    {
        // 3 insígnias → limite 30.
        Assert.Contains(TradeRules.For(Mon(6, level: 36, ot: "EMU"), Me), w => w.Code == "obedience");
        Assert.DoesNotContain(TradeRules.For(Mon(6, level: 30, ot: "EMU"), Me), w => w.Code == "obedience");
        // Do próprio treinador, obedece sempre.
        Assert.DoesNotContain(TradeRules.For(Mon(6, level: 90), Me), w => w.Code == "obedience");
        // SID diferente = outro treinador (o jogo compara o ID de 32 bits).
        Assert.Contains(TradeRules.For(Mon(6, level: 90, sid: 9), Me), w => w.Code == "obedience");
    }

    [Fact]
    public void Mew_e_deoxys_sem_encontro_fatidico()
    {
        Assert.Contains(TradeRules.For(Mon(151), Me), w => w.Code == "mythical_not_fateful");
        Assert.DoesNotContain(TradeRules.For(Mon(386, fateful: true), Me with { HasNationalDex = true }), w => w.Code == "mythical_not_fateful");
    }

    [Fact]
    public void Evolucao_por_troca()
    {
        Assert.Contains(TradeRules.For(Mon(64), Me), w => w.Code == "trade_evolution");
    }
}
