using Serval.Application.Ipc;

namespace Serval.Web.Pages.Services;

/// <summary>One inventory table: the rows of a section plus the page that owns filtering state.</summary>
public sealed record ServiceTableModel(IndexModel Page, IReadOnlyList<AgentService> Rows, string? LabelledBy);
