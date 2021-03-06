﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using NuGet.ProjectManagement;
using Task = System.Threading.Tasks.Task;

namespace NuGet.PackageManagement.VisualStudio
{
    /// <summary>
    /// Extends core <see cref="ISolutionManager"/> API with VisualStudio specific methods.
    /// </summary>
    public interface IVsSolutionManager : ISolutionManager
    {
        /// <summary>
        /// Associates instance with loaded VS extension package.
        /// Needed mostly to perform initialization actions when environment is fully
        /// loaded. As a MEF constructor might be called with partially incomplete components.
        /// For instance, UI thread bound actions can't be invoked in a constructor due to high
        /// risk of a deadlock.
        /// </summary>
        /// <param name="site">VS extension package. Provides async access to VS services.</param>
        /// <returns>Asyncronous operation.</returns>
        Task InitializeAsync(IAsyncServiceProvider site);

        /// <summary>
        /// Retrieves <see cref="NuGetProject"/> instance associated with VS project.
        /// Creates new instance if not found in project system cache.
        /// </summary>
        /// <param name="project">VS project.</param>
        /// <param name="projectContext">Context object for new instance creation.</param>
        /// <returns>Existing or new <see cref="NuGetProject"/> instance.</returns>
        Task<NuGetProject> GetOrCreateProjectAsync(EnvDTE.Project project, INuGetProjectContext projectContext);

        /// <summary>
        /// Retrieves instance of <see cref="EnvDTE.Project"/> associated with project name, path, or id.
        /// </summary>
        /// <param name="name">Project name, full path or unique name.</param>
        /// <returns>Desired project object.</returns>
        EnvDTE.Project GetDTEProject(string name);

        /// <summary>
        /// Return true if all projects in the solution have been loaded in background.
        /// </summary>
        bool IsSolutionFullyLoaded { get; }
    }
}
