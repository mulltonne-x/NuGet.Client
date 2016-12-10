// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.ProjectModel;
using System.Threading;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.VisualStudio.Workspace.Extensions.MSBuild;
using System;
using NuGet.LibraryModel;
using NuGet.Versioning;
using NuGet.Commands;
using NuGet.RuntimeModel;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// Utility class to construct restore data for deferred projects.
    /// </summary>
    public static class DeferredProjectRestoreUtility
    {
        private static readonly string IncludeAssets = "IncludeAssets";
        private static readonly string ExcludeAssets = "ExcludeAssets";
        private static readonly string PrivateAssets = "PrivateAssets";
        private static readonly string BaseIntermediateOutputPath = "BaseIntermediateOutputPath";
        private static readonly string PackageReference = "PackageReference";
        private static readonly string ProjectReference = "ProjectReference";
        private static readonly string RuntimeIdentifier = "RuntimeIdentifier";
        private static readonly string RuntimeIdentifiers = "RuntimeIdentifiers";
        private static readonly string RuntimeSupports = "RuntimeSupports";
        private static readonly string TargetFramework = "TargetFramework";
        private static readonly string TargetFrameworks = "TargetFrameworks";
        private static readonly string NuGetTargetFramework = "NuGetTargetFramework";
        private static readonly string PackageTargetFallback = "PackageTargetFallback";

        public static async Task<DeferredProjectRestoreData> GetDeferredProjectsData(
            IDeferredProjectWorkspaceService deferredWorkspaceService,
            IEnumerable<string> deferredProjectsPath,
            CancellationToken token)
        {
            var packageReferencesDict = new Dictionary<PackageReference, List<string>>(new PackageReferenceComparer());
            var packageSpecs = new List<PackageSpec>();

            foreach (var projectPath in deferredProjectsPath)
            {
                // packages.config
                string packagesConfigFilePath = Path.Combine(Path.GetDirectoryName(projectPath), "packages.config");
                bool packagesConfigFileExists = await deferredWorkspaceService.EntityExists(packagesConfigFilePath);

                if (packagesConfigFileExists)
                {
                    // read packages.config and get all package references.
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);
                    using (var stream = new FileStream(packagesConfigFilePath, FileMode.Open, FileAccess.Read))
                    {
                        var reader = new PackagesConfigReader(stream);
                        var packageReferences = reader.GetPackages();

                        foreach (var packageRef in packageReferences)
                        {
                            List<string> projectNames = null;
                            if (!packageReferencesDict.TryGetValue(packageRef, out projectNames))
                            {
                                projectNames = new List<string>();
                                packageReferencesDict.Add(packageRef, projectNames);
                            }

                            projectNames.Add(projectName);
                        }
                    }

                    // create package spec for packages.config based project
                    var packageSpec = await GetPackageSpecForPackagesConfigAsync(deferredWorkspaceService, projectPath);
                    if (packageSpec != null)
                    {
                        packageSpecs.Add(packageSpec);
                    }
                }
                else
                {

                    // project.json
                    string projectJsonFilePath = Path.Combine(Path.GetDirectoryName(projectPath), "project.json");
                    bool projectJsonFileExists = await deferredWorkspaceService.EntityExists(projectJsonFilePath);

                    if (projectJsonFileExists)
                    {
                        // create package spec for project.json based project
                        var packageSpec = await GetPackageSpecForProjectJsonAsync(deferredWorkspaceService, projectPath, projectJsonFilePath);
                        packageSpecs.Add(packageSpec);
                    }
                    else
                    {
                        // package references (CPS or Legacy CSProj)
                        var packageSpec = await GetPackageSpecForPackageReferencesAsync(deferredWorkspaceService, projectPath);
                        if (packageSpec != null)
                        {
                            packageSpecs.Add(packageSpec);
                        }
                    }
                }
            }
            
            return new DeferredProjectRestoreData(packageReferencesDict, packageSpecs);
        }

        private static async Task<PackageSpec> GetPackageSpecForPackagesConfigAsync(IDeferredProjectWorkspaceService deferredWorkspaceService, string projectPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var msbuildProject = EnvDTEProjectUtility.AsMicrosoftBuildEvaluationProject(projectPath);
            var targetFrameworkString = MSBuildProjectUtility.GetTargetFrameworkString(msbuildProject);

            if (targetFrameworkString == null)
            {
                return null;
            }

            var nuGetFramework = new NuGetFramework(targetFrameworkString);

            var packageSpec = new PackageSpec(
                new List<TargetFrameworkInformation>
                {
                    new TargetFrameworkInformation
                    {
                        FrameworkName = nuGetFramework
                    }
                });

            packageSpec.Name = projectName;
            packageSpec.FilePath = projectPath;

            var metadata = new ProjectRestoreMetadata();
            packageSpec.RestoreMetadata = metadata;

            metadata.OutputType = RestoreOutputType.PackagesConfig;
            metadata.ProjectPath = projectPath;
            metadata.ProjectName = projectName;
            metadata.ProjectUniqueName = projectPath;
            metadata.TargetFrameworks.Add(new ProjectRestoreMetadataFrameworkInfo(nuGetFramework));

            await AddProjectReferencesAsync(deferredWorkspaceService, metadata, projectPath);

            return packageSpec;
        }

        private static async Task<PackageSpec> GetPackageSpecForProjectJsonAsync(
            IDeferredProjectWorkspaceService deferredWorkspaceService,
            string projectPath,
            string projectJsonFilePath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);
            var packageSpec = JsonPackageSpecReader.GetPackageSpec(projectName, projectJsonFilePath);

            var metadata = new ProjectRestoreMetadata();
            packageSpec.RestoreMetadata = metadata;

            metadata.OutputType = RestoreOutputType.UAP;
            metadata.ProjectPath = projectPath;
            metadata.ProjectJsonPath = packageSpec.FilePath;
            metadata.ProjectName = packageSpec.Name;
            metadata.ProjectUniqueName = projectPath;

            foreach (var framework in packageSpec.TargetFrameworks.Select(e => e.FrameworkName))
            {
                metadata.TargetFrameworks.Add(new ProjectRestoreMetadataFrameworkInfo(framework));
            }

            await AddProjectReferencesAsync(deferredWorkspaceService, metadata, projectPath);

            return packageSpec;
        }

        private static async Task<PackageSpec> GetPackageSpecForPackageReferencesAsync(
            IDeferredProjectWorkspaceService deferredWorkspaceService, 
            string projectPath)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectPath);

            var msbuildProjectDataService = await deferredWorkspaceService.GetMSBuildProjectDataService(projectPath);

            var packageReferences = (await deferredWorkspaceService.GetProjectItemsAsync(msbuildProjectDataService, PackageReference)).ToList();
            if (packageReferences.Count == 0)
            {
                return null;
            }

            var targetFrameworks = await GetNuGetFrameworks(deferredWorkspaceService, msbuildProjectDataService, projectPath);

            if (targetFrameworks.Count == 0)
            {
                return null;
            }

            var crossTargeting = targetFrameworks.Count > 1;

            var tfi = new TargetFrameworkInformation
            {
                FrameworkName = targetFrameworks.First()
            };

            var ptf = await deferredWorkspaceService.GetProjectPropertyAsync(msbuildProjectDataService, PackageTargetFallback);
            if (!string.IsNullOrEmpty(ptf))
            {
                var fallBackList = ptf.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NuGetFramework.Parse).ToList();

                if (fallBackList.Count > 0)
                {
                    tfi.FrameworkName = new FallbackFramework(tfi.FrameworkName, fallBackList);
                }

                tfi.Imports = fallBackList;
            }

            tfi.Dependencies.AddRange(
                packageReferences.Select(ToPackageLibraryDependency));

            var tfis = new TargetFrameworkInformation[] { tfi };

            var projectReferences = (await deferredWorkspaceService.GetProjectItemsAsync(msbuildProjectDataService, ProjectReference))
                .Select(item => ToProjectRestoreReference(item, projectPath));

            var packageSpec = new PackageSpec(tfis)
            {
                Name = projectName,
                FilePath = projectPath,
                RestoreMetadata = new ProjectRestoreMetadata
                {
                    ProjectName = projectName,
                    ProjectUniqueName = projectPath,
                    ProjectPath = projectPath,
                    OutputPath = await deferredWorkspaceService.GetProjectPropertyAsync(msbuildProjectDataService, BaseIntermediateOutputPath),
                    OutputType = RestoreOutputType.NETCore,
                    TargetFrameworks = new List<ProjectRestoreMetadataFrameworkInfo>()
                    {
                        new ProjectRestoreMetadataFrameworkInfo(tfis[0].FrameworkName)
                        {
                            ProjectReferences = projectReferences?.ToList()
                        }
                    },
                    OriginalTargetFrameworks = tfis
                        .Select(tf => tf.FrameworkName.GetShortFolderName())
                        .ToList(),
                    CrossTargeting = crossTargeting
                },
                RuntimeGraph = await GetRuntimeGraph(deferredWorkspaceService, msbuildProjectDataService)
            };

            return packageSpec;
        }

        private static async Task<RuntimeGraph> GetRuntimeGraph(
            IDeferredProjectWorkspaceService deferredWorkspaceService,
            IMSBuildProjectDataService dataService)
        {
            var runtimes = Enumerable.Empty<string>();

            var runtimeIdentifier = await deferredWorkspaceService.GetProjectPropertyAsync(dataService, RuntimeIdentifier);
            var runtimeIdentifiers = await deferredWorkspaceService.GetProjectPropertyAsync(dataService, RuntimeIdentifiers);

            if (!string.IsNullOrEmpty(runtimeIdentifier))
            {
                runtimes.Concat(new[] { runtimeIdentifier });
            }

            if (!string.IsNullOrEmpty(runtimeIdentifiers))
            {
                runtimes.Concat(runtimeIdentifiers.Split(';').Where(s => !string.IsNullOrEmpty(s)));
            }

            var supports = (await deferredWorkspaceService.GetProjectPropertyAsync(dataService, RuntimeSupports))
                .Split(';')
                .Where(s => !string.IsNullOrEmpty(s));

            return new RuntimeGraph(
                runtimes.Select(runtime => new RuntimeDescription(runtime)),
                supports.Select(support => new CompatibilityProfile(support)));
        }

        private static ProjectRestoreReference ToProjectRestoreReference(MSBuildProjectItemData item, string projectPath)
        {
            var referencePath = Path.GetFullPath(
                Path.Combine(
                    Path.GetDirectoryName(projectPath), item.EvaluatedInclude));

            var reference = new ProjectRestoreReference()
            {
                ProjectUniqueName = referencePath,
                ProjectPath = referencePath
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                reference,
                includeAssets: GetPropertyValueOrDefault(item, IncludeAssets),
                excludeAssets: GetPropertyValueOrDefault(item, ExcludeAssets),
                privateAssets: GetPropertyValueOrDefault(item, PrivateAssets));

            return reference;
        }

        private static LibraryDependency ToPackageLibraryDependency(MSBuildProjectItemData item)
        {
            var dependency = new LibraryDependency
            {
                LibraryRange = new LibraryRange(
                    name: item.EvaluatedInclude,
                    versionRange: GetVersionRange(item),
                    typeConstraint: LibraryDependencyTarget.Package)
            };

            MSBuildRestoreUtility.ApplyIncludeFlags(
                dependency,
                includeAssets: GetPropertyValueOrDefault(item, IncludeAssets),
                excludeAssets: GetPropertyValueOrDefault(item, ExcludeAssets),
                privateAssets: GetPropertyValueOrDefault(item, PrivateAssets));

            return dependency;
        }

        private static VersionRange GetVersionRange(MSBuildProjectItemData item)
        {
            var versionRange = GetPropertyValueOrDefault(item, "Version");

            if (!string.IsNullOrEmpty(versionRange))
            {
                return VersionRange.Parse(versionRange);
            }

            return VersionRange.All;
        }

        private static string GetPropertyValueOrDefault(
            MSBuildProjectItemData item, string propertyName, string defaultValue = "")
        {
            if (item.Metadata.Keys.Contains(propertyName))
            {
                return item.Metadata[propertyName];
            }

            return defaultValue;
        }

        private static async Task<List<NuGetFramework>> GetNuGetFrameworks(
            IDeferredProjectWorkspaceService deferredWorkspaceService,
            IMSBuildProjectDataService dataService,
            string projectPath)
        {
            var targetFramework = await deferredWorkspaceService.GetProjectPropertyAsync(dataService, TargetFramework);
            if (!string.IsNullOrEmpty(targetFramework))
            {
                return new List<NuGetFramework> { NuGetFramework.Parse(targetFramework) };
            }

            targetFramework = await deferredWorkspaceService.GetProjectPropertyAsync(dataService, TargetFrameworks);
            if (!string.IsNullOrEmpty(targetFramework))
            {
                return targetFramework
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(NuGetFramework.Parse).ToList();
            }

            var nuGetTargetFramework = await deferredWorkspaceService.GetProjectPropertyAsync(dataService, NuGetTargetFramework);
            if (!string.IsNullOrEmpty(nuGetTargetFramework))
            {
                return new List<NuGetFramework> { NuGetFramework.ParseFrameworkName(nuGetTargetFramework, DefaultFrameworkNameProvider.Instance) };
            }

            var msbuildProject = EnvDTEProjectUtility.AsMicrosoftBuildEvaluationProject(projectPath);
            var targetFrameworkString = MSBuildProjectUtility.GetTargetFrameworkString(msbuildProject);

            return new List<NuGetFramework> { new NuGetFramework(targetFrameworkString) };
        }

        private static async Task AddProjectReferencesAsync(
            IDeferredProjectWorkspaceService deferredWorkspaceService,
            ProjectRestoreMetadata metadata,
            string projectPath)
        {
            var references = await deferredWorkspaceService.GetProjectReferencesAsync(projectPath);

            foreach (var reference in references)
            {
                var restoreReference = new ProjectRestoreReference()
                {
                    ProjectPath = reference,
                    ProjectUniqueName = reference
                };

                foreach (var frameworkInfo in metadata.TargetFrameworks)
                {
                    frameworkInfo.ProjectReferences.Add(restoreReference);
                }
            }
        }
    }
}
