// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Workspace.Indexing;
using Microsoft.VisualStudio.Workspace.VSIntegration;
using Newtonsoft.Json.Linq;

namespace NuGet.VisualStudio.Workspace.Extensions.Implementation
{
    /// <summary>
    /// Data cache from project.json
    /// </summary>
    internal static class ProjectJsonDataValueProvider
    {
        private static Guid projectJsonDataValueTypeGuid = new Guid(NugetConstants.ProjectJsonDataValueType);

        /// <summary>
        /// Scans a project.json file to read the contents and returns them as a collection of <see cref="FileDataValue"/> objects.
        /// </summary>
        /// <param name="projectJsonFilePath">Path to the project.json file</param>
        /// <param name="cancellationToken">Cancellation token for the task</param>
        /// <returns>Collection of <see cref="FileDataValue"/> objects</returns>
        internal static Task<List<FileDataValue>> ScanProjectJsonContent(
            string projectJsonFilePath,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var jsonData = ReadProjectJsonData(projectJsonFilePath);

            var fileDataValues = new List<FileDataValue>();
            foreach (var data in jsonData)
            {
                fileDataValues.Add(new FileDataValue(projectJsonDataValueTypeGuid, data.Key, data.Value));
            }

            return Task.FromResult(fileDataValues);
        }

        private static IDictionary<string, object> ReadProjectJsonData(string projectJsonFilePath)
        {
            if (string.IsNullOrWhiteSpace(projectJsonFilePath))
            {
                throw new ArgumentNullException(nameof(projectJsonFilePath));
            }

            if (!File.Exists(projectJsonFilePath))
            {
                return null;
            }

            JObject jObject = JsonHelper.DeserializeObjectFromFilePath(projectJsonFilePath);

            return JsonHelper.DeserializeJObject(jObject);
        }
    }
}
