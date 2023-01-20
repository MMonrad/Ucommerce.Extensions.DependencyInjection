using System.Linq;
using Nuke.Common;
using Nuke.Common.CI;
using Nuke.Common.Execution;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[ShutdownDotNetAfterServerBuild]
class Build : NukeBuild
{
    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    [GitRepository] readonly GitRepository GitRepository;
    [GitVersion] readonly GitVersion GitVersion;
    [Solution] readonly Solution Solution;
    [Parameter] string NugetApiKey;
    [Parameter] string NugetApiUrl = "https://api.nuget.org/v3/index.json"; //default
    AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";

    Target Clean =>
        _ => _
            .Before(Restore)
            .Executes(() =>
            {
                SourceDirectory.GlobDirectories("**/bin", "**/obj")
                    .ForEach(DeleteDirectory);
                EnsureCleanDirectory(ArtifactsDirectory);
            });

    Target Compile =>
        _ => _
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

    AbsolutePath NugetDirectory => ArtifactsDirectory / "nuget";

    Target Pack =>
        _ => _
            .DependsOn(Compile)
            .Executes(() =>
            {
                string NuGetVersionCustom = GitVersion.NuGetVersionV2;

                //if it's not a tagged release - append the commit number to the package version
                //tagged commits on master have versions
                // - v0.3.0-beta
                //other commits have
                // - v0.3.0-beta1

                if (int.TryParse(GitVersion.CommitsSinceVersionSource, out var commitNum))
                {
                    NuGetVersionCustom = commitNum > 0 ? NuGetVersionCustom + $"{commitNum}" : NuGetVersionCustom;
                }

                DotNetPack(s => s
                    .SetProject(Solution
                        .GetProject("Ucommerce.Extensions.DependencyInjection"))
                    .SetConfiguration(Configuration)
                    .EnableNoBuild()
                    .EnableNoRestore()
                    .SetDescription("Easy manage dependency injection in code when working with Ucommerce.")
                    .SetPackageTags("ucommerce ioc dependency commerce ecommerce")
                    .SetDeterministic(true)
                    .SetIncludeSource(true)
                    .SetIncludeSymbols(true)
                    .SetNoDependencies(true)
                    .EnableDeterministicSourcePaths()
                    .SetVersion(NuGetVersionCustom)
                    .SetOutputDirectory(ArtifactsDirectory / "nuget"));
            });

    Target Push =>
        _ => _
            .DependsOn(Pack)
            .Requires(() => NugetApiUrl)
            .Requires(() => NugetApiKey)
            .Requires(() => Configuration.Equals(Configuration.Release))
            .Requires(() => GitRepository.IsOnMainBranch())
            .Executes(() =>
            {
                NugetDirectory.GlobFiles("*.nupkg")
                    .WhereNotNull()
                    .Where(x => !x.Name.EndsWith("symbols.nupkg"))
                    .ForEach(x =>
                    {
                        DotNetNuGetPush(s => s
                            .SetTargetPath(x)
                            .SetSource(NugetApiUrl)
                            .SetApiKey(NugetApiKey)
                        );
                    });

                NugetDirectory.GlobFiles("*.snupkg")
                    .WhereNotNull()
                    .ForEach(x =>
                    {
                        DotNetNuGetPush(s => s
                            .SetTargetPath(x)
                            .SetSource(NugetApiUrl)
                            .SetApiKey(NugetApiKey)
                        );
                    });
            });

    Target Restore =>
        _ => _
            .Executes(() =>
            {
                DotNetRestore(s => s
                    .SetProjectFile(Solution));
            });

    AbsolutePath SourceDirectory => RootDirectory / "src";

    /// Support plugins are available for:
    ///   - JetBrains ReSharper        https://nuke.build/resharper
    ///   - JetBrains Rider            https://nuke.build/rider
    ///   - Microsoft VisualStudio     https://nuke.build/visualstudio
    ///   - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Compile);
}
