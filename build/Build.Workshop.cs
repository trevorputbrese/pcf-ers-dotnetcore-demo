using Nuke.Common;
using Serilog;
using static Nuke.Common.Tools.CloudFoundry.CloudFoundryTasks;
partial class Build
{
    [Parameter("Number of users to create")]
    readonly int NumUsers = 1;
    Target CfCreateWorkshopUsers => _ => _
        .Executes(() =>
        {
            Dictionary<string, string> users = new();
            for (var i = 1; i < NumUsers+1; i++)
            {
                var (userName, password) = GetCredentialsForUser(i);
                CloudFoundry($"create-user {userName} {password}");
                CloudFoundry($"create-org {userName}");
                CloudFoundry($"create-org {userName} {userName} OrgManager");
                users.Add(userName, password);
            }

            Log.Information(string.Join("\n", users.Select(x => $"{x.Key} / {x.Value}")));
        });

    Target CfDeleteWorkshopUsers => _ => _
        .Executes(() =>
        {
            for (int i = 1;; i++)
            {
                var (userName, password) = GetCredentialsForUser(i);
                var results = CloudFoundry($"delete-user {userName} -f");
                if (results.Any(x => x.Text.Contains("does not exist")))
                    break;
                CloudFoundry($"delete-org {userName} -f");
            }
        });

    (string userName, string password) GetCredentialsForUser(int i) => ($"user-{i}", "p@ssword1");
}