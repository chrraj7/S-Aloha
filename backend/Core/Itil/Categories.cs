using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Records;

namespace SAloha.Api.Core.Itil;

/// <summary>ADM-50 : catégorie d'incident ou de problème, tenue par les gestionnaires (SOC-27).</summary>
public class Category
{
    public static readonly string[] Practices = ["incident", "problem"];

    public int Id { get; set; }
    [MaxLength(20)] public string Practice { get; set; } = "incident";
    [MaxLength(100)] public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedBy { get; set; } = "";

    /// <summary>
    /// ADM-51 : une catégorie saisie ou changée doit être une catégorie active de la pratique —
    /// dès que la pratique en a au moins une ; avant, la saisie reste libre (installation neuve).
    /// Message (400) ou null ; la valeur est ramenée au nom exact de la liste.
    /// </summary>
    public static async Task<(string? Error, string Value)> CheckAsync(AppDbContext db, string practice, string? value, string? before)
    {
        var name = value?.Trim() ?? "";
        if (name == "" || string.Equals(name, before, StringComparison.OrdinalIgnoreCase)) return (null, name);
        var active = await db.Categories.AsNoTracking().Where(c => c.Practice == practice && c.Active).Select(c => c.Name).ToListAsync();
        if (active.Count == 0) return (null, name);
        var match = active.FirstOrDefault(c => string.Equals(c, name, StringComparison.OrdinalIgnoreCase));
        return match is not null ? (null, match)
            : ($"Catégorie inconnue : « {name} ». Catégories : {string.Join(", ", active.Order())}.", name);
    }
}

public record CategoryView(int Id, string Practice, string Name, bool Active, int Usage);
public record CategoryDto(string Practice, string Name, bool Active = true);

/// <summary>ADM-50 à ADM-52 : lecture par tous (formulaires), tenue par les gestionnaires (Paramétrage).</summary>
[ApiController]
[Route("api/categories")]
[Authorize(Policy = "User")]
public class CategoriesController(AppDbContext db) : ControllerBase
{
    internal static Task<Dictionary<string, int>> UsageAsync(AppDbContext db, string practice) => practice == "incident"
        ? db.Incidents.AsNoTracking().Where(i => i.Category != "").GroupBy(i => i.Category.ToLower()).ToDictionaryAsync(g => g.Key, g => g.Count())
        : db.Problems.AsNoTracking().Where(p => p.Category != "").GroupBy(p => p.Category.ToLower()).ToDictionaryAsync(g => g.Key, g => g.Count());

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string practice)
    {
        if (Allowed.Check("Pratique", practice, Category.Practices) is { } error) return BadRequest(new { message = error });
        var usage = await UsageAsync(db, practice);
        var list = await db.Categories.AsNoTracking().Where(c => c.Practice == practice).OrderBy(c => c.Name).ToListAsync();
        return Ok(list.Select(c => new CategoryView(c.Id, c.Practice, c.Name, c.Active, usage.GetValueOrDefault(c.Name.ToLower()))));
    }

    [HttpPost]
    [Authorize(Policy = "Manager")]
    public async Task<IActionResult> Create(CategoryDto dto)
    {
        var c = new Category { CreatedBy = User.Identity?.Name ?? "" };
        if (await ApplyAsync(c, dto) is { } error) return BadRequest(new { message = error });
        db.Categories.Add(c);
        await db.SaveChangesAsync();
        return Ok(new CategoryView(c.Id, c.Practice, c.Name, c.Active, 0));
    }

    /// <summary>Renommer ou (dés)activer. Renommer ne réécrit pas les enregistrements existants.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = "Manager")]
    public async Task<IActionResult> Update(int id, CategoryDto dto)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        if (dto.Practice != c.Practice) return BadRequest(new { message = "Une catégorie ne change pas de pratique." });
        if (await ApplyAsync(c, dto) is { } error) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        return Ok(new CategoryView(c.Id, c.Practice, c.Name, c.Active, (await UsageAsync(db, c.Practice)).GetValueOrDefault(c.Name.ToLower())));
    }

    /// <summary>Une catégorie utilisée ne se supprime pas : elle se désactive (409).</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await db.Categories.FirstOrDefaultAsync(x => x.Id == id);
        if (c is null) return NotFound();
        var used = (await UsageAsync(db, c.Practice)).GetValueOrDefault(c.Name.ToLower());
        if (used > 0) return Conflict(new { message = $"« {c.Name} » est utilisée par {used} enregistrement(s) : la désactiver plutôt que la supprimer." });
        db.Categories.Remove(c);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<string?> ApplyAsync(Category c, CategoryDto dto)
    {
        if (Allowed.Check("Pratique", dto.Practice, Category.Practices) is { } error) return error;
        var name = dto.Name?.Trim() ?? "";
        if (name == "") return "Nommer la catégorie.";
        var key = name.ToLower();
        if (await db.Categories.AnyAsync(x => x.Id != c.Id && x.Practice == dto.Practice && x.Name.ToLower() == key))
            return $"La catégorie « {name} » existe déjà.";
        c.Practice = dto.Practice; c.Name = name; c.Active = dto.Active;
        return Lengths.Check(c);
    }
}

/// <summary>
/// OPT-13 : les catégories d'une pratique, dans la vue de l'API publique, pour le conteneur
/// <c>features</c> : un voisin ne vote que pour une catégorie active (TRI-03, ADM-51).
/// </summary>
[ApiController]
[Route("internal/categories")]
[Authorize(Policy = SAloha.Api.Core.Features.ContainerTokenHandler.Policy)]
[SAloha.Api.Core.Features.InternalFeature(SAloha.Api.Core.Features.FeatureCatalog.AssistedTriage)]
public class InternalCategoriesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string practice)
    {
        if (Allowed.Check("Pratique", practice, Category.Practices) is { } error) return BadRequest(new { message = error });
        var usage = await CategoriesController.UsageAsync(db, practice);
        var list = await db.Categories.AsNoTracking().Where(c => c.Practice == practice).OrderBy(c => c.Name).ToListAsync();
        return Ok(list.Select(c => new CategoryView(c.Id, c.Practice, c.Name, c.Active, usage.GetValueOrDefault(c.Name.ToLower()))));
    }
}
