namespace AirbnbShopApi.Models
{
    public enum ReconciliationType
    {
        Produtos = 1,
        Payout = 2,
        Duvidas = 3,
        Outros = 4
    }

    public enum ProtocolStatus
    {
        Aberto = 1,
        EmProgresso = 2,
        Fechado = 3
    }

    public class ReconciliationProtocol
    {
        public int Id { get; set; }
        public int TransactionId { get; set; }
        public ReconciliationType Type { get; set; }
        public ProtocolStatus Status { get; set; }
        public string? InitiatorId { get; set; } // Tornando anulável
        public string? OwnerId { get; set; } // Tornando anulável
        public DateTime CreatedDate { get; set; }
        public List<ProtocolComment> Comments { get; set; } = new List<ProtocolComment>();
    }

    public class ProtocolComment
    {
        public int Id { get; set; }
        public int ReconciliationProtocolId { get; set; }
        public string? UserId { get; set; } // Tornando anulável
        public string? Comment { get; set; } // Tornando anulável
        public DateTime CommentDate { get; set; }
    }
}