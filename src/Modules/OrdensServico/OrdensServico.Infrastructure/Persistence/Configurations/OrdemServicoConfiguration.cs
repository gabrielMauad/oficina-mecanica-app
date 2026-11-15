using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrdensServico.Adapters.DataSources.Records;

namespace OrdensServico.Infrastructure.Persistence.Configurations;

internal sealed class OrdemServicoConfiguration : IEntityTypeConfiguration<OrdemServicoRecord>
{
    public void Configure(EntityTypeBuilder<OrdemServicoRecord> builder)
    {
        builder.ToTable("ordem_servico");

        builder.HasKey(os => os.Id);
        builder.Property(os => os.Id)
            .HasColumnName("id");

        builder.Property(os => os.ClienteId)
            .HasColumnName("cliente_id")
            .IsRequired();

        builder.Property(os => os.VeiculoId)
            .HasColumnName("veiculo_id")
            .IsRequired();

        builder.Property(os => os.Status)
            .HasColumnName("status")
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(os => os.DescricaoDiagnostico)
            .HasColumnName("descricao_diagnostico");

        builder.Property(os => os.NotificadoEm)
            .HasColumnName("notificado_em");

        builder.Property(os => os.EntregueEm)
            .HasColumnName("entregue_em");

        builder.Property(s => s.CriadoEm)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(s => s.AtualizadoEm)
            .HasColumnName("updated_at")
            .IsRequired();

        builder.Property(s => s.StatusAlteradoEm)
            .HasColumnName("status_alterado_em")
            .IsRequired();

        // Concorrência otimista: o UPDATE só vale se a versão ainda for a lida. O repositório
        // incrementa a versão a cada gravação, mesmo quando só as entidades filhas mudam.
        builder.Property(s => s.Versao)
            .HasColumnName("versao")
            .IsConcurrencyToken()
            .IsRequired();

        builder.HasMany(os => os.ItensServico)
            .WithOne()
            .HasForeignKey("ordem_servico_id")
            .IsRequired();

        builder.HasMany(os => os.ItensPeca)
            .WithOne()
            .HasForeignKey("ordem_servico_id")
            .IsRequired();

        builder.HasMany(os => os.Orcamentos)
            .WithOne()
            .HasForeignKey("ordem_servico_id")
            .IsRequired();
    }
}
