namespace BookDistributionAPI.Features.Governorates;

public class Governorate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ICollection<City> Cities { get; set; } = new List<City>();
}

public class City
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int GovernorateId { get; set; }
    public Governorate Governorate { get; set; } = null!;
}
