namespace BookDistributionAPI.Features.Analytics;

public sealed class DashboardAnalyticsDto
{
    public int TotalLibraries { get; init; }
    public int TotalItems { get; init; }
    public int LowStockCount { get; init; }
    public decimal TotalRevenue { get; init; }
    public decimal TotalCollected { get; init; }
    public decimal TotalOutstanding { get; init; }
    public int TotalItemsSold { get; init; }
    public int OrderCount { get; init; }
    public int RefundCount { get; init; }
    public List<DashboardCriticalStockDto> CriticalStock { get; init; } = [];
    public List<DashboardLibraryBalanceDto> LibraryBalances { get; init; } = [];
    public List<DashboardBookCountDto> MostRefunded { get; init; } = [];
    public List<DashboardTermSalesDto> SalesByTerm { get; init; } = [];
    public List<DashboardYearSalesDto> ClassicSalesByYear { get; init; } = [];
    public List<DashboardClassicRowDto> ClassicRows { get; init; } = [];
}

public sealed class DashboardCriticalStockDto
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Grade { get; init; } = string.Empty;
    public int StockQuantity { get; init; }
    public int Demand { get; set; }
}

public sealed class DashboardLibraryBalanceDto
{
    public int LibraryId { get; init; }
    public string LibraryName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public decimal Balance { get; init; }
}

public sealed class DashboardBookCountDto
{
    public string Name { get; init; } = string.Empty;
    public int Count { get; init; }
}

public sealed class DashboardTermSalesDto
{
    public string TermCode { get; init; } = string.Empty;
    public string TermName { get; init; } = string.Empty;
    public decimal Revenue { get; init; }
}

public sealed class DashboardYearSalesDto
{
    public string AcademicYear { get; init; } = string.Empty;
    public decimal Revenue { get; init; }
    public int Quantity { get; init; }
}

public sealed class DashboardClassicRowDto
{
    public string AcademicYear { get; init; } = string.Empty;
    public string TermCode { get; init; } = string.Empty;
    public string TermName { get; init; } = string.Empty;
    public string BestLibraryByRevenue { get; init; } = string.Empty;
    public string BestLibraryByQuantity { get; init; } = string.Empty;
    public int LibraryCount { get; init; }
    public int Ordered { get; init; }
    public int Refunded { get; init; }
    public decimal NetRevenue { get; init; }
    public int NetQuantity { get; init; }
}
