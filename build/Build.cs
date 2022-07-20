using System.Collections;
using System.Diagnostics;
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
    string CfOrg;
    [Parameter("Cloud Foundry Space")]
    string CfSpace;
    [Parameter("App Name for inner loop")]
    string AppName = "ers";
    [Parameter("Type of database plan (default: db-small)")]
    readonly string DbPlan = "db-small";
    [Parameter("Type of SSO plan")]
    string SsoPlan;
    [Parameter("Internal domain for c2c. Optional")]
    string InternalDomain = null;
    [Parameter("Public domain to assign to apps. Optional")]
    string PublicDomain;
    [Parameter("Trigger to use to trigger live sync (Build or Source. Default to Source)")]
    SyncTrigger SyncTrigger = SyncTrigger.Source;
    [Parameter("Should CF push be performed when livesync is started. Disabling is quicker, but only works if app was previously deployed for livesync")]
    bool CfPushInit = true;



    string Runtime = "linux-x64";
    string AssemblyName = "Articulate";
    AbsolutePath SourceDirectory => RootDirectory / "src";
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    AbsolutePath PublishDirectory => RootDirectory / "src" / "bin" / Configuration / Framework / Runtime  / "publish";
    string PackageZipName => $"articulate-{GitVersion.MajorMinorPatch}.zip";

    AppDeployment[] Apps;
    AppDeployment Green, Blue, Backend;

    
    Target Clean => _ => _
        .Before(Restore)
        .Description("Clean out bin/obj folders")
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
        });

    Target Restore => _ => _
        .Description("Restore nuget packages")
        .Executes(() =>
        {
            DotNetRestore(s => s
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Description("Compiles code for local execution")
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

    Target InnerLoop => _ => _
        .Requires(() => AppName)
        .Executes(async () =>
        {
            var currentEnvVars = Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .ToDictionary(x => (string)x.Key, x => (string)x.Value);
            string os = "";
            if (OperatingSystem.IsWindows())
                os = "win";
            else if (OperatingSystem.IsLinux())
                os = "linux";
            else
                os = "osx";
            var tilt = ToolPathResolver.GetPackageExecutable($"Tilt.CommandLine.{os}-x64", "tilt" + (OperatingSystem.IsWindows() ? ".exe" : ""));
            var tiltProcess = ProcessTasks.StartProcess(tilt, "up", 
                workingDirectory: RootDirectory, 
                environmentVariables: new Dictionary<string, string>(currentEnvVars)
                {
                    {"APP_NAME", AppName},
                    {"APP_DIR", "./src"},
                    {"SYNC_TRIGGER", SyncTrigger.ToString().ToLower()},
                    {"CF_PUSH_INIT", CfPushInit.ToString().ToLower()},
                    {"AssemblyName", AssemblyName},
                    // {"PUSH_PATH", "."},
                    // {"PUSH_COMMAND", $"cd ${{HOME}} && ./watchexec --ignore *.yaml --restart --watch . 'dotnet {AssemblyName}.dll --urls http://0.0.0.0:8080'"},
                    {"TILT_WATCH_WINDOWS_BUFFER_SIZE", "555555"}
                });
            await Task.Delay(3000);
            var tiltPsi = new ProcessStartInfo
            {
                FileName = "http://localhost:10350",
                UseShellExecute = true
            };
            Process.Start(tiltPsi);
            
            tiltProcess.WaitForExit();
        });

    struct AppDeployment
    {
        public string Name { get; set; }
        public string Org { get; set; }
        public string Space { get; set; }
        public string Domain { get; set; }
        public string Hostname => $"{Name}-{Space}-{Org}";
        public bool IsInternal { get; set; }
    }

    Target DeployFull => _ => _
        .DependsOn(CfLogin, CfTarget, Deploy)
        .Executes(() =>
        {
            
        });


    Target EnsureCfCurrentTarget => _ => _
        .After(CfTarget, Pack)
        .Executes(() =>
        {
            var userProfileDir = (AbsolutePath)Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var cfConfigFile = userProfileDir / ".cf" / "config.json";
            var cfConfig = JObject.Parse(File.ReadAllText(cfConfigFile));

            CfOrg = cfConfig.SelectToken($"OrganizationFields.Name")?.Value<string>(); 
            CfSpace = cfConfig.SelectToken($"SpaceFields.Name")?.Value<string>();
            if (CfOrg is null || CfSpace is null)
            {
                Assert.Fail("CF CLI is not set to an org/space");
            }
            var orgGuid = CloudFoundry($"org {CfOrg} --guid", logOutput: false).StdToText();
            PublicDomain = CloudFoundryCurl(o => o
                    .SetPath($"/v3/organizations/{orgGuid}/domains/default")
                    .DisableProcessLogOutput())
                .StdToJson<dynamic>().name;
            InternalDomain = CloudFoundryCurl(o => o
                    .SetPath($"/v3/domains")
                    .DisableProcessLogOutput())
                .ReadPaged<dynamic>()
                .Where(x => x.@internal == true)
                .Select(x => x.name)
                .First();
            
            Assert.True(PublicDomain != null);
            Assert.True(InternalDomain != null);
        });

    Target CreateDeploymentPlan => _ => _
        .Unlisted()
        .DependsOn(EnsureCfCurrentTarget)
        .Executes(() =>
        {
            Green = new AppDeployment
            {
                Name = "ers-green",
                Org = CfOrg,
                Space = CfSpace,
                Domain = PublicDomain
            };
            Blue = Green;
            Blue.Name = "ers-blue";

            Backend = Green;
            Backend.Name = "ers-backend";
            Backend.Domain = InternalDomain;
            Backend.IsInternal = true;
            Apps = new[] { Green, Blue, Backend };
        });
    Target Deploy => _ => _
        .After(CfLogin, CfTarget)
        .DependsOn(Pack, EnsureCfCurrentTarget, CreateDeploymentPlan)
        .Description("Deploys {AppsCount} instances to Cloud Foundry /w all dependency services into current target set by cli")
        .Executes(async () =>
        {

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
                .EnableNoRoute()
                .EnableNoStart()
                .SetMemory("384M")
                .SetPath(ArtifactsDirectory / PackageZipName)
                .CombineWith(Apps,(push,app) =>
                {
                    
                    push = push
                        .SetAppName(app.Name);
                    if (app.IsInternal) // override start command as buildpack sets --urls flag which prevents us from binding to non standard ssl port
                    {
                        push = push.SetStartCommand("cd ${HOME} && ASPNETCORE_URLS='http://0.0.0.0:8080;https://0.0.0.0:8443' && exec ./Articulate");
                    }
                    return push;
                }), degreeOfParallelism: 3);

            // bind backend to both regular 8080 http port and 8443 which can be accessed directly by other apps bypassing gorouter
            CloudFoundrySetEnv(c => c
                .SetAppName(Backend.Name)
                .SetEnvVarName("ASPNETCORE_URLS")
                .SetEnvVarValue("http://0.0.0.0:8080;https://0.0.0.0:8443")); 
            
            CloudFoundrySetEnv(c => c
                .SetAppName(Backend.Name)
                .SetEnvVarName("SPRING__PROFILES__ACTIVE")
                .SetEnvVarValue("Backend")); 
            
            // CloudFoundryPush(c => c
            //     .SetAppName(backend)
            //     .EnableNoRoute()
            //     .EnableNoStart()
            //     .SetPath(ArtifactsDirectory / PackageZipName)
            //     .SetProcessEnvironmentVariable("ASPNETCORE_URLS", "http://0.0.0.0:8080;https://0.0.0.0:8443"));
            

            // CloudFoundryMapRoute(c => c
            //     .SetDomain(defaultDomain)
            //     .CombineWith(names, (cfg, app) => cfg
            //         .SetAppName(app)
            //         .SetHostname($"{app}-{CfSpace}-{CfOrg}"))); 
            
            CloudFoundryMapRoute(c => c
                .CombineWith(Apps, (cf,app) => cf
                    .SetAppName(app.Name)
                    .SetDomain(app.Domain)
                    .SetHostname(app.Hostname))
                , degreeOfParallelism: 3);

            CloudFoundry($"add-network-policy {Green.Name} {Backend.Name} --port 8443 --protocol tcp"); // expose on ssl as well
            CloudFoundry($"add-network-policy {Blue.Name} {Backend.Name} --port 8443 --protocol tcp"); // expose on ssl as well
            
            await CloudFoundryEnsureServiceReady("eureka");
            await CloudFoundryEnsureServiceReady("mysql");
            await CloudFoundryEnsureServiceReady("sso");

            
            CloudFoundryBindService(c => c
                .SetServiceInstance("eureka")
                .CombineWith(Apps, (cf,app) => cf
                    .SetAppName(app.Name)), degreeOfParallelism: 3);

            CloudFoundryBindService(c => c
                .SetServiceInstance("mysql")
                .CombineWith(Apps, (cf, app) => cf
                    .SetAppName(app.Name)), degreeOfParallelism: 3);

            CloudFoundryBindService(c => c
                .SetServiceInstance("sso")
                .SetConfigurationParameters(RootDirectory / "sso-binding.json")
                .CombineWith(Apps, (cf, app) => cf
                    .SetAppName(app.Name)), degreeOfParallelism: 3);
            

            CloudFoundryStart(c => c
                .CombineWith(Apps, (cf, app) => cf
                    .SetAppName(app.Name)), degreeOfParallelism: 3);
        });

    Target DeleteApps => _ => _
        .After(CfLogin, CfTarget)
        .DependsOn(EnsureCfCurrentTarget, CreateDeploymentPlan)
        .Executes(() =>
        {
            CloudFoundryDeleteApplication(c => c
                .EnableDeleteRoutes()
                .CombineWith(Apps, (cf, app) => cf
                    .SetAppName(app.Name)));
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
enum SyncTrigger
{
    Build,
    Source
}
