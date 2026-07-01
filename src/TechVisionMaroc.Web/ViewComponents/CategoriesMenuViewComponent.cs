using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TechVisionMaroc.Data;

namespace TechVisionMaroc.ViewComponents;

public class CategoriesMenuViewComponent : ViewComponent
{
    private readonly AppDbContext _db;

    public CategoriesMenuViewComponent(AppDbContext db) => _db = db;

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var categories = await _db.Categories
            .Where(c => c.EstActive && c.ParentId == null)
            .Include(c => c.SousCategories.Where(s => s.EstActive))
            .OrderBy(c => c.Nom)
            .ToListAsync();

        return View(categories);
    }
}
