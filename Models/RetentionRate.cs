namespace AirbnbShopApi.Models
{
    public class RetentionRate
    {
        public int Id { get; set; }
        public decimal Rate { get; set; } // Taxa de retenção (ex.: 0.1 para 10%)
        public DateTime EffectiveDate { get; set; } = DateTime.Now;
    }
}