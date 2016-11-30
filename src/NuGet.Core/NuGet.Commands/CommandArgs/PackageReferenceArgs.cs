// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using NuGet.Packaging.Core;

namespace NuGet.Commands
{
    public class PackageReferenceArgs
    {
        public string ProjectCSProj { get; }
        public PackageIdentity PackageIdentity { get; set; }

        public PackageReferenceArgs(string projectCSProj, PackageIdentity packageIdentity)
        {
            if (projectCSProj == null)
            {
                throw new ArgumentNullException(nameof(projectCSProj));
            }
            else if (packageIdentity == null)
            {
                throw new ArgumentNullException(nameof(packageIdentity));
            }
            ProjectCSProj = projectCSProj;
            PackageIdentity = packageIdentity;
        }
    }
}