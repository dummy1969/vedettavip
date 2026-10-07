using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VedettaVip.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SubmapSummaryLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Etichetta di default dei nodi Submap: "(sottomappa)" → riepilogo dello stato aggregato ([Summary]).
            // Le etichette personalizzate restano invariate.
            migrationBuilder.Sql("UPDATE \"MapNodes\" SET \"LabelTemplate\" = E'[Name]\n[Summary]' " +
                                 "WHERE \"Kind\" = 'Submap' AND \"LabelTemplate\" = E'[Name]\n(sottomappa)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE \"MapNodes\" SET \"LabelTemplate\" = E'[Name]\n(sottomappa)' " +
                                 "WHERE \"Kind\" = 'Submap' AND \"LabelTemplate\" = E'[Name]\n[Summary]';");
        }
    }
}
