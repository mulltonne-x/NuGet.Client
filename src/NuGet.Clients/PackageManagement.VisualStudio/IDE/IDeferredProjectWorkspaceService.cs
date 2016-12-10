// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.VisualStudio.Workspace.Extensions.MSBuild;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.PackageManagement.VisualStudio
{
    public interface IDeferredProjectWorkspaceService
    {
        Task<bool> EntityExists(string filePath);

        Task<IEnumerable<string>> GetProjectReferencesAsync(string projectFilePath);

        Task<IMSBuildProjectDataService> GetMSBuildProjectDataService(string projectFilePath);

        Task<IEnumerable<MSBuildProjectItemData>> GetProjectItemsAsync(IMSBuildProjectDataService dataService, string itemType);

        Task<string> GetProjectPropertyAsync(IMSBuildProjectDataService dataService, string propertyName);
    }
}
