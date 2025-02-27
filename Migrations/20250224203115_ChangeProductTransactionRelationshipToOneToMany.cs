using Microsoft.EntityFrameworkCore.Migrations;

namespace AirbnbShopApi.Migrations
{
    public partial class ChangeProductTransactionRelationshipToOneToMany : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Passo 1: Remover a chave estrangeira existente
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Products_ProductId",
                table: "Transactions");

            // Passo 2: Remover o índice único
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ProductId",
                table: "Transactions");

            // Passo 3: Recriar a chave estrangeira sem unicidade
            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Products_ProductId",
                table: "Transactions",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Passo 4: Criar um índice não único (opcional, mas melhora performance)
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProductId",
                table: "Transactions",
                column: "ProductId",
                unique: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverter: Remover o índice não único
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ProductId",
                table: "Transactions");

            // Reverter: Remover a chave estrangeira
            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Products_ProductId",
                table: "Transactions");

            // Reverter: Recriar o índice único e a chave estrangeira com unicidade implícita
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProductId",
                table: "Transactions",
                column: "ProductId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Products_ProductId",
                table: "Transactions",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}