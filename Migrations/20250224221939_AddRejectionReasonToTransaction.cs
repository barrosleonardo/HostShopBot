using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirbnbShopApi.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectionReasonToTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectionReason",
                table: "Transactions",
                type: "longtext",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectionReason",
                table: "Transactions");
        }
    }
}
