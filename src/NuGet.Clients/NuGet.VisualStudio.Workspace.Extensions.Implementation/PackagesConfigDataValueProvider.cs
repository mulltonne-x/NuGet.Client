// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.VisualStudio.Workspace.Indexing;
using Microsoft.VisualStudio.Workspace.VSIntegration;

namespace NuGet.VisualStudio.Workspace.Extensions.Implementation
{
    /// <summary>
    /// Data cache from packages.config
    /// </summary>
    internal static class PackagesConfigDataValueProvider
    {
        private const string PackageElementName = "package";
        private const string IdAttributeName = "id";
        private const string VersionAttributeName = "version";
        private const string TargetFrameworkAttributeName = "targetFramework";
        private const string DevelopmentDependencyAttributeName = "developmentDependency";

        private const string FileDataValueName = "Packages.Config";

        private static Guid packagesConfigDataValueTypeGuid = new Guid(NugetConstants.PackagesConfigDataValueType);

        /// <summary>
        /// Scans a packages.config file to read the contents and returns them as a collection of <see cref="FileDataValue"/> objects.
        /// </summary>
        /// <param name="packagesConfigFilePath">Path to the packages.config file</param>
        /// <param name="cancellationToken">Cancellation token for the task</param>
        /// <returns>Collection of <see cref="FileDataValue"/> objects</returns>
        internal static Task<List<FileDataValue>> ScanPackagesConfigContent(
            string packagesConfigFilePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packagesData = ReadPackagesConfigData(packagesConfigFilePath);

            return Task.FromResult(new List<FileDataValue>() { new FileDataValue(packagesConfigDataValueTypeGuid, FileDataValueName, packagesData) });
        }

        private static IEnumerable<PackagesConfigData> ReadPackagesConfigData(string packagesConfigFilePath)
        {
            if (string.IsNullOrWhiteSpace(packagesConfigFilePath))
            {
                throw new ArgumentNullException(nameof(packagesConfigFilePath));
            }

            if (!File.Exists(packagesConfigFilePath))
            {
                return null;
            }

            var data = new List<PackagesConfigData>();

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.XmlResolver = null;
            XmlReader xmlReader = XmlReaderHelper.CreateXmlReader(packagesConfigFilePath);
            xmlDoc.Load(xmlReader);

            foreach (XmlNode node in xmlDoc.GetElementsByTagName(PackageElementName))
            {
                var packagesConfigData = default(PackagesConfigData);

                packagesConfigData.PackageId = node.Attributes.GetNamedItem(IdAttributeName)?.Value;
                packagesConfigData.Version = node.Attributes.GetNamedItem(VersionAttributeName)?.Value;
                packagesConfigData.TargetFramework = node.Attributes.GetNamedItem(TargetFrameworkAttributeName)?.Value;
                packagesConfigData.DevelopmentDependency = node.Attributes.GetNamedItem(DevelopmentDependencyAttributeName)?.Value;

                data.Add(packagesConfigData);
            }

            return data;
        }
    }
}
