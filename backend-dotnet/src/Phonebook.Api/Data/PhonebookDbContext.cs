using Microsoft.EntityFrameworkCore;
using Phonebook.Api.Models;

namespace Phonebook.Api.Data;

public sealed class PhonebookDbContext : DbContext
{
    public PhonebookDbContext(DbContextOptions<PhonebookDbContext> options)
        : base(options)
    {
    }

    public DbSet<Contact> Contacts => Set<Contact>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Contact>(entity =>
        {
            entity.ToTable("contacts");

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd()
                .UseIdentityByDefaultColumn();

            entity.Property(c => c.Name)
                .HasColumnName("name")
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(c => c.PhoneNumber)
                .HasColumnName("phone_number")
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(c => c.Email)
                .HasColumnName("email")
                .HasMaxLength(255);

            entity.Property(c => c.Address)
                .HasColumnName("address")
                .HasColumnType("text");

            entity.Property(c => c.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(c => c.PhoneNumber).IsUnique();
            entity.HasIndex(c => c.Email).IsUnique();
            entity.HasIndex(c => new { c.CreatedAt, c.Id });
        });
    }
}