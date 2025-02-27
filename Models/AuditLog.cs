namespace AirbnbShopApi.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string UserId { get; set; } = null!;
        public string Action { get; set; } = null!;
        public string EntityType { get; set; } = null!;
        public int? EntityId { get; set; }
        public string Details { get; set; } = null!;
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public ApplicationUser User { get; set; } = null!;
    }
}