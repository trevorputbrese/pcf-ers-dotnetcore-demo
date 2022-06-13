using System.Text.RegularExpressions;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.CloudFoundry;

namespace Tools.CloudFoundry;

public static class CloudFoundryExtensions
{
    public static IEnumerable<T> ReadPaged<T>(this IReadOnlyCollection<Output> pagedResult, CancellationToken cancellationToken = default)
    {
        string nextUrl;
        do
        {
            var page = pagedResult.StdToJson<PagedResponse<T>>();
            foreach (var item in page.Resources)
            {
                yield return item;
            }

            nextUrl = page.Pagination.Next?.Href;
            if (nextUrl == null) break;
            nextUrl = Regex.Replace(nextUrl, "https?://[^/]+", "");
            pagedResult = CloudFoundryTasks.CloudFoundryCurl(o => o.SetPath(nextUrl));

        } while (true);
    }
 
}