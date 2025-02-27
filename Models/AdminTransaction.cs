namespace AirbnbShopApi.Models
{
    public enum PayoutStatus
    {
        Solicitado,
        Processado,
        Falha
    }

    public class AdminTransaction
    {
        public int Id { get; set; }
        public int ApartmentId { get; set; }
        public Apartment Apartment { get; set; } = null!;
        public string AdminId { get; set; } = null!;
        public decimal Amount { get; set; } // Valor bruto solicitado
        public decimal NetAmount { get; set; } // Valor líquido após taxa
        public decimal RetentionAmount { get; set; } // Taxa retida
        public string PixKey { get; set; } = null!;
        public PayoutStatus Status { get; set; }
        public string? FailureReason { get; set; }
        public string? ReceiptUrl { get; set; }
        public DateTime RequestDate { get; set; } = DateTime.Now;
    }
}