// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Commands
{
    public class AddPackageReferenceCommandRunner
    {
        public void ExecuteCommand(PackageReferenceArgs packageReferenceArgs)
        {
        }

        public async Task<IReadOnlyList<RestoreSummary>> PreviewAddPackageReference(PackageReferenceArgs packageReferenceArgs)
        {
            if (packageReferenceArgs == null)
            {
                throw new ArgumentNullException(nameof(packageReferenceArgs));
            }
            using (var cacheContext = new SourceCacheContext())
            {
                cacheContext.NoCache = false;
                cacheContext.IgnoreFailedSources = true;

                var sourceProvider = new CachingSourceProvider(new PackageSourceProvider(packageReferenceArgs.Settings));
                var restoreContext = new RestoreArgs()
                {
                    CacheContext = cacheContext,
                    LockFileVersion = LockFileFormat.Version,
                    DisableParallel = false,
                    Log = packageReferenceArgs.Logger,
                    MachineWideSettings = new XPlatMachineWideSetting(),
                    CachingSourceProvider = sourceProvider
                };
                return await RestoreRunner.Run(restoreContext);
            }
        }
    }
}