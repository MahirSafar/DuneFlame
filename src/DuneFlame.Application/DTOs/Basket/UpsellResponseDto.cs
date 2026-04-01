namespace DuneFlame.Application.DTOs.Basket;

public class UpsellResponseDto
{
    public decimal TargetThreshold { get; set; }
    public decimal CurrentSubtotal { get; set; }
    public decimal GapAmount { get; set; }
    public UpsellRecommendationDto? Recommendation { get; set; }
}
