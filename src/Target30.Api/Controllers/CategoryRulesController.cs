using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Target30.Api.Data;

namespace Target30.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/category-rules")]
public class CategoryRulesController : ControllerBase
{
    private readonly Target30DbContext _db;

    public CategoryRulesController(Target30DbContext db)
    {
        _db = db;
    }

    private string CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> GetRules()
    {
        var rules = await _db.CategoryRules
            .Where(r => r.UserId == CurrentUserId)
            .OrderBy(r => r.MerchantKey)
            .ToListAsync();
        return Ok(rules.Select(r => new CategoryRuleDto(r.Id, r.MerchantKey, r.Category)));
    }

    // Remove só a regra (deixa de valer pras próximas transações): o que já foi recategorizado
    // continua como está — dá pra ajustar transação por transação.
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRule(int id)
    {
        var rule = await _db.CategoryRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == CurrentUserId);
        if (rule is null)
            return NotFound();

        _db.CategoryRules.Remove(rule);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}

public record CategoryRuleDto(int Id, string MerchantKey, string Category);
