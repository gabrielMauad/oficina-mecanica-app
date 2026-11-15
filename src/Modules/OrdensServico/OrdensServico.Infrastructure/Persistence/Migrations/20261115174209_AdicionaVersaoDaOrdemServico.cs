using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrdensServico.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdicionaVersaoDaOrdemServico : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "versao",
                schema: "ordem_servico",
                table: "ordem_servico",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "versao",
                schema: "ordem_servico",
                table: "ordem_servico");
        }
    }
}
