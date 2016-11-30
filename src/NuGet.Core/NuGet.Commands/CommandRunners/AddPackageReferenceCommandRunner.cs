// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;

namespace NuGet.Commands
{
    public class AddPackageReferenceCommandRunner
    {
        public void ExecuteCommand(PackageReferenceArgs packageReferenceArgs)
        {
        }

        public async Task<BuildIntegratedProjectAction> PreviewAddPackageReference(
            BuildIntegratedNuGetProject buildIntegratedProject,
            IEnumerable<NuGetProjectAction> nuGetProjectActions,
            INuGetProjectContext nuGetProjectContext,
            CancellationToken token)
        {
            if (nuGetProjectActions == null)
            {
                throw new ArgumentNullException(nameof(nuGetProjectActions));
            }

            if (buildIntegratedProject == null)
            {
                throw new ArgumentNullException(nameof(buildIntegratedProject));
            }

            if (nuGetProjectContext == null)
            {
                throw new ArgumentNullException(nameof(nuGetProjectContext));
            }

            if (!nuGetProjectActions.Any())
            {
                // Return null if there are no actions.
                return null;
            }

            var stopWatch = Stopwatch.StartNew();
            var projectId = string.Empty;
            buildIntegratedProject.TryGetMetadata<string>(NuGetProjectMetadataKeys.ProjectId, out projectId);

            // Find all sources used in the project actions
            var sources = new HashSet<SourceRepository>(
                nuGetProjectActions.Where(action => action.SourceRepository != null)
                    .Select(action => action.SourceRepository),
                    new SourceRepositoryComparer());

            // Add all enabled sources for the existing packages
            var enabledSources = SourceRepositoryProvider.GetRepositories();

            sources.UnionWith(enabledSources);

            // Read the current lock file if it exists
            LockFile originalLockFile = null;
            var lockFileFormat = new LockFileFormat();

            var lockFilePath = await buildIntegratedProject.GetAssetsFilePathAsync();

            if (File.Exists(lockFilePath))
            {
                originalLockFile = lockFileFormat.Read(lockFilePath);
            }

            var logger = new ProjectContextLogger(nuGetProjectContext);
            var dependencyGraphContext = new DependencyGraphCacheContext(logger);

            // Get Package Spec as json object
            var originalPackageSpec = await DependencyGraphRestoreUtility.GetProjectSpec(buildIntegratedProject, dependencyGraphContext);

            // Create a copy to avoid modifying the original spec which may be shared.
            var updatedPackageSpec = originalPackageSpec.Clone();

            var pathContext = NuGetPathContext.Create(Settings);
            var providerCache = new RestoreCommandProvidersCache();

            // For installs only use cache entries newer than the current time.
            // This is needed for scenarios where a new package shows up in search
            // but a previous cache entry does not yet have it.
            // So we want to capture the time once here, then pass it down to the two
            // restores happening in this flow.
            var now = DateTimeOffset.UtcNow;
            Action<SourceCacheContext> cacheModifier = (cache) => cache.MaxAge = now;

            // If the lock file does not exist, restore before starting the operations
            if (originalLockFile == null)
            {
                var originalRestoreResult = await DependencyGraphRestoreUtility.PreviewRestoreAsync(
                    SolutionManager,
                    buildIntegratedProject,
                    originalPackageSpec,
                    dependencyGraphContext,
                    providerCache,
                    cacheModifier,
                    sources,
                    Settings,
                    logger,
                    token);

                originalLockFile = originalRestoreResult.Result.LockFile;
            }

            foreach (var action in nuGetProjectActions)
            {
                if (action.NuGetProjectActionType == NuGetProjectActionType.Uninstall)
                {
                    // Remove the package from all frameworks and dependencies section.
                    PackageSpecOperations.RemoveDependency(updatedPackageSpec, action.PackageIdentity.Id);
                }
                else if (action.NuGetProjectActionType == NuGetProjectActionType.Install)
                {
                    PackageSpecOperations.AddOrUpdateDependency(updatedPackageSpec, action.PackageIdentity);
                }
            }

            // Restore based on the modified package spec. This operation does not write the lock file to disk.
            var restoreResult = await DependencyGraphRestoreUtility.PreviewRestoreAsync(
                SolutionManager,
                buildIntegratedProject,
                updatedPackageSpec,
                dependencyGraphContext,
                providerCache,
                cacheModifier,
                sources,
                Settings,
                logger,
                token);

            var nugetProjectActionsList = nuGetProjectActions.ToList();

            var allFrameworks = updatedPackageSpec
                .TargetFrameworks
                .Select(t => t.FrameworkName)
                .Distinct()
                .ToList();

            var unsuccessfulFrameworks = restoreResult
                .Result
                .CompatibilityCheckResults
                .Where(t => !t.Success)
                .Select(t => t.Graph.Framework)
                .Distinct()
                .ToList();

            var successfulFrameworks = allFrameworks
                .Except(unsuccessfulFrameworks)
                .ToList();

            var firstAction = nugetProjectActionsList[0];

            // If the restore failed and this was a single package install, try to install the package to a subset of
            // the target frameworks.
            if (nugetProjectActionsList.Count == 1 &&
                firstAction.NuGetProjectActionType == NuGetProjectActionType.Install &&
                successfulFrameworks.Any() &&
                unsuccessfulFrameworks.Any() &&
                !restoreResult.Result.Success &&
                // Exclude upgrades, for now we take the simplest case.
                !PackageSpecOperations.HasPackage(originalPackageSpec, firstAction.PackageIdentity.Id))
            {
                updatedPackageSpec = originalPackageSpec.Clone();

                PackageSpecOperations.AddDependency(
                    updatedPackageSpec,
                    firstAction.PackageIdentity,
                    successfulFrameworks);

                restoreResult = await DependencyGraphRestoreUtility.PreviewRestoreAsync(
                    SolutionManager,
                    buildIntegratedProject,
                    updatedPackageSpec,
                    dependencyGraphContext,
                    providerCache,
                    cacheModifier,
                    sources,
                    Settings,
                    logger,
                    token);
            }

            // Build the installation context
            var originalFrameworks = updatedPackageSpec
                .RestoreMetadata
                .OriginalTargetFrameworks
                .GroupBy(x => NuGetFramework.Parse(x))
                .ToDictionary(x => x.Key, x => x.First());
            var installationContext = new BuildIntegratedInstallationContext(
                successfulFrameworks,
                unsuccessfulFrameworks,
                originalFrameworks);

            InstallationCompatibility.EnsurePackageCompatibility(
                buildIntegratedProject,
                pathContext,
                nuGetProjectActions,
                restoreResult.Result);

            // If this build integrated project action represents only uninstalls, mark the entire operation
            // as an uninstall. Otherwise, mark it as an install. This is important because install operations
            // are a bit more sensitive to errors (thus resulting in rollbacks).
            var actionType = NuGetProjectActionType.Install;
            if (nuGetProjectActions.All(x => x.NuGetProjectActionType == NuGetProjectActionType.Uninstall))
            {
                actionType = NuGetProjectActionType.Uninstall;
            }

            stopWatch.Stop();
            TelemetryServiceUtility.EmitEvent(nuGetProjectContext.TelemetryService,
                string.Format(TelemetryConstants.PreviewBuildIntegratedStepName, projectId),
                stopWatch.Elapsed.TotalSeconds);

            return new BuildIntegratedProjectAction(
                buildIntegratedProject,
                nuGetProjectActions.First().PackageIdentity,
                actionType,
                originalLockFile,
                restoreResult,
                sources.ToList(),
                nugetProjectActionsList,
                installationContext);
        }
    }
}