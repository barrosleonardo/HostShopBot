using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirbnbShopApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutRetentionFieldsToAdminTransaction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "NetAmount",
                table: "AdminTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "RetentionAmount",
                table: "AdminTransactions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NetAmount",
                table: "AdminTransactions");

            migrationBuilder.DropColumn(
                name: "RetentionAmount",
                table: "AdminTransactions");
        }
    }
}
