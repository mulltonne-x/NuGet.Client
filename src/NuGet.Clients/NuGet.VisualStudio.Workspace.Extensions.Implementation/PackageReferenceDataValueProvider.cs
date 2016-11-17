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
    /// Data cache of <PackageReference /> items from the project files
    /// </summary>
    internal static class PackageReferenceDataValueProvider
    {
        private const string PackageReferenceElementName = "PackageReference";
        private const string IncludeAttributeName = "Include";

        private static Guid packageReferenceDataValueTypeGuid = new Guid(NugetConstants.PackageReferenceDataValueType);

        /// <summary>
        /// Scans a MSBuild project file to read the contents and returns them as a <see cref="FileDataValue"/> object.
        /// </summary>
        /// <param name="projectFilePath">Path to the project file</param>
        /// <param name="cancellationToken">Cancellation token for the task</param>
        /// <returns>A <see cref="FileDataValue"/> object</returns>
        internal static Task<List<FileDataValue>> ScanPackageReferenceContent(
            string projectFilePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var packageReferenceData = ReadPackageReferenceData(projectFilePath);

            return Task.FromResult(new List<FileDataValue>() { new FileDataValue(packageReferenceDataValueTypeGuid, PackageReferenceElementName, packageReferenceData) });
        }

        private static IEnumerable<PackageReferenceData> ReadPackageReferenceData(string projectFilePath)
        {
            if (string.IsNullOrWhiteSpace(projectFilePath))
            {
                throw new ArgumentNullException(nameof(projectFilePath));
            }

            if (!File.Exists(projectFilePath))
            {
                return null;
            }

            var data = new List<PackageReferenceData>();

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.XmlResolver = null;
                XmlReader xmlReader = XmlReaderHelper.CreateXmlReader(projectFilePath);
                xmlDoc.Load(xmlReader);

                foreach (XmlNode node in xmlDoc.GetElementsByTagName(PackageReferenceElementName))
                {
                    var packageReferenceData = default(PackageReferenceData);

                    packageReferenceData.Name = node.Attributes.GetNamedItem(IncludeAttributeName)?.Value;
                    packageReferenceData.Version = node.FirstChild?.InnerText;

                    data.Add(packageReferenceData);
                }
            }
            catch (XmlException)
            {
                // Invalid project file.
            }

            return data;
        }
    }
}
