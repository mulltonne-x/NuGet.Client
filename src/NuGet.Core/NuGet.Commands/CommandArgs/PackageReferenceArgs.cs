// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging.Core;

namespace NuGet.Commands
{
    public class PackageReferenceArgs
    {
        public string ProjectCSProj { get; }
        public PackageIdentity PackageIdentity { get; set; }
        public ISettings Settings { get; }
        public ILogger Logger { get; }

        public PackageReferenceArgs(string projectCSProj, PackageIdentity packageIdentity, ISettings settings, ILogger logger)
        {
            if (projectCSProj == null)
            {
                throw new ArgumentNullException(nameof(projectCSProj));
            }
            else if (packageIdentity == null)
            {
                throw new ArgumentNullException(nameof(packageIdentity));
            }
            else if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }
            else if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }
            ProjectCSProj = projectCSProj;
            PackageIdentity = packageIdentity;
            Settings = settings;
            Logger = logger;
        }
    }
}