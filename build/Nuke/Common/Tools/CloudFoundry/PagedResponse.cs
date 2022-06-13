namespace Nuke.Common.Tools.CloudFoundry;

public class PagedResponse<T>
{
    public Pagination Pagination { get; set; }
    public List<T> Resources { get; set; }

}