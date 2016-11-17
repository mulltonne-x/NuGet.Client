// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace;
using Microsoft.VisualStudio.Workspace.Extensions;
using Microsoft.VisualStudio.Workspace.Indexing;
using Microsoft.VisualStudio.Workspace.VSIntegration;

namespace NuGet.VisualStudio.Workspace.Extensions.Implementation
{
    /// <summary>
    /// File scanner implementation for Nuget
    /// </summary>
    [ExportFileScanner(
        (FileScannerOptions)SolutionWorkspaceProviderOptions.SupportedAndOnlySolutionWorkspace,
        ProviderType,
        "NugetSupportedProject",
        new string[] { NugetConstants.PackagesConfigFileName, NugetConstants.ProjectJsonFileName, MsBuildProjectExtensionSuffix },
        new Type[] { typeof(IReadOnlyCollection<FileDataValue>) },
        ProviderPriority.Normal)]
    public sealed class NugetFileScanner : IWorkspaceProviderFactory<IFileScanner>, IFileScanner
    {
        /// <summary>
        /// Provider type
        /// </summary>
        public const string ProviderType = "343F7469-E2D3-4F38-973C-79B5D320E249";

        /// <summary>
        /// Suffix of common project extensions
        /// </summary>
        public const string MsBuildProjectExtensionSuffix = "proj";

        /// <inheritdoc/>
        public IFileScanner CreateProvider(IWorkspace workspaceContext)
        {
            return this;
        }

        /// <inheritdoc />
        public async Task<T> ScanContentAsync<T>(string filePath, CancellationToken cancellationToken)
            where T : class
        {            
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (typeof(T) != FileScannerTypeConstants.FileDataValuesType)
            {
                throw new NotImplementedException();
            }

            cancellationToken.ThrowIfCancellationRequested();
            T results = null;

            var fileInfo = new FileInfo(filePath);

            if (NugetConstants.PackagesConfigFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase))
            {
                results = await PackagesConfigDataValueProvider.ScanPackagesConfigContent(filePath, cancellationToken) as T;
            }
            else if (NugetConstants.ProjectJsonFileName.Equals(fileInfo.Name, StringComparison.OrdinalIgnoreCase))
            {
                results = await ProjectJsonDataValueProvider.ScanProjectJsonContent(filePath, cancellationToken) as T;
            }
            else if (!string.IsNullOrEmpty(fileInfo.Extension) && fileInfo.Extension.EndsWith(MsBuildProjectExtensionSuffix, StringComparison.OrdinalIgnoreCase))
            {
                results = await PackageReferenceDataValueProvider.ScanPackageReferenceContent(filePath, cancellationToken) as T;
            }

            return results;
        }
    }
}
