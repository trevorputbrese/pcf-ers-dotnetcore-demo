using System.IO.Compression;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using NuGet.Configuration;
using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tooling;
using Nuke.Common.Tools.CloudFoundry;
using Nuke.Common.Tools.Docker;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.Git;
using Nuke.Common.Tools.GitHub;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using Octokit;
using Serilog;
using Tools.CloudFoundry;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.Docker.DockerTasks;
using static Nuke.Common.Tools.CloudFoundry.CloudFoundryTasks;
// ReSharper disable TemplateIsNotCompileTimeConstantProblem


[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
//[AzureDevopsConfigurationGenerator(
//    VcsTriggeredTargets = new[]{"Pack"}
//)]
partial class Build : NukeBuild
{
    static Build()
    {
        Environment.SetEnvironmentVariable("NUKE_TELEMETRY_OPTOUT","true");
    }
    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode

    public static int Main () => Execute<Build>();

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;
    string Framework => "net6.0";
    [Parameter("GitHub personal access token with access to the repo")]
    string GitHubToken;

    [Solution] readonly Solution Solution;

    [GitRepository] GitRepository GitRepository;
//    [GitVersion] public GitVersion GitVersion { get; set; }

    [GitVersion(Framework = "net6.0")] readonly GitVersion GitVersion;
    [Parameter("Cloud Foundry Username")]
    readonly string CfUsername;
    [Parameter("Cloud Foundry Password")]
    readonly string CfPassword;
    [Parameter("Cloud Foundry Endpoint")]
    readonly string CfApiEndpoint;
    [Parameter("Cloud Foundry Org")]
    readonly string CfOrg;
    [Parameter("Cloud Foundry Space")]
    readonly string CfSpace;
    [Parameter("Type of database plan (default: db-small)")]
    readonly string DbPlan = "db-small";
    [Parameter("Type of SSO plan")]
    string SsoPlan = null;

    [Parameter("Enable parallel push. Speeds things up, but logs are only output after all pushes are done")] 
    readonly bool FastPush = true;

    
    string Runtime = "linux-x64";
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PublishDirectory => RootDirectory / "src" / "bin" / Configuration / Framework / Runtime  / "publish";
    string PackageZipName => $"articulate-{GitVersion.MajorMinorPatch}.zip";

    
    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Executes(() =>
        {
            DotNetBuild(s => s
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion)
                .EnableNoRestore());
        });

    Target Publish => _ => _
        .Description("Publishes the project to a folder which is ready to be deployed to target machines")
        .Executes(() =>
        {
            DotNetPublish(s => s
                .SetProject(Solution)
                .SetConfiguration(Configuration)
                .SetRuntime("linux-x64")
                .DisableSelfContained()
                .SetAssemblyVersion(GitVersion.AssemblySemVer)
                .SetFileVersion(GitVersion.AssemblySemFileVer)
                .SetInformationalVersion(GitVersion.InformationalVersion));
        });

    Target Pack => _ => _
        .DependsOn(Publish)
        .Description("Publishes the project and creates a zip package in artifacts folder")
        .Produces(ArtifactsDirectory)
        .Executes(() =>
        {
            Directory.CreateDirectory(ArtifactsDirectory);
            DeleteFile(ArtifactsDirectory / PackageZipName);
            ZipFile.CreateFromDirectory(PublishDirectory, ArtifactsDirectory / PackageZipName);
            Log.Information(ArtifactsDirectory / PackageZipName);
        });

    Target CfLogin => _ => _
        // .OnlyWhenStatic(() => !CfSkipLogin)
        .Requires(() => CfUsername, () => CfPassword, () => CfApiEndpoint)
        .Unlisted()
        .Executes(() =>
        {
            CloudFoundryApi(c => c.SetUrl(CfApiEndpoint));
            CloudFoundryAuth(c => c
                .SetUsername(CfUsername)
                .SetPassword(CfPassword));
        });

    Target CfTarget => _ => _
        .Requires(() => CfSpace, () => CfOrg)
        .Executes(() =>
        {
            CloudFoundryCreateSpace(c => c
                .SetOrg(CfOrg)
                .SetSpace(CfSpace));
            CloudFoundryTarget(c => c
                .SetSpace(CfSpace)
                .SetOrg(CfOrg));
        });

    Target Deploy => _ => _
        .DependsOn(CfLogin, CfTarget, Pack)
        .Description("Deploys {AppsCount} instances to Cloud Foundry /w all dependency services")
        .Executes(async () =>
        {
            var userProfileDir = (AbsolutePath)Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cfConfigFile = userProfileDir / ".cf" / "config.json";
            var cfConfig = JObject.Parse(File.ReadAllText(cfConfigFile));

            var cliTargetOrg = cfConfig.SelectToken($"OrganizationFields.Name")?.Value<string>(); 
            var cliTargetSpace = cfConfig.SelectToken($"SpaceFields.Name")?.Value<string>();
            if (cliTargetOrg is null || cliTargetSpace is null)
            {
                Assert.Fail("CF CLI is not set to an org/space");
            }
            
            var orgGuid = CloudFoundry($"org {cliTargetOrg} --guid", logOutput: false).StdToText();
            string defaultDomain = CloudFoundryCurl(o => o
                .SetPath($"/v3/organizations/{orgGuid}/domains/default")
                .DisableProcessLogOutput())
                .StdToJson<dynamic>().Name;
            string defaultInternalDomain = CloudFoundryCurl(o => o
                    .SetPath($"/v3/domains")
                    .DisableProcessLogOutput())
                .ReadPaged<dynamic>()
                .Where(x => x.@internal == true)
                .Select(x => x.name)
                .First();

            var green = "ers-green";
            var blue = "ers-blue";
            var backend = "ers-backend";
            var names = new[] { green,blue };
            var marketplace = CloudFoundry("marketplace", logOutput: false).StdToText();
            var hasMySql = marketplace.Contains("p.mysql");
            var hasDiscovery = marketplace.Contains("p.service-registry");
            var hasSso = marketplace.Contains("p-identity");
            if (hasSso && SsoPlan == null)
            {
                SsoPlan = Regex.Match(marketplace, @"(?<=^p-identity\s+)[^\s]+", RegexOptions.Multiline).Value;
            }

            if (hasDiscovery)
            {
                CloudFoundryCreateService(c => c
                    .SetService("p.service-registry")
                    .SetPlan("standard")
                    .SetInstanceName("eureka"));
            }
            else
            {
                Log.Warning("Service registry not detected in marketplace. Some demos will not be available");
            }

            if (hasMySql)
            {
                CloudFoundryCreateService(c => c
                    .SetService("p.mysql")
                    .SetPlan(DbPlan)
                    .SetInstanceName("mysql"));
            }
            else
            {
                Log.Warning("MySQL not detected in marketplace. Some demos will not be available");
            }

            if (hasSso)
            {
                CloudFoundryCreateService(c => c
                    .SetService("p-identity")
                    .SetPlan(SsoPlan)
                    .SetInstanceName("sso"));
            }
            else
            {
                Log.Warning("SSO not detected in marketplace. Some demos will not be available");
            }
            
            CloudFoundryPush(c => c
                .EnableRandomRoute()
                .EnableNoStart()
                .SetPath(ArtifactsDirectory / PackageZipName)
                .CombineWith(new[]{green, blue},(cs,v) => cs.SetAppName(v)), degreeOfParallelism: FastPush ? 3 : 1);

            CloudFoundryPush(c => c
                .SetAppName(backend)
                .EnableNoRoute()
                .EnableNoStart()
                .SetPath(ArtifactsDirectory / PackageZipName)
                .SetProcessEnvironmentVariable("ASPNETCORE_URLS", "http://0.0.0.0:8080;https://0.0.0.0:8443"));
            

            // CloudFoundryMapRoute(c => c
            //     .SetDomain(defaultDomain)
            //     .CombineWith(names, (cfg, app) => cfg
            //         .SetAppName(app)
            //         .SetHostname($"{app}-{CfSpace}-{CfOrg}"))); 
            
            CloudFoundryMapRoute(c => c
                .SetDomain(defaultInternalDomain)
                .SetAppName(backend)
                .SetHostname($"{backend}-{cliTargetSpace}-{cliTargetOrg}"));

            CloudFoundry($"add-network-policy {green} {backend} --port 8443 --protocol tcp"); // expose on ssl as well
            CloudFoundry($"add-network-policy {blue} {backend} --port 8443 --protocol tcp"); // expose on ssl as well
            
            await CloudFoundryEnsureServiceReady("eureka");
            await CloudFoundryEnsureServiceReady("mysql");
            await CloudFoundryEnsureServiceReady("sso");
            foreach (var appName in new []{blue,green,backend})
            {
                var eurekaBound = CloudFoundryBindService(c => c
                    .SetServiceInstance("eureka")
                    .SetAppName(appName))
                    .StdToText()
                    .Contains("already bound");
                var mySqlBound = CloudFoundryBindService(c => c
                        .SetServiceInstance("mysql")
                        .SetAppName(appName))
                    .StdToText()
                    .Contains("already bound");
                var ssoBound = CloudFoundryBindService(c => c
                        .SetServiceInstance("sso")
                        .SetConfigurationParameters(RootDirectory / "sso-binding.json")
                        .SetAppName(appName))
                    .StdToText()
                    .Contains("already bound");
                if (!eurekaBound || !mySqlBound || !ssoBound)
                {
                    CloudFoundryRestart(c => c
                        .SetAppName(appName));
                }
                else
                {
                    CloudFoundryStart(c => c
                        .SetAppName(appName));
                }
            }
        });

    Target Release => _ => _
        .Description("Creates a GitHub release (or amends existing) and uploads the artifact")
        .DependsOn(Publish)
        .Requires(() => GitHubToken)
        .Executes(async () =>
        {
            if (!GitRepository.IsGitHubRepository())
                Assert.Fail("Only supported when git repo remote is github");
            if(!IsGitPushedToRemote)
                Assert.Fail("Your local git repo has not been pushed to remote. Can't create release until source is upload");
            var client = new GitHubClient(new ProductHeaderValue("nuke-build"))
            {
                Credentials = new Credentials(GitHubToken, AuthenticationType.Bearer)
            };
            var gitIdParts = GitRepository.Identifier.Split("/");
            var owner = gitIdParts[0];
            var repoName = gitIdParts[1];
            
            var releaseName = $"v{GitVersion.MajorMinorPatch}";
            Release release;
            try
            {
                release = await client.Repository.Release.Get(owner, repoName, releaseName);
            }
            catch (NotFoundException)
            {
                var newRelease = new NewRelease(releaseName)
                {
                    Name = releaseName, 
                    Draft = false, 
                    Prerelease = false
                };
                release = await client.Repository.Release.Create(owner, repoName, newRelease);
            }

            var existingAsset = release.Assets.FirstOrDefault(x => x.Name == PackageZipName);
            if (existingAsset != null)
            {
                await client.Repository.Release.DeleteAsset(owner, repoName, existingAsset.Id);
            }
            
            var zipPackageLocation = ArtifactsDirectory / PackageZipName;
            var releaseAssetUpload = new ReleaseAssetUpload(PackageZipName, "application/zip", File.OpenRead(zipPackageLocation), null);
            var releaseAsset = await client.Repository.Release.UploadAsset(release, releaseAssetUpload);
            
            Log.Information(releaseAsset.BrowserDownloadUrl);
        });


    bool IsGitPushedToRemote => GitTasks
        .Git("status")
        .Select(x => x.Text)
        .Count(x => x.Contains("nothing to commit, working tree clean") || x.StartsWith("Your branch is up to date with")) == 2;

    Target RunSpringBootAdmin => _ => _
        .Executes(async () =>
        {
            var containerName = "spring-boot-admin";
            IReadOnlyCollection<Output> output = new Output[0];
            await Task.WhenAny(Task.Run(() =>
                output = DockerRun(c => c
                    .SetImage("steeltoeoss/spring-boot-admin")
                    .EnableRm()
                    .SetName(containerName)
                    .SetAttach("STDOUT", "STDERR")
                    .SetPublish("9090:8080"))
            ), Task.Delay(TimeSpan.FromSeconds(10)));
            
            output.EnsureOnlyStd();
            Log.Information("Press ENTER to shutdown...");
            Console.ReadLine();
            DockerKill(c => c
                .SetContainers(containerName));
        });
}
