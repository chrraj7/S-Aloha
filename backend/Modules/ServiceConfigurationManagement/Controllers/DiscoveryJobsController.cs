using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SAloha.Api.Core.Data;
using SAloha.Api.Core.Perimeters;
using SAloha.Api.Core.Records;
using SAloha.Api.Modules.ServiceConfigurationManagement.Discovery;

namespace SAloha.Api.Modules.ServiceConfigurationManagement.Controllers;

/// <summary>
/// Tâches de scan (CFG-46) : paramétrage par les gestionnaires de leurs périmètres,
/// lancement manuel, suppression par l'Admin.
/// </summary>
[ApiController]
[Route("api/discovery/jobs")]
[Authorize(Policy = "User")]
public class DiscoveryJobsController(AppDbContext db, PerimeterAccess access, DiscoveryQueue queue) : ControllerBase
{
    private async Task<PerimeterScope> ScopeAsync() => await access.GetAsync();

    private static object View(DiscoveryJob j) => new
    {
        j.Id, j.PerimeterId, j.Name, j.ScanType, j.TargetRange, j.PortList, j.FrequencyHours,
        WindowStart = j.WindowStart?.ToString("HH:mm"), WindowEnd = j.WindowEnd?.ToString("HH:mm"),
        j.CredentialId, Credential = j.Credential == null ? null : new { j.Credential.Id, j.Credential.Name, j.Credential.CredentialType },
        j.IsActive, j.AssistedCmdb, j.LastRunAt, j.LastStatus, j.LastError, j.LastFoundCount, j.CreatedAt, j.CreatedBy
    };

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var scope = await ScopeAsync();
        var ids = scope.ManagedIds.ToList();
        if (!scope.Global && ids.Count == 0) return Forbid();
        var jobs = await db.Set<DiscoveryJob>().AsNoTracking().Include(j => j.Credential)
            .Where(j => scope.Global || ids.Contains(j.PerimeterId)).OrderBy(j => j.Name).ToListAsync();
        return Ok(jobs.Select(View));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var job = await db.Set<DiscoveryJob>().AsNoTracking().Include(j => j.Credential).FirstOrDefaultAsync(j => j.Id == id);
        return job is null || !(await ScopeAsync()).ManagerOn(job.PerimeterId) ? NotFound() : Ok(View(job));
    }

    [HttpPost]
    public async Task<IActionResult> Create(DiscoveryJobDto dto)
    {
        var (perimeterId, refused, forbidden) = await access.ChooseAsync(dto.PerimeterId);
        if (forbidden || perimeterId is null && refused is null) return Forbid();
        if (refused is not null) return BadRequest(new { message = refused });
        if (!(await ScopeAsync()).ManagerOn(perimeterId!.Value)) return Forbid();
        var job = new DiscoveryJob { PerimeterId = perimeterId.Value, CreatedBy = User.Identity?.Name ?? "" };
        if (await ApplyAsync(job, dto) is { } error) return BadRequest(new { message = error });
        db.Add(job);
        await db.SaveChangesAsync();
        await db.Entry(job).Reference(j => j.Credential).LoadAsync();
        return CreatedAtAction(nameof(Get), new { id = job.Id }, View(job));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, DiscoveryJobDto dto)
    {
        var job = await db.Set<DiscoveryJob>().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null || !(await ScopeAsync()).ManagerOn(job.PerimeterId)) return NotFound();
        if (await ApplyAsync(job, dto) is { } error) return BadRequest(new { message = error });
        await db.SaveChangesAsync();
        await db.Entry(job).Reference(j => j.Credential).LoadAsync();
        return Ok(View(job));
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var job = await db.Set<DiscoveryJob>().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null) return NotFound();
        if (job.LastStatus == DiscoveryJob.Running) return BadRequest(new { message = "Ce scan est en cours : attendre sa fin pour supprimer la tâche." });
        db.Remove(job);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Lancement immédiat, hors fenêtre : la tâche rejoint la file d'exécution (202).</summary>
    [HttpPost("{id:int}/run")]
    public async Task<IActionResult> Run(int id)
    {
        var job = await db.Set<DiscoveryJob>().FirstOrDefaultAsync(j => j.Id == id);
        if (job is null || !(await ScopeAsync()).ManagerOn(job.PerimeterId)) return NotFound();
        if (job.LastStatus is DiscoveryJob.Queued or DiscoveryJob.Running)
            return BadRequest(new { message = $"Ce scan est déjà {job.LastStatus.ToLowerInvariant()}." });
        job.LastStatus = DiscoveryJob.Queued;
        await db.SaveChangesAsync();
        queue.Enqueue(job.Id);
        return Accepted(new { job.Id, job.LastStatus });
    }

    private async Task<string?> ApplyAsync(DiscoveryJob job, DiscoveryJobDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return "Nommer la tâche de scan.";
        if (Allowed.Check("Type de scan", dto.ScanType, DiscoveryJob.ScanTypes) is { } error) return error;
        var target = dto.TargetRange?.Trim() ?? "";
        if (DiscoveryJob.ApiScanTypes.Contains(dto.ScanType))
        {
            if (HypervisorApi.Validate(target) is { } urlError) return urlError;
        }
        else if (dto.ScanType != "ActiveDirectory" && IpRange.Validate(target) is { } rangeError) return rangeError;
        var ports = dto.ScanType == "IpSweep" ? (string.IsNullOrWhiteSpace(dto.PortList) ? DiscoveryJob.DefaultPorts : dto.PortList) : "";
        if (dto.ScanType == "IpSweep" && IpRange.Ports(ports, out var portError) is null) return portError;
        // SSH, WinRM : un seul port, 22 ou 5986 par défaut (CFG-52, CFG-57).
        if (dto.ScanType is "Ssh" or "WinRm" && !string.IsNullOrWhiteSpace(dto.PortList))
        {
            if (IpRange.Ports(dto.PortList, out var sshError) is not [_])
                return sshError ?? $"Un scan {(dto.ScanType == "Ssh" ? "SSH" : "WinRM")} vise un seul port ({(dto.ScanType == "Ssh" ? 22 : 5986)} par défaut).";
            ports = dto.PortList.Trim();
        }
        if (dto.FrequencyHours is < 0 or > 24 * 31) return "Fréquence en heures : 0 (lancement manuel) à 744 (un mois).";
        TimeOnly? start = null, end = null;
        if (!string.IsNullOrWhiteSpace(dto.WindowStart) || !string.IsNullOrWhiteSpace(dto.WindowEnd))
        {
            if (!TimeOnly.TryParse(dto.WindowStart, out var s) || !TimeOnly.TryParse(dto.WindowEnd, out var e))
                return "Fenêtre de scan : heures de début et de fin au format HH:mm, ou aucune des deux.";
            (start, end) = (s, e);
        }

        DiscoveryCredential? credential = null;
        if (dto.CredentialId is { } credId)
        {
            credential = await db.Set<DiscoveryCredential>().AsNoTracking().FirstOrDefaultAsync(c => c.Id == credId);
            if (credential is null || credential.PerimeterId != job.PerimeterId)
                return "Choisir un identifiant du coffre du même périmètre que la tâche.";
        }
        string[] expected = dto.ScanType switch { "Snmp" => ["SnmpV2c", "SnmpV3"], "ActiveDirectory" => ["WindowsDomain"], "Ssh" => ["Ssh"],
            "Proxmox" => ["ProxmoxToken"], "VCenter" => ["VCenter"], "WinRm" => ["WindowsDomain"], _ => [] };
        if (dto.ScanType == "Snmp" && credential is null) return "Un scan SNMP exige une communauté v2c ou un utilisateur v3 du coffre.";
        if (dto.ScanType == "Ssh" && credential is null) return "Un scan SSH exige un compte SSH du coffre.";
        if (dto.ScanType == "WinRm" && credential is null) return "Un scan WinRM exige un compte Windows (de domaine ou local) du coffre.";
        if (DiscoveryJob.ApiScanTypes.Contains(dto.ScanType) && credential is null)
            return dto.ScanType == "Proxmox" ? "Un scan Proxmox VE exige un jeton d'API du coffre." : "Un scan vCenter exige un compte vCenter du coffre.";
        if (credential is not null && !expected.Contains(credential.CredentialType))
            return expected.Length == 0 ? "Un balayage IP n'utilise pas d'identifiant."
                : $"Un scan {dto.ScanType} attend un identifiant {string.Join(" ou ", expected)}, pas {credential.CredentialType}.";

        job.Name = dto.Name.Trim();
        job.ScanType = dto.ScanType;
        job.TargetRange = target;
        job.PortList = ports;
        job.CredentialId = credential?.Id;
        job.FrequencyHours = dto.FrequencyHours;
        (job.WindowStart, job.WindowEnd) = (start, end);
        job.IsActive = dto.IsActive;
        job.AssistedCmdb = dto.AssistedCmdb;
        return Lengths.Check(job);
    }
}
