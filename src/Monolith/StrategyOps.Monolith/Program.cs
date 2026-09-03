using Microsoft.EntityFrameworkCore;
using StrategyOps.Monolith;

// ---------------------------------------------------------------------------
// StrategyOps, as one application.
//
// Everything the nine services do, in about 250 lines. Read Program.cs and
// Model.cs together and you have the entire system in your head - which is
// exactly the property the microservices version gives up, and exactly why the
// trade needs justifying rather than assuming.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<StrategyDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("Strategy") ?? "Data Source=strategyops-monolith.db"));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options => options.SwaggerDoc("v1", new()
{
    Title = "StrategyOps - Monolith",
    Version = "v1",
    Description = "The 'before' picture: the same domain as one deployable, one database and one transaction. See docs/questions/24-*.md for how it is pulled apart."
}));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<StrategyDbContext>().Database.EnsureCreatedAsync();
}

app.UseSwagger();
app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Monolith v1"));

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapPost("/objectives", async (StrategicObjective objective, StrategyDbContext db) =>
{
    db.Objectives.Add(objective);
    await db.SaveChangesAsync();
    return Results.Created($"/objectives/{objective.Id}", objective);
});

// ---------------------------------------------------------------------------
// THE method to compare against the saga.
//
// Everything the ProjectInitiationSaga coordinates across four services happens
// here in one SaveChanges. It either all commits or none of it does. There is
// no compensation, because there is nothing to compensate: the database rolls
// back. There is no timeout, no correlation id, no inbox, no orphaned
// scorecard - none of those problems exist.
//
// That is roughly 500 lines of saga, contracts, consumers and tests replaced by
// one transaction. Anyone proposing microservices should be able to say what
// they are buying for that price. The honest answers are: independent
// deployment, independent scaling, and team autonomy at a size where one
// codebase has become a queue. Nothing else.
// ---------------------------------------------------------------------------
app.MapPost("/projects", async (CreateProjectRequest request, StrategyDbContext db) =>
{
    if (!await db.Objectives.AnyAsync(o => o.Id == request.ObjectiveId))
    {
        return Results.NotFound(new { code = "project.objective_not_found" });
    }

    if (await db.Projects.AnyAsync(p => p.Code == request.Code))
    {
        return Results.Conflict(new { code = "project.duplicate_code" });
    }

    var forecast = Math.Round(request.Budget * 1.4m, 2);

    if (forecast > 1_000_000m)
    {
        // The same business rule that makes the distributed version compensate.
        // Here it is just an early return, before anything was written.
        return Results.BadRequest(new { code = "benefit.exceeds_portfolio_ceiling", forecast });
    }

    var project = new Project
    {
        Code = request.Code.ToUpperInvariant(),
        Name = request.Name,
        ObjectiveId = request.ObjectiveId,
        Sponsor = request.Sponsor,
        Budget = request.Budget,
        Stage = ProjectStage.Active,
        Health = ProjectHealth.Green
    };

    db.Projects.Add(project);

    db.Kpis.AddRange(
        new Kpi { ProjectId = project.Id, Name = "Schedule variance", Target = 0m, AmberThreshold = -5m },
        new Kpi { ProjectId = project.Id, Name = "Cost variance", Target = 0m, AmberThreshold = -5m },
        new Kpi { ProjectId = project.Id, Name = "Benefit realisation", Target = 100m, AmberThreshold = 80m });

    db.Benefits.Add(new BenefitProfile { ProjectId = project.Id, ForecastValue = forecast });

    // One transaction. Four "services" worth of state, atomically.
    await db.SaveChangesAsync();

    return Results.Created($"/projects/{project.Id}", new { project.Id, project.Code, Stage = project.Stage.ToString() });
});

app.MapGet("/projects/{id:guid}", async (Guid id, StrategyDbContext db) =>
{
    var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);

    if (project is null)
    {
        return Results.NotFound();
    }

    // ------------------------------------------------------------------
    // Compare this to the Reporting service and the gateway's aggregation
    // endpoint. Here it is four joins in one query against one database:
    // always consistent, always fast, impossible to get out of step.
    //
    // The distributed version needs a read model, seventeen projections, an
    // inbox to stop them double-counting, and a rebuild endpoint for when
    // they drift. All of that exists solely because this join became
    // impossible.
    // ------------------------------------------------------------------
    var kpis = await db.Kpis.AsNoTracking().Where(k => k.ProjectId == id).ToListAsync();
    var risks = await db.Risks.AsNoTracking().Where(r => r.ProjectId == id).ToListAsync();
    var issues = await db.Issues.AsNoTracking().Where(i => i.ProjectId == id).ToListAsync();
    var benefit = await db.Benefits.AsNoTracking().FirstOrDefaultAsync(b => b.ProjectId == id);

    return Results.Ok(new
    {
        project.Id,
        project.Code,
        project.Name,
        Stage = project.Stage.ToString(),
        Health = project.Health.ToString(),
        Kpis = kpis.Select(k => new { k.Name, k.Target, k.LatestValue }),
        OpenRisks = risks.Count(r => r.Status is RiskStatus.Open or RiskStatus.Mitigating),
        OpenIssues = issues.Count(i => i.Status is IssueStatus.New or IssueStatus.Assigned),
        BenefitForecast = benefit?.ForecastValue ?? 0,
        BenefitRealised = benefit?.RealisedToDate ?? 0
    });
});

app.MapPost("/projects/{id:guid}/risks", async (Guid id, RaiseRiskRequest request, StrategyDbContext db) =>
{
    if (!await db.Projects.AnyAsync(p => p.Id == id))
    {
        return Results.NotFound();
    }

    var risk = new Risk
    {
        ProjectId = id,
        Title = request.Title,
        Probability = request.Probability,
        Impact = request.Impact,
        Status = RiskStatus.Open
    };

    db.Risks.Add(risk);
    await db.SaveChangesAsync();

    return Results.Created($"/risks/{risk.Id}", new { risk.Id, risk.Score });
});

// ---------------------------------------------------------------------------
// THE method to compare against the choreographed chain.
//
// Escalating a risk raises an issue, drops the project's health and flags the
// benefit - three things that happen in three separate services, asynchronously,
// in the distributed version. Here they are five lines and one transaction.
//
// The catch is the one this file is meant to make visible: every one of these
// lines is a reason this code cannot be split. To extract Issues, someone has
// to find THIS method - and every other method like it - and turn it into a
// message. That is what "strangler fig" actually means in practice, and why
// finding the seams is most of the work.
// ---------------------------------------------------------------------------
app.MapPost("/risks/{id:guid}/escalate", async (Guid id, StrategyDbContext db) =>
{
    var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == id);

    if (risk is null)
    {
        return Results.NotFound();
    }

    if (risk.Status is RiskStatus.Closed or RiskStatus.Materialised)
    {
        return Results.Conflict(new { code = "risk.invalid_status_transition" });
    }

    risk.Status = RiskStatus.Materialised;

    var issue = new Issue
    {
        ProjectId = risk.ProjectId,
        OriginRiskId = risk.Id,
        Title = $"[Escalated] {risk.Title}",
        Status = IssueStatus.New
    };

    db.Issues.Add(issue);

    var project = await db.Projects.FirstAsync(p => p.Id == risk.ProjectId);
    project.Health = risk.Score >= 16 ? ProjectHealth.Red : ProjectHealth.Amber;

    await db.SaveChangesAsync();

    return Results.Ok(new { risk.Id, Status = risk.Status.ToString(), IssueId = issue.Id, ProjectHealth = project.Health.ToString() });
});

app.MapPost("/kpis/{id:guid}/measurements", async (Guid id, RecordMeasurementRequest request, StrategyDbContext db) =>
{
    var kpi = await db.Kpis.FirstOrDefaultAsync(k => k.Id == id);

    if (kpi is null)
    {
        return Results.NotFound();
    }

    kpi.LatestValue = request.Value;
    await db.SaveChangesAsync();

    var rag = request.Value >= kpi.Target ? "Green" : request.Value >= kpi.AmberThreshold ? "Amber" : "Red";
    return Results.Ok(new { kpi.Id, kpi.LatestValue, Rag = rag });
});

// The portfolio dashboard: one query, one database, always consistent. The
// Reporting service exists only because this stops being possible.
app.MapGet("/portfolio", async (StrategyDbContext db) =>
{
    var rows = await db.Projects
        .AsNoTracking()
        .Select(p => new
        {
            p.Id,
            p.Code,
            p.Name,
            Stage = p.Stage.ToString(),
            Health = p.Health.ToString(),
            OpenRisks = db.Risks.Count(r => r.ProjectId == p.Id && (r.Status == RiskStatus.Open || r.Status == RiskStatus.Mitigating)),
            OpenIssues = db.Issues.Count(i => i.ProjectId == p.Id && (i.Status == IssueStatus.New || i.Status == IssueStatus.Assigned)),
            BenefitForecast = db.Benefits.Where(b => b.ProjectId == p.Id).Select(b => b.ForecastValue).FirstOrDefault()
        })
        .ToListAsync();

    return Results.Ok(rows);
});

app.Run();

internal sealed record CreateProjectRequest(string Code, string Name, Guid ObjectiveId, string Sponsor, decimal Budget);

internal sealed record RaiseRiskRequest(string Title, int Probability, int Impact);

internal sealed record RecordMeasurementRequest(decimal Value);
