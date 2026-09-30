using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TradeRock.Core.Pokemon;
using TradeRock.Core.Storage;

namespace TradeRock.App.Server;

public sealed class AccountException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record AccountDto(Guid Id, string? Username, UserRole Role, bool Disabled, bool IsOwner, bool CanLogin,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt)
{
    public static AccountDto From(UserAccount u) => new(u.Id, u.Username, u.Role, u.Disabled, u.Id == OwnerScope.LocalOwner,
        u.Username is not null && u.PasswordHash is not null && !u.Disabled, u.CreatedAt, u.LastLoginAt);
}

/// <summary>
/// Contas do modo servidor. A do dono do PC (Id = <see cref="OwnerScope.LocalOwner"/>) existe sempre, é
/// administradora e não pode ser apagada, rebaixada nem desativada; só entra pela rede se ganhar nome e senha.
/// Cada conta nova começa com os Pokémon de demonstração no próprio cofre.
/// </summary>
public sealed partial class AccountStore(IDbContextFactory<TradeRockDb> dbFactory, PokemonStore pokemon, TimeProvider time)
{
    public const int MinPassword = 8;
    public const int MaxPassword = 128;

    [GeneratedRegex("^[a-z0-9._-]{3,24}$")]
    private static partial Regex UsernamePattern();

    /// <summary>Hash de uma senha que não existe, para o login demorar o mesmo com ou sem conta.</summary>
    private static readonly Lazy<string> DummyHash = new(() => PasswordHasher.Hash(Guid.NewGuid().ToString()));

    public static string NormalizeUsername(string? name) => (name ?? "").Trim().ToLowerInvariant();

    public async Task<IReadOnlyList<AccountDto>> ListAsync(CancellationToken ct)
    {
        await EnsureOwnerAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var users = await db.Users.AsNoTracking().ToListAsync(ct);
        return users.OrderByDescending(u => u.Id == OwnerScope.LocalOwner).ThenBy(u => u.Username)
            .Select(AccountDto.From).ToList();
    }

    public async Task<UserAccount?> FindAsync(Guid id, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<AccountDto> CreateAsync(string username, string password, UserRole role, CancellationToken ct)
    {
        username = ValidateUsername(username);
        ValidatePassword(password);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Username == username, ct))
            throw new AccountException("username_taken");
        var user = new UserAccount
        {
            Id = Guid.CreateVersion7(),
            Username = username,
            PasswordHash = PasswordHasher.Hash(password),
            Role = role,
            CreatedAt = time.GetUtcNow(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        using (OwnerScope.Use(user.Id))
        {
            foreach (var (name, data) in DemoPokemon.All)
                await pokemon.ImportAsync(data, PokemonOrigin.Demo, name, ct);
        }
        return AccountDto.From(user);
    }

    public sealed record AccountPatch(string? Username = null, string? Password = null, UserRole? Role = null, bool? Disabled = null);

    public async Task<AccountDto> UpdateAsync(Guid id, AccountPatch patch, CancellationToken ct)
    {
        await EnsureOwnerAsync(ct);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new AccountException("account_not_found");
        var isOwner = id == OwnerScope.LocalOwner;

        if (patch.Username is not null)
        {
            var username = ValidateUsername(patch.Username);
            if (await db.Users.AnyAsync(u => u.Username == username && u.Id != id, ct))
                throw new AccountException("username_taken");
            user.Username = username;
        }
        if (patch.Password is not null)
        {
            ValidatePassword(patch.Password);
            user.PasswordHash = PasswordHasher.Hash(patch.Password);
        }
        if (patch.Role is { } role)
        {
            if (isOwner && role != UserRole.Admin)
                throw new AccountException("owner_fixed");
            user.Role = role;
        }
        if (patch.Disabled is { } disabled)
        {
            if (isOwner && disabled)
                throw new AccountException("owner_fixed");
            user.Disabled = disabled;
        }
        if (isOwner && user.PasswordHash is not null && user.Username is null)
            throw new AccountException("username_invalid");
        await db.SaveChangesAsync(ct);
        return AccountDto.From(user);
    }

    /// <summary>Apaga a conta e o cofre dela (Pokémon, caixas, tags, histórico e treinadores salvos).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        if (id == OwnerScope.LocalOwner)
            throw new AccountException("owner_fixed");
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new AccountException("account_not_found");

        using (OwnerScope.Use(id))
        {
            await using var vault = await dbFactory.CreateDbContextAsync(ct);
            var trades = await vault.Trades.Select(t => t.Id).ToListAsync(ct);
            await vault.TradeEntries.Where(e => trades.Contains(e.TradeRecordId)).ExecuteDeleteAsync(ct);
            await vault.Trades.ExecuteDeleteAsync(ct);
            var mine = await vault.Pokemon.Select(p => new { p.Id, p.Sha256 }).ToListAsync(ct);
            var ids = mine.Select(p => p.Id).ToList();
            await vault.PokemonTags.Where(t => ids.Contains(t.PokemonId)).ExecuteDeleteAsync(ct);
            await vault.Pokemon.ExecuteDeleteAsync(ct);
            await vault.Tags.ExecuteDeleteAsync(ct);
            await vault.Boxes.ExecuteDeleteAsync(ct);
            await vault.TrainerPresets.ExecuteDeleteAsync(ct);
            await vault.Settings.Where(s => s.Key.EndsWith(UserSettings.Suffix(id))).ExecuteDeleteAsync(ct);

            // Arquivos que nenhum outro cofre usa.
            var hashes = mine.Select(p => p.Sha256).Distinct().ToList();
            var stillUsed = await vault.Pokemon.IgnoreQueryFilters().Where(p => hashes.Contains(p.Sha256))
                .Select(p => p.Sha256).Distinct().ToListAsync(ct);
            foreach (var hash in hashes.Except(stillUsed))
            {
                var path = pokemon.FilePath(hash);
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Confere nome e senha. Null = recusado (sem dizer se a conta existe).</summary>
    public async Task<UserAccount?> VerifyAsync(string username, string password, CancellationToken ct)
    {
        username = NormalizeUsername(username);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);
        if (user?.PasswordHash is not { } hash || user.Disabled || password.Length > MaxPassword)
        {
            PasswordHasher.Verify(password.Length > MaxPassword ? "" : password, DummyHash.Value);
            return null;
        }
        if (!PasswordHasher.Verify(password, hash))
            return null;

        if (PasswordHasher.NeedsRehash(hash))
            user.PasswordHash = PasswordHasher.Hash(password);
        user.LastLoginAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>A conta do dono existe sempre (criada na primeira vez que as contas são usadas).</summary>
    public async Task EnsureOwnerAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Id == OwnerScope.LocalOwner, ct))
            return;
        db.Users.Add(new UserAccount { Id = OwnerScope.LocalOwner, Role = UserRole.Admin, CreatedAt = time.GetUtcNow() });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // criada ao mesmo tempo por outra requisição
        }
    }

    private static string ValidateUsername(string? name)
    {
        var username = NormalizeUsername(name);
        if (!UsernamePattern().IsMatch(username))
            throw new AccountException("username_invalid");
        return username;
    }

    private static void ValidatePassword(string? password)
    {
        if (password is null || password.Length is < MinPassword or > MaxPassword)
            throw new AccountException("password_invalid");
    }
}

/// <summary>
/// Configurações por conta (hoje, só "Meu treinador"): a chave do dono é a de sempre e a das outras contas
/// ganha o sufixo "@id".
/// </summary>
public static class UserSettings
{
    public static string Suffix(Guid owner) => "@" + owner.ToString("N");

    public static string Key(string key) =>
        OwnerScope.Current == OwnerScope.LocalOwner ? key : key + Suffix(OwnerScope.Current);
}
