using Ber2Bytes.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Ber2Bytes.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<ReturnRequest> Returns => Set<ReturnRequest>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<StoreSettings> Settings => Set<StoreSettings>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite has no decimal type. Storing money as REAL lets SQLite sort and compare it.
        // Values are always rounded to 2 decimals in code, so this is safe for pesos.
        // (This also applies to nullable decimal? properties.)
        builder.Properties<decimal>().HaveConversion<double>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Username).IsUnique();
        b.Entity<Category>().HasIndex(x => x.Name).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Sku).IsUnique();
        b.Entity<Product>().HasIndex(x => x.Barcode);
        b.Entity<Product>().HasIndex(x => x.Name);
        b.Entity<Sale>().HasIndex(x => x.InvoiceNo).IsUnique();
        b.Entity<Sale>().HasIndex(x => x.Date);
        b.Entity<StockMovement>().HasIndex(x => x.Date);
        b.Entity<ReturnRequest>().HasIndex(x => x.ReturnNo).IsUnique();
        b.Entity<AuditLog>().HasIndex(x => x.Date);

        // Never cascade-delete business history.
        b.Entity<Product>().HasOne(x => x.Category).WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne(x => x.Supplier).WithMany(x => x.Products)
            .HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.SetNull);
        b.Entity<Sale>().HasOne(x => x.Cashier).WithMany()
            .HasForeignKey(x => x.CashierId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SaleItem>().HasOne(x => x.Sale).WithMany(x => x.Items)
            .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<SaleItem>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReturnRequest>().HasOne(x => x.Sale).WithMany()
            .HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReturnRequest>().HasOne(x => x.SaleItem).WithMany()
            .HasForeignKey(x => x.SaleItemId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReturnRequest>().HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReturnRequest>().HasOne(x => x.CreatedBy).WithMany()
            .HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ReturnRequest>().HasOne(x => x.ProcessedBy).WithMany()
            .HasForeignKey(x => x.ProcessedById).OnDelete(DeleteBehavior.Restrict);

        b.Entity<StoreSettings>().Property(x => x.Id).ValueGeneratedNever();
    }
}
