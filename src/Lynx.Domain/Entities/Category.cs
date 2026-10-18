namespace Lynx.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#4f8cff";
    public bool IsIncome { get; set; }
}
