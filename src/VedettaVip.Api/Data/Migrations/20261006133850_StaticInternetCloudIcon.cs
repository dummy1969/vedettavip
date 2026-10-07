using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaticInternetCloudIcon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nodi statici "Internet" senza icona: la nuvola (icona "cloud" del catalogo Tabler del client)
            migrationBuilder.Sql("UPDATE \"MapNodes\" SET \"Icon\" = 'cloud' " +
                                 "WHERE \"Kind\" = 'Static' AND \"Icon\" IS NULL AND \"LabelTemplate\" ILIKE '%internet%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"MapNodes\" SET \"Icon\" = NULL " +
                                 "WHERE \"Kind\" = 'Static' AND \"Icon\" = 'cloud' AND \"LabelTemplate\" ILIKE '%internet%';");
        }
    }
}
