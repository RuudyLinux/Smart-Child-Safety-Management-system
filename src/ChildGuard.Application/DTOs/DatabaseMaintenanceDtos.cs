namespace ChildGuard.Application.DTOs;

public record DatabaseTableCountDto(string Table, int Rows);

/// <param name="Provider">MySql or PostgreSQL.</param>
/// <param name="Tables">Application tables and their row counts; administrator accounts are listed separately.</param>
public record DatabaseOverviewDto(string Provider, string? LatestMigration, int Admins, IReadOnlyList<DatabaseTableCountDto> Tables)
{
    public int TotalRows => Tables.Sum(t => t.Rows);
}

/// <param name="Confirmation">Must be the word RESET: a reset cannot be undone except by importing a backup.</param>
/// <param name="LoadDemoData">After clearing, load the demo school (routes, buses, drivers, families, cards, history).</param>
public record DatabaseResetRequest(string Confirmation, bool LoadDemoData = true);

/// <param name="Message">One-line outcome for the admin.</param>
/// <param name="Tables">Row counts after the operation.</param>
public record DatabaseOperationResultDto(string Message, IReadOnlyList<DatabaseTableCountDto> Tables);
