using AsphaltPlantManager.Core.Records;

namespace AsphaltPlantManager.Core.Search;

public sealed record RecordQuery
{
    public RecordQuery(
        DateOnly? From = null,
        DateOnly? To = null,
        string? TemplateId = null,
        string? Customer = null,
        string? Project = null,
        string? Specification = null,
        RecordStatus? Status = null,
        bool? HasReceivable = null,
        string? Keyword = null,
        int Page = 1,
        int PageSize = 50,
        bool IncludeDeleted = false)
    {
        if (Page < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Page), "页码必须大于或等于 1。");
        }

        if (PageSize is < 1 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(PageSize), "每页数量必须在 1 到 200 之间。");
        }

        this.From = From;
        this.To = To;
        this.TemplateId = TemplateId;
        this.Customer = Customer;
        this.Project = Project;
        this.Specification = Specification;
        this.Status = Status;
        this.HasReceivable = HasReceivable;
        this.Keyword = Keyword;
        this.Page = Page;
        this.PageSize = PageSize;
        this.IncludeDeleted = IncludeDeleted;
    }

    public DateOnly? From { get; }
    public DateOnly? To { get; }
    public string? TemplateId { get; }
    public string? Customer { get; }
    public string? Project { get; }
    public string? Specification { get; }
    public RecordStatus? Status { get; }
    public bool? HasReceivable { get; }
    public string? Keyword { get; }
    public int Page { get; }
    public int PageSize { get; }
    public bool IncludeDeleted { get; }
}
