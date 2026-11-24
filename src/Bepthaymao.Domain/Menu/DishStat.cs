namespace Bepthaymao.Domain.Menu;

public sealed class DishStat
{
    public Guid DishId { get; set; }
    public Guid BranchId { get; set; }
    public long OrderCount { get; set; }
}
