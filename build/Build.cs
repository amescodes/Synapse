using System.Collections.Generic;
using System.IO;
using System.Linq;

using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.GitVersion;
using Nuke.Common.Tools.ILRepack;
using Nuke.Common.Tools.NuGet;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.IO.PathConstruction;
using static Nuke.Common.Tools.DotNet.DotNetTasks;
using static Nuke.Common.Tools.ILRepack.ILRepackTasks;
using static Nuke.Common.Tools.NuGet.NuGetTasks;

using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

[GitHubActions(
    name: "cicd",
    GitHubActionsImage.WindowsLatest,
    FetchDepth = 0,
    OnPushBranches = new[] { "main", "develop" },
    InvokedTargets = new[] { nameof(Build.Pack) },
    ImportSecrets = new[] { nameof(SYNAPSE_NUGET_API_KEY) },
    EnableGitHubToken = true,
    PublishArtifacts = true,
    CacheKeyFiles = new[] { "global.json", "./**/*.csproj" }
)]
class Build : NukeBuild
{
    [Solution]
    readonly Solution Solution;

    [Secret][Parameter] readonly string SYNAPSE_NUGET_API_KEY;

    [GitVersion(UpdateAssemblyInfo = true, UpdateBuildNumber = true, NoFetch = true)]
    readonly GitVersion GitVersion;

    string Version => GitVersion.NuGetVersionV2;

    public static int Main() => Execute<Build>(x => x.Compile);

    [Parameter("Configuration to build - Default is 'Debug' (local) or 'Release' (server)")]
    readonly Configuration Configuration = IsLocalBuild ? Configuration.Debug : Configuration.Release;

    AbsolutePath ClientDirectory => RootDirectory / "Synapse.Client";
    AbsolutePath ServerDirectory => RootDirectory / "Synapse.Revit";
    AbsolutePath NugetOutputDirectory => RootDirectory / "nuget";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            ClientDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);
            ServerDirectory.GlobDirectories("**/bin", "**/obj").ForEach(DeleteDirectory);

            EnsureCleanDirectory(NugetOutputDirectory);
        });

    Target Restore => _ => _
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Restore)
        .Before(Pack)
        //.Produces(ClientDirectory / "**/*.dll")
        //.Produces(ServerDirectory / "**/*.dll")
        .Executes(() =>
        {
            Project synapseClient = Solution.GetProject("Synapse.Client");
            DotNetBuild(_ => _
                .SetProjectFile(synapseClient)
                .SetNoRestore(true)
                .SetConfiguration(Configuration));

            Project synapseServer = Solution.GetProject("Synapse.Revit");
            DotNetBuild(_ => _
                .SetProjectFile(synapseServer)
                .SetNoRestore(true)
                .SetConfiguration(Configuration));
            MergeRevitServerDllsWithILRepack();
        });

    Target Pack => _ => _
        .DependsOn(Compile)
        .Consumes(Compile)
        .Produces(NugetOutputDirectory / "*.nupkg")
        .Executes(() =>
        {
            string iconFileName = "icon.png";
            string iconPath = Solution.Directory / "images" / iconFileName;
            if (File.Exists(iconPath))
            {
                if (!Directory.Exists(NugetOutputDirectory))
                {
                    Directory.CreateDirectory(NugetOutputDirectory);
                }
                File.Copy(iconPath, NugetOutputDirectory / iconFileName, true);
                File.Copy(iconPath, NugetOutputDirectory / iconFileName, true);
            }

            // client
            Project synapseClient = Solution.GetProject("Synapse.Client");
            DotNetPack(_ => _
                .SetProject(synapseClient)
                .SetConfiguration(Configuration)
                .EnableNoBuild()
                .EnableNoRestore()
                .SetAuthors("ames codes")
                .SetPackageProjectUrl("https://github.com/amescodes/Synapse")
                .SetDescription("Client package for Revit Synapse library.")
                .SetPackageTags("revit grpc client server communication")
                .SetPackageIconUrl(iconPath)
                .SetVersion(Version)
                .SetOutputDirectory(NugetOutputDirectory));

            // revit server
            ManifestContentFiles iconFile = new ManifestContentFiles();
            iconFile.Include = iconFileName;

            IReadOnlyCollection<string> serverBuildDirStr = GlobDirectories(ServerDirectory, $"bin/{Configuration}/**");
            AbsolutePath serverBuildDir = (AbsolutePath)serverBuildDirStr.MaxBy(p => p.Length);

            File.Copy(iconPath, serverBuildDir / iconFileName, true);

            ManifestMetadata nugetPackageMetadata = new ManifestMetadata()
            {
                Id = "Synapse.Revit",
                Authors = new[] { "ames codes" },
                Description =
                    "Server package for Revit Synapse library. This package should be loaded into the Revit addin.",
                Tags = "revit grpc client server communication",
                Icon = iconFileName,
                Version = NuGetVersion.Parse(Version), // sets during pack
                Repository = new RepositoryMetadata("git", "https://github.com/amescodes/Synapse", "main", ""),
                Copyright = "Copyright © 2022 ames codes",
            };
            Manifest nuspecFile = NuGet.Packaging.Manifest.Create(nugetPackageMetadata);
            nuspecFile.Files.Add(new ManifestFile() { Source = "Synapse.Revit.dll", Target = "lib" });
            if (Configuration == Configuration.Debug)
            {
                nuspecFile.Files.Add(new ManifestFile() { Source = "Synapse.Revit.pdb", Target = "lib" });
            }
            nuspecFile.Files.Add(new ManifestFile() { Source = "grpc_csharp_ext.x64.dll", Target = "lib" });
            nuspecFile.Files.Add(new ManifestFile() { Source = iconFileName, Target = "." });

            AbsolutePath nuspecFilePath = NugetOutputDirectory / "Synapse.Revit.nuspec";
            using (Stream nuspecStream = new FileStream(nuspecFilePath, FileMode.Create, FileAccess.Write))
            {
                nuspecFile.Save(nuspecStream);
            }

            NuGetPack(_ => _
                .SetConfiguration(Configuration)
                .SetTargetPath(nuspecFilePath)
                .SetBasePath(serverBuildDir)
                .SetBuild(false)
                .SetIncludeReferencedProjects(false)
                .SetVersion(Version)
                .SetOutputDirectory(NugetOutputDirectory));
        });

    void MergeRevitServerDllsWithILRepack()
    {
        IReadOnlyCollection<string> serverBuildDirStr = GlobDirectories(ServerDirectory, $"**/**");
        AbsolutePath serverBuildDir = (AbsolutePath)serverBuildDirStr.MaxBy(p => p.Length);
        AbsolutePath synapseDllFile = serverBuildDir / "Synapse.Revit.dll";
        string[] inputAssemblies = new string[]
        {
            synapseDllFile,
            serverBuildDir / "Google.Protobuf.dll",
            serverBuildDir / "Grpc.Core.dll",
            serverBuildDir / "Grpc.Core.Api.dll",
            serverBuildDir / "Newtonsoft.Json.dll",
            serverBuildDir / "System.Buffers.dll",
            serverBuildDir / "System.Memory.dll",
            serverBuildDir / "System.Numerics.Vectors.dll",
            serverBuildDir / "System.Runtime.CompilerServices.Unsafe.dll",
        };

        ILRepack(_ => _
                .SetAssemblies(inputAssemblies)
                .SetLib(serverBuildDir)
                .SetVersion(GitVersion.AssemblySemFileVer)
                .SetOutput(synapseDllFile)
                .SetInternalize(true)
                .SetCopyAttributes(true)
                .SetUnion(true)
                .SetAllowMultiple(true)
                .SetParallel(false)
                .SetVerbose(true)
                .SetLogFile(serverBuildDir / "log_ilrepack-revit.txt")
        );

        // delete everything but synapse revit dll
        string[] files = Directory.GetFiles(serverBuildDir);
        foreach (string filePath in files)
        {
            if (filePath.Equals(synapseDllFile) ||
                filePath.EndsWith("grpc_csharp_ext.x64.dll") ||
                // keep pdb unless in release mode
                (Configuration != Configuration.Release && filePath.EndsWith("Synapse.Revit.pdb")) ||
                !File.Exists(filePath))
            {
                continue;
            }

            File.Delete(filePath);
        }
    }
}
