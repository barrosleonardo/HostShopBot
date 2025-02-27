namespace AirbnbShopApi.Models
{
    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public decimal Price { get; set; }
        public string OwnerTelegramId { get; set; } = null!;
        public int ApartmentId { get; set; }
        public byte[]? Image { get; set; }
        public string LockCode { get; set; } = null!;
        public bool IsAvailable { get; set; } = true;
        public Apartment Apartment { get; set; } = null!;
        public string? ImageUrl { get; set; } // Novo campo para a URL da imagem
        public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>(); // Mudança para coleção
    }
}