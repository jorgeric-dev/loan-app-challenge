using LoanApp.Application.Abstractions;
using LoanApp.Domain;
using LoanApp.Domain.Customers;
using LoanApp.Infrastructure.Outbox;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LoanApp.Infrastructure.Persistence;

/// <summary>
/// One SaveChanges = one database transaction, so customer, application and outbox
/// rows are committed together or not at all.
/// </summary>
public sealed class LoanDbContext(DbContextOptions<LoanDbContext> options) : DbContext(options), IUnitOfWork
{
    private const string RowVersion = "RowVersion";

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<LoanApplication> LoanApplications => Set<LoanApplication>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("The customer was modified by another request.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConcurrencyConflictException("A customer with this SSN was created by another request.", ex);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(customer =>
        {
            customer.HasKey(c => c.Id);
            customer.Property(c => c.Ssn)
                .HasConversion(ssn => ssn.Value, value => Ssn.Parse(value))
                .HasMaxLength(9).IsFixedLength();
            customer.HasIndex(c => c.Ssn).IsUnique();
            customer.Property(c => c.FirstName).HasMaxLength(100);
            customer.Property(c => c.LastName).HasMaxLength(100);
            customer.Property(c => c.CompanyName).HasMaxLength(200);
            customer.ComplexProperty(c => c.Address, address =>
            {
                address.Property(a => a.Street).HasColumnName("Street").HasMaxLength(200);
                address.Property(a => a.City).HasColumnName("City").HasMaxLength(100);
                address.Property(a => a.State).HasColumnName("State").HasMaxLength(2).IsFixedLength();
                address.Property(a => a.ZipCode).HasColumnName("ZipCode").HasMaxLength(10);
            });
            customer.HasOne(c => c.Application)
                .WithOne()
                .HasForeignKey<LoanApplication>(a => a.CustomerId)
                .IsRequired();
            customer.Navigation(c => c.Application).IsRequired();
            // Optimistic concurrency, kept out of the domain model as a shadow property.
            customer.Property<byte[]>(RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<LoanApplication>(application =>
        {
            application.HasKey(a => a.Id);
            application.Property(a => a.RequestedAmount).HasPrecision(18, 2);
            application.Property<byte[]>(RowVersion).IsRowVersion();
        });

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.HasKey(m => m.Id);
            message.Property(m => m.Type).HasMaxLength(200);
            message.Property(m => m.OrderingKey).HasMaxLength(100);
            message.Property(m => m.TraceParent).HasMaxLength(100);
            message.Property(m => m.LockedBy).HasMaxLength(200);
            message.HasIndex(m => m.EventId).IsUnique();
            // Serves the claim query: unprocessed messages, per ordering key, in order.
            message.HasIndex(m => new { m.OrderingKey, m.Id })
                .HasFilter("[ProcessedAt] IS NULL")
                .HasDatabaseName("IX_OutboxMessages_Unprocessed");
            message.HasIndex(m => m.ProcessedAt).HasFilter("[ProcessedAt] IS NOT NULL");
        });
    }
}
