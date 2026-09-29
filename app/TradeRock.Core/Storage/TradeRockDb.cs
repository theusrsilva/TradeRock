using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace TradeRock.Core.Storage;

public sealed class TradeRockDb(DbContextOptions<TradeRockDb> options) : DbContext(options)
{
    public DbSet<StoredPokemon> Pokemon => Set<StoredPokemon>();
    public DbSet<TradeRecord> Trades => Set<TradeRecord>();
    public DbSet<TradeRecordEntry> TradeEntries => Set<TradeRecordEntry>();
    public DbSet<TrainerPreset> TrainerPresets => Set<TrainerPreset>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // O SQLite não ordena DateTimeOffset; guardamos sempre em UTC como inteiro.
        builder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<StoredPokemon>(e =>
        {
            e.HasIndex(p => p.Sha256).IsUnique();
            e.HasIndex(p => p.Species);
            e.Property(p => p.Origin).HasConversion<string>();
        });

        model.Entity<TradeRecord>(e =>
        {
            e.HasIndex(t => t.StartedAt);
            e.Property(t => t.Game).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>();
            e.HasMany(t => t.Entries).WithOne().HasForeignKey(x => x.TradeRecordId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<TradeRecordEntry>(e =>
        {
            e.HasOne<StoredPokemon>().WithMany().HasForeignKey(x => x.SentPokemonId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StoredPokemon>().WithMany().HasForeignKey(x => x.ReceivedPokemonId).OnDelete(DeleteBehavior.Restrict);
        });

        model.Entity<TrainerPreset>(e => e.Property(t => t.Game).HasConversion<string>());

        model.Entity<SettingEntry>(e => e.HasKey(s => s.Key));
    }
}

/// <summary>Usado só pelo "dotnet ef migrations".</summary>
internal sealed class TradeRockDbDesignFactory : IDesignTimeDbContextFactory<TradeRockDb>
{
    public TradeRockDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<TradeRockDb>().UseSqlite("Data Source=design.db").Options);
}
