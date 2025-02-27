namespace AirbnbShopApi.Models
{
    public class Transaction
    {
        public int Id { get; set; }
        public int ProductId { get; set; }
        public Product Product { get; set; } = null!;
        public string BuyerTelegramId { get; set; } = null!;
        public decimal Amount { get; set; }
        public string PaymentId { get; set; } = null!;
        public string PaymentStatus { get; set; } = null!;
        public string? RejectionReason { get; set; } // Novo campo
        public DateTime RequestDate { get; set; } = DateTime.Now;
    }
}