using System;
using System.Collections.Generic;
using CareBridge.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore; // [AUTH] Provides IdentityDbContext for user, role, and login management.

namespace CareBridge.Api.Data;

// [AUTH] Inherit from IdentityDbContext<ApplicationUser> so Identity can create and manage
// authentication tables such as AspNetUsers and AspNetRoles.
// This keeps user security data centralized and supports compliance requirements
// by controlling who can access patient information.
public partial class CareBridgeDbContext : IdentityDbContext<ApplicationUser>
{
    public CareBridgeDbContext()
    {
    }

    public CareBridgeDbContext(DbContextOptions<CareBridgeDbContext> options)
        : base(options) // Calls IdentityDbContext constructor so Identity features work correctly.
    {
    }

    // Existing business tables remain unchanged.
    public virtual DbSet<Insurance> Insurances { get; set; }
    public virtual DbSet<Patient> Patients { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
#warning To protect potentially sensitive information in your connection string,
        // you should move it out of source code. See https://go.microsoft.com/fwlink/?linkid=2131148
        => optionsBuilder.UseSqlServer(
               "Server=localhost;Database=CareBridgeDB;" +
               "Trusted_Connection=True;TrustServerCertificate=True;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // [AUTH - REQUIRED]
        // Allows ASP.NET Identity to configure its tables (AspNetUsers, AspNetRoles, etc.).
        // Without this line, Identity tables are not created and migrations will fail.
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Insurance>(entity =>
        {
            entity.HasKey(e => e.InsuranceId).HasName("PK__Insuranc__74231A247A894884");
            entity.ToTable("Insurance");

            entity.Property(e => e.Payer).HasMaxLength(120);
            entity.Property(e => e.PolicyNumber).HasMaxLength(50);

            entity.HasOne(d => d.Patient).WithMany(p => p.Insurances)
                .HasForeignKey(d => d.PatientId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__Insurance__Patie__412EB0B6");
        });

        modelBuilder.Entity<Patient>(entity =>
        {
            entity.HasKey(e => e.PatientId).HasName("PK__Patient__970EC366C78A1E40");

            entity
                .ToTable("Patient")
                .ToTable(tb => tb.IsTemporal(ttb =>
                {
                    ttb.UseHistoryTable("Patient_History", "dbo");
                    ttb.HasPeriodStart("ValidFrom").HasColumnName("ValidFrom");
                    ttb.HasPeriodEnd("ValidTo").HasColumnName("ValidTo");
                }));

            entity.HasIndex(e => e.Mrn, "UQ__Patient__C790FDB4F895781F").IsUnique();

            entity.Property(e => e.City).HasMaxLength(100);
            entity.Property(e => e.FullName).HasMaxLength(150);

            entity.Property(e => e.Gender)
                .HasMaxLength(1).IsUnicode(false).IsFixedLength();

            entity.Property(e => e.IsActive).HasDefaultValue(true);
            entity.Property(e => e.Mrn).HasMaxLength(20).HasColumnName("MRN");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
