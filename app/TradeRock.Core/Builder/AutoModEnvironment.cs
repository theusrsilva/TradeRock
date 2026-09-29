using PKHeX.Core;
using PKHeX.Core.AutoMod;

namespace TradeRock.Core.Builder;

/// <summary>
/// Configuração do PKHeX.Core.AutoMod. As opções dele são estáticas (globais no processo), então
/// são definidas uma vez aqui e toda geração passa por <see cref="RunAsync{T}"/>, uma por vez.
/// </summary>
public static class AutoModEnvironment
{
    private static readonly Lock InitLock = new();
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _initialized;

    /// <summary>Segundos máximos por geração (APILegality.Timeout).</summary>
    public const int TimeoutSeconds = 15;

    public static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (_initialized)
                return;

            // Base de Mystery Gifts embutida no PKHeX.Core (inclui os eventos oficiais da Gen 3, EncountersWC3).
            EncounterEvent.RefreshMGDB();

            // Sem isto, quando a geração falha o AutoMod troca o Pokémon por um "easter egg" (outra espécie).
            Legalizer.EnableEasterEggs = false;

            // Com o modo dev desligado, o AutoMod consulta na internet a versão mais recente do PKHeX
            // (ALMVersion → UpdateUtil.GetLatestPKHeXVersion). O TradeRock não faz requisições sozinho.
            APILegality.EnableDevMode = true;

            // O treinador vem do pedido (linhas OT/TID/SID/OTGender), não do banco de treinadores do AutoMod.
            APILegality.AllowTrainerOverride = true;
            APILegality.UseTrainerData = false;

            // Sem pedido de Poké Bola, fica a do encontro (em geral a Poké Ball comum), não uma "da cor" da espécie.
            APILegality.SetMatchingBalls = false;
            // O AutoMod troca nível 50 por 100 (pensado em times de VGC); aqui o nível pedido vale.
            APILegality.ForceLevel100for50 = false;
            APILegality.ForceSpecifiedBall = false;
            APILegality.SetAllLegalRibbons = false;
            APILegality.UseMarkings = false;
            APILegality.UseCompetitiveMarkings = false;
            APILegality.AllowBatchCommands = true;
            APILegality.Timeout = TimeoutSeconds;

            // Origem preferida: o jogo escolhido, depois o outro FRLG, depois RSE e Colosseum/XD.
            APILegality.GameVersionPriority = GameVersionPriorityType.PriorityOrder;
            APILegality.PriorityOrder = PriorityFor(GameVersion.FR);

            _initialized = true;
        }
    }

    public static List<GameVersion> PriorityFor(GameVersion target) => target == GameVersion.LG
        ? [GameVersion.LG, GameVersion.FR, GameVersion.E, GameVersion.R, GameVersion.S, GameVersion.CXD]
        : [GameVersion.FR, GameVersion.LG, GameVersion.E, GameVersion.R, GameVersion.S, GameVersion.CXD];

    /// <summary>Executa <paramref name="work"/> com exclusividade sobre as opções globais do AutoMod.</summary>
    public static async Task<T> RunAsync<T>(GameVersion target, Func<T> work, CancellationToken ct)
    {
        EnsureInitialized();
        await Gate.WaitAsync(ct);
        try
        {
            APILegality.PriorityOrder = PriorityFor(target);
            return await Task.Run(work, ct);
        }
        finally
        {
            Gate.Release();
        }
    }
}
