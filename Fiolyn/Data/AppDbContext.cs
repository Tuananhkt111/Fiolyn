using Fiolyn.Entities ;
using Fiolyn.Entities.Base ;
using Microsoft.EntityFrameworkCore ;

namespace Fiolyn.Data ;

public class AppDbContext( DbContextOptions<AppDbContext> options ) : DbContext( options )
{
  public DbSet<BrokerAccount> BrokerAccount => Set<BrokerAccount>() ;
  public DbSet<BrokerAccountMember> BrokerAccountMember => Set<BrokerAccountMember>() ;
  public DbSet<StockTransaction> StockTransaction => Set<StockTransaction>() ;
  public DbSet<SpendAccount> SpendAccount => Set<SpendAccount>() ;
  public DbSet<SpendAccountMember> SpendAccountMember => Set<SpendAccountMember>() ;
  public DbSet<AppUser> AppUser => Set<AppUser>() ;
  public DbSet<AllowedEmail> AllowedEmail => Set<AllowedEmail>() ;
  public DbSet<RefreshToken> RefreshToken => Set<RefreshToken>() ;
  public DbSet<TransactionBase> Transaction => Set<TransactionBase>() ;
  public DbSet<TransactionCategory> TransactionCategory => Set<TransactionCategory>() ;
  public DbSet<IncomeTransaction> IncomeTransaction => Set<IncomeTransaction>() ;
  public DbSet<ExpenseTransaction> ExpenseTransaction => Set<ExpenseTransaction>() ;
  public DbSet<LendTransaction> LendTransaction => Set<LendTransaction>() ;
  public DbSet<BorrowingTransaction> BorrowingTransaction => Set<BorrowingTransaction>() ;
  public DbSet<TransferTransaction> TransferTransaction => Set<TransferTransaction>() ;
  public DbSet<AdjustmentTransaction> AdjustmentTransaction => Set<AdjustmentTransaction>() ;

  protected override void OnModelCreating( ModelBuilder modelBuilder )
  {
    base.OnModelCreating( modelBuilder ) ;

    // BrokerAccount → SpendAccount: required FK, but Restrict on delete (no [DeleteBehavior]
    // attribute exists — Fluent is the only option). The default Cascade for required relationships
    // would (a) form a multi-cascade chain into BrokerAccount/BrokerAccountMember that EF flags as
    // a dependency loop, and (b) silently delete every broker if a spend account is hard-deleted.
    // SpendAccountService blocks soft-delete while any active broker references it.
    modelBuilder.Entity<BrokerAccount>()
      .HasOne( b => b.SpendAccount )
      .WithMany()
      .HasForeignKey( b => b.SpendAccountId )
      .OnDelete( DeleteBehavior.Restrict ) ;

    // Transaction hierarchy: TPT (each leaf in its own table, base in "Transaction").
    modelBuilder.Entity<TransactionBase>().UseTptMappingStrategy() ;
    modelBuilder.Entity<TransactionBase>().ToTable( "Transaction" ) ;
    modelBuilder.Entity<IncomeTransaction>().ToTable( "IncomeTransaction" ) ;
    modelBuilder.Entity<ExpenseTransaction>().ToTable( "ExpenseTransaction" ) ;
    modelBuilder.Entity<LendTransaction>().ToTable( "LendTransaction" ) ;
    modelBuilder.Entity<BorrowingTransaction>().ToTable( "BorrowingTransaction" ) ;
    modelBuilder.Entity<TransferTransaction>().ToTable( "TransferTransaction" ) ;
    modelBuilder.Entity<AdjustmentTransaction>().ToTable( "AdjustmentTransaction" ) ;
    modelBuilder.Entity<StockTransaction>().ToTable( "StockTransaction" ) ;

    // Transaction → SpendAccount (source): Restrict so a spend account can't be hard-deleted while
    // it has transactions; SpendAccountService also blocks soft-delete via reference check.
    modelBuilder.Entity<TransactionBase>()
      .HasOne( t => t.SpendAccount )
      .WithMany()
      .HasForeignKey( t => t.SpendAccountId )
      .OnDelete( DeleteBehavior.Restrict ) ;

    // Transaction → Category: optional FK; SetNull keeps the transaction history intact when an
    // admin retires a category.
    modelBuilder.Entity<TransactionBase>()
      .HasOne( t => t.Category )
      .WithMany()
      .HasForeignKey( t => t.CategoryId )
      .OnDelete( DeleteBehavior.SetNull ) ;

    // Transaction → Payer: optional FK; SetNull so deleting a payer's AppUser doesn't cascade
    // through the ledger.
    modelBuilder.Entity<TransactionBase>()
      .HasOne( t => t.Payer )
      .WithMany()
      .HasForeignKey( t => t.PayerId )
      .OnDelete( DeleteBehavior.SetNull ) ;

    // TransferTransaction → destination SpendAccount: Restrict (mirrors source).
    modelBuilder.Entity<TransferTransaction>()
      .HasOne( t => t.DestinationSpendAccount )
      .WithMany()
      .HasForeignKey( t => t.DestinationSpendAccountId )
      .OnDelete( DeleteBehavior.Restrict ) ;

    // Global soft-delete filters
    modelBuilder.Entity<AppUser>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<AllowedEmail>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<SpendAccount>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<BrokerAccount>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<TransactionBase>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<TransactionCategory>().HasQueryFilter( e => ! e.IsDeleted ) ;
    modelBuilder.Entity<BrokerAccountMember>().HasQueryFilter( m => ! m.AppUser.IsDeleted ) ;
    modelBuilder.Entity<SpendAccountMember>().HasQueryFilter( m => ! m.AppUser.IsDeleted ) ;
    modelBuilder.Entity<RefreshToken>().HasQueryFilter( r => ! r.User.IsDeleted ) ;
  }

  public override int SaveChanges()
  {
    SetDefaultValues() ;
    return base.SaveChanges() ;
  }

  public override Task<int> SaveChangesAsync( CancellationToken cancellationToken = new() )
  {
    SetDefaultValues() ;
    return base.SaveChangesAsync( cancellationToken ) ;
  }

  private void SetDefaultValues()
  {
    var entries = ChangeTracker.Entries()
      .Where( e => e.State is EntityState.Added or EntityState.Modified ) ;

    var now = DateTime.UtcNow ;

    foreach ( var entry in entries )
    {
      if ( entry.Entity is IAuditable auditableEntity )
      {
        if ( entry.State == EntityState.Added )
        {
          auditableEntity.Created = now ;
        }

        auditableEntity.LastModified = now ;
      }
    }
  }
}
